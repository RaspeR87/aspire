FROM quay.io/keycloak/keycloak:26.3

USER root

# Copy all HSM files (including the libstdc++)
COPY hsm /opt/utimaco

# Copy cs_pkcs11_R3.cfg to /etc so the HSM library can find it
RUN cp /opt/utimaco/cs_pkcs11_R3.cfg /etc/cs_pkcs11_R3.cfg

# Link PKCS#11 lib globally and fix libstdc++ symlink
RUN ln -s /opt/utimaco/libcs_pkcs11_R3.so /usr/lib/libcs_pkcs11_R3.so || true && \
    cp /opt/utimaco/java.security /etc/pki/java/java.security && \
    cp /opt/utimaco/libstdc++.so.6.0.29 /usr/lib64/libstdc++.so.6.0.29 && \
    ln -s /usr/lib64/libstdc++.so.6.0.29 /usr/lib64/libstdc++.so.6

# Set environment variable so the Utimaco lib can locate its config
ENV CSLIBPATH=/etc/cs_pkcs11_R3.cfg

# Create the logs directory expected by cs_pkcs11_R3.cfg
RUN mkdir -p /opt/utimaco/logs && chmod 777 /opt/utimaco/logs

USER keycloak
