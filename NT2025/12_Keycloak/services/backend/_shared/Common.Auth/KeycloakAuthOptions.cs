namespace Common.Auth;

public sealed class KeycloakAuthOptions
{
    // Keycloak
    public string KeycloakBase { get; set; } = "http://localhost:8080"; // no trailing slash
    public string Realm        { get; set; } = "platform";
    public string Audience     { get; set; } = "platform-be";

    // Docs (Scalar) OIDC client
    public string DocsClientId     { get; set; } = "platform-docs";
    public string DocsClientSecret { get; set; } = ""; // supply via env/secrets
    public string DocsCookieName   { get; set; } = "docs_auth";

    // Scalar OAuth2 client for Try-It
    public string ScalarClientId { get; set; } = "platform-scalar";

    // Toggle docs
    public bool EnableDocs { get; set; } = true;

    // CORS (dev)
    public string[] DevCorsOrigins { get; set; } = Array.Empty<string>();

    // TLS
    public bool RequireHttpsMetadata { get; set; } = false;
}
