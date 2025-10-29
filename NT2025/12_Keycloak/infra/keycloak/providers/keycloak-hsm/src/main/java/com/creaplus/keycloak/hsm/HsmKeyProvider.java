// HsmKeyProvider.java
package com.creaplus.keycloak.hsm;

import org.keycloak.crypto.Algorithm;
import org.keycloak.crypto.KeyStatus;
import org.keycloak.crypto.KeyUse;
import org.keycloak.crypto.KeyType;
import org.keycloak.crypto.KeyWrapper;
import org.keycloak.models.KeycloakSession;

import java.security.Key;
import java.security.KeyStore;
import java.security.PublicKey;
import java.security.cert.X509Certificate;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.List;
import java.util.stream.Stream;

public class HsmKeyProvider implements org.keycloak.keys.KeyProvider {

    private final List<KeyWrapper> keys;

    public HsmKeyProvider(KeycloakSession session, KeyStore ks, String sigAlias, String encAlias) {
        this.keys = new ArrayList<>();

        try {
            if (sigAlias != null && ks.containsAlias(sigAlias)) {
                keys.add(buildWrapper(ks, sigAlias, KeyUse.SIG, Algorithm.RS256));
            }
            if (encAlias != null && ks.containsAlias(encAlias)) {
                keys.add(buildWrapper(ks, encAlias, KeyUse.ENC, Algorithm.RSA_OAEP)); // or RSA_OAEP_256
            }

            // Fallback: if nothing configured, load *all* aliases and infer by name
            if (keys.isEmpty()) {
                for (Enumeration<String> e = ks.aliases(); e.hasMoreElements(); ) {
                    String alias = e.nextElement();
                    KeyUse use = alias.toLowerCase().contains("enc") ? KeyUse.ENC : KeyUse.SIG;
                    String alg = (use == KeyUse.SIG) ? Algorithm.RS256 : Algorithm.RSA_OAEP;
                    keys.add(buildWrapper(ks, alias, use, alg));
                }
            }
        } catch (Exception ex) {
            throw new RuntimeException("Failed to load HSM keys", ex);
        }
    }

    private static KeyWrapper buildWrapper(KeyStore ks, String alias, KeyUse use, String alg) throws Exception {
        X509Certificate cert = (X509Certificate) ks.getCertificate(alias);
        PublicKey publicKey = cert.getPublicKey();
        Key privateKey = ks.getKey(alias, null); // already logged-in keystore

        KeyWrapper w = new KeyWrapper();
        w.setType(KeyType.RSA);
        w.setUse(use);
        w.setAlgorithm(alg);
        w.setCertificate(cert);
        w.setPublicKey(publicKey);
        w.setPrivateKey(privateKey);
        w.setStatus(KeyStatus.ACTIVE);

        // Stable kid (thumbprint works fine)
        String kid = thumbprint(cert); // implement below or use a KC util if you add the dep
        w.setKid(kid);

        return w;
    }

    private static String thumbprint(X509Certificate cert) throws Exception {
        var md = java.security.MessageDigest.getInstance("SHA-256");
        byte[] der = cert.getEncoded();
        byte[] digest = md.digest(der);
        StringBuilder sb = new StringBuilder();
        for (byte b : digest) sb.append(String.format("%02x", b));
        return sb.toString();
    }

    @Override public Stream<KeyWrapper> getKeysStream() { return keys.stream(); }
    @Override public void close() { }
}
