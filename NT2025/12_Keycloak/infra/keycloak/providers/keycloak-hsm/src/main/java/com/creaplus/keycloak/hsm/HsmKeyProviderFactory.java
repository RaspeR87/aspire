// HsmKeyProviderFactory.java
package com.creaplus.keycloak.hsm;

import org.keycloak.Config;
import org.keycloak.component.ComponentModel;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;
import org.keycloak.provider.ProviderConfigProperty;
import org.keycloak.keys.KeyProviderFactory;

import java.io.FileInputStream;
import java.io.InputStream;
import java.security.KeyStore;
import java.security.Provider;
import java.security.Security;
import java.util.Arrays;
import java.util.List;

public class HsmKeyProviderFactory implements KeyProviderFactory<HsmKeyProvider> {

    private static final List<ProviderConfigProperty> PROPS = Arrays.asList(
        prop("pin", "HSM PIN", "PIN for PKCS#11 session", ProviderConfigProperty.PASSWORD),
        prop("sigAlias", "Signing alias", "Alias for SIG key in HSM", ProviderConfigProperty.STRING_TYPE),
        prop("encAlias", "Encryption alias", "Alias for ENC key in HSM", ProviderConfigProperty.STRING_TYPE)
    );

    private static ProviderConfigProperty prop(String name, String label, String help, String type) {
        ProviderConfigProperty p = new ProviderConfigProperty();
        p.setName(name); p.setLabel(label); p.setHelpText(help); p.setType(type); return p;
    }

    @Override
    public HsmKeyProvider create(KeycloakSession session, ComponentModel model) {
        try {
            String cfg = "/opt/utimaco/sunpkcs11-utimaco.cfg";
            String pin = model.get("pin");
            String sigAlias = model.get("sigAlias");
            String encAlias = model.get("encAlias");

            // Ensure SunPKCS11 provider is registered from config
            Provider p = Security.getProvider("SunPKCS11-UtimacoHSM");
            if (p == null) {
                try (InputStream is = new FileInputStream(cfg)) {
                    Provider base = Security.getProvider("SunPKCS11");
                    p = base.configure(cfg);
                    Security.addProvider(p);
                }
            }

            KeyStore ks = KeyStore.getInstance("PKCS11", p);
            ks.load(null, pin != null ? pin.toCharArray() : null);

            // (Optional) log aliases
            var e = ks.aliases(); System.out.println("🔐 PKCS#11 aliases:");
            while (e.hasMoreElements()) System.out.println(" - " + e.nextElement());

            return new HsmKeyProvider(session, ks, sigAlias, encAlias);
        } catch (Exception e) {
            throw new RuntimeException("Could not load PKCS#11 keystore", e);
        }
    }

    @Override public void init(Config.Scope config) { }
    @Override public void postInit(KeycloakSessionFactory factory) { }
    @Override public void close() { }
    @Override public String getId() { return "hsm"; }
    @Override public String getHelpText() { return "HSM-backed RSA keys via PKCS#11"; }
    @Override public List<ProviderConfigProperty> getConfigProperties() { return PROPS; }
}
