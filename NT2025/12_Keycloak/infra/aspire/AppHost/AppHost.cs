using AppHost.Extensions.OTEL;
using Aspire.Hosting;
using DotNetEnv;

var builder = DistributedApplication.CreateBuilder(args);

// Enables Docker publisher
builder.AddDockerComposeEnvironment("instance-82");

Env.Load("../.env");

// ---------- paths ----------
var solutionRootDirectory = Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory())!.FullName)!.FullName;
var feRootDirectory = Path.Combine(solutionRootDirectory, "..", "services", "frontend");
var dockerfileAbs   = Path.Combine(feRootDirectory, "Dockerfile.dev.nuxt");

// ---------- RUN_MODE parsing ----------
var runModeRaw = (builder.Configuration["RUN_MODE"] ?? "infra-only").ToLowerInvariant();
var tokens = runModeRaw
    .Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(t => t.Trim())
    .ToHashSet();

bool platformBE = tokens.Contains("platform:be") || tokens.Contains("platform:be+fe");
bool platformFE = tokens.Contains("platform:be+fe");
bool portalBE   = tokens.Contains("portal:be")   || tokens.Contains("portal:be+fe");
bool portalFE   = tokens.Contains("portal:be+fe");

#region Observability

// ============ Observability ============
var observability_group = builder.AddGroup("e-observability");

var otelCollector = builder.AddOpenTelemetryCollector("e01-otelcollector", "../../observability/collector_config.yaml");
otelCollector.WithParentRelationship(observability_group);

#endregion

#region HSM

// ============ HSM ============
var hsm_group = builder.AddGroup("d-hsm");
var hsmSim = builder.AddContainer("d01-hsm-sim", "registry.local/utimaco/sim:6.0-debian")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithVolume("hsm_sim_devices", "/opt/utimaco/devices")
    .WithEndpoint(33001, 3001);
hsmSim.WithParentRelationship(hsm_group);

#endregion

#region Keycloak

var keycloak_group = builder.AddGroup("a-keycloak");

var keycloak_db = builder.AddContainer("a01-keycloak-db", "postgres:17.6-alpine3.22")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithEnvironment("POSTGRES_USER", Environment.GetEnvironmentVariable("KEYCLOAK_DB_USER"))
    .WithEnvironment("POSTGRES_PASSWORD", Environment.GetEnvironmentVariable("KEYCLOAK_DB_PASSWORD"))
    .WithEnvironment("POSTGRES_DB", Environment.GetEnvironmentVariable("KEYCLOAK_DB"))
    .WithVolume("keycloak_db_data", "/var/lib/postgresql/data")
    .WithEndpoint(15432, 5432, name: "port");
keycloak_db.WithParentRelationship(keycloak_group);

static string? authHostname(bool isManagement = false) => isManagement ?
    Environment.GetEnvironmentVariable("KEYCLOAK_MANAGEMENT_URL") :
    Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL");

var keycloak = builder.AddDockerfile("a02-keycloak", "../../keycloak")
    .WaitFor(keycloak_db)
    .WaitFor(otelCollector)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithContainerRuntimeArgs("--restart", "on-failure:5")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN"))
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", Environment.GetEnvironmentVariable("KEYCLOAK_ADMIN_PASSWORD"))
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_SCHEMA", Environment.GetEnvironmentVariable("KEYCLOAK_DB_SCHEMA"))
    .WithEnvironment("KC_DB_URL_HOST", keycloak_db.Resource.Name)
    .WithEnvironment("KC_DB_URL_PORT", keycloak_db.Resource.GetEndpoint("port").TargetPort.ToString())
    .WithEnvironment("KC_DB_USERNAME", Environment.GetEnvironmentVariable("KEYCLOAK_DB_USER"))
    .WithEnvironment("KC_DB_PASSWORD", Environment.GetEnvironmentVariable("KEYCLOAK_DB_PASSWORD"))
    .WithEnvironment("KC_DB_DATABASE", Environment.GetEnvironmentVariable("KEYCLOAK_DB"))
    .WithEnvironment("KC_HOSTNAME", authHostname())
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithEnvironment("JAVA_OPTS_APPEND", "-Djava.security.debug=properties")
    .WithEnvironment("JAVA_TOOL_OPTIONS",
        "-javaagent:/otel/opentelemetry-javaagent.jar " +
        "-Dotel.service.name=keycloak " +
        "-Dotel.exporter.otlp.protocol=http/protobuf " +
        $"-Dotel.exporter.otlp.endpoint=http://{otelCollector.Resource.Name}:4318 " +
        "-Dotel.resource.attributes=service.version=26.3 " +
        "-Djava.security.properties=/opt/utimaco/java.security")
    .WithContainerFiles("/otel/opentelemetry-javaagent.jar", "../../keycloak/otel/opentelemetry-javaagent.jar")
    .WithContainerFiles("/opt/keycloak/providers/keycloak-hsm-provider-1.0.0.jar", "../../keycloak/providers/keycloak-hsm/target/deploy/keycloak-hsm-provider-1.0.0.jar")
    .WithContainerFiles("/opt/keycloak/data/import", "../../keycloak/realms/dev")
    .WithHttpEndpoint(8080, 8080, "http")
    .WithHttpEndpoint(9000, 9000, "management")
    .WithUrlForEndpoint("http", url => { url.DisplayText = "Admin Console (HTTP)"; url.Url += "/"; })
    .WithUrlForEndpoint("management", url => { url.DisplayText = "Management Interface"; url.Url += "/"; })
    .WithArgs("start-dev", "--import-realm", "--features=scripts", "--spi-script-upload-enabled=true")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck($"{authHostname(true)}/health/ready");
keycloak.WithParentRelationship(keycloak_group);

#endregion

#region Platform

IResourceBuilder<GroupResource>? platformGroup = null;

if (platformBE || platformFE)
{
    var platform_db = builder.AddContainer("b01-platform-db", "postgres:17.6-alpine3.22")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithEnvironment("POSTGRES_USER", Environment.GetEnvironmentVariable("PLATFORM_DB_USER"))
        .WithEnvironment("POSTGRES_PASSWORD", Environment.GetEnvironmentVariable("PLATFORM_DB_PASSWORD"))
        .WithEnvironment("POSTGRES_DB", Environment.GetEnvironmentVariable("PLATFORM_DB"))
        .WithVolume("platform_db_data", "/var/lib/postgresql/data")
        .WithEndpoint(25432, 5432, name: "port");
    AttachToGroup(platform_db, ref platformGroup, "b-platform");

    var platform_be = builder.AddProject<Projects.Platform>("b02-platform-be")
        .WaitFor(platform_db)
        .WithEnvironment("Auth__KeycloakBase", Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL"))
        .WithEnvironment("Auth__Realm", Environment.GetEnvironmentVariable("PLATFORM_REALM"))
        .WithEnvironment("Auth__Audience", Environment.GetEnvironmentVariable("PLATFORM_AUDIENCE"))
        .WithEnvironment("Auth__DocsClientId", Environment.GetEnvironmentVariable("PLATFORM_DOCS_CLIENT_ID"))
        .WithEnvironment("Auth__DocsClientSecret", Environment.GetEnvironmentVariable("PLATFORM_DOCS_CLIENT_SECRET"))
        .WithEnvironment("Auth__DocsCookieName", Environment.GetEnvironmentVariable("PLATFORM_DOCS_COOKIE_NAME"))
        .WithEnvironment("Auth__ScalarClientId", Environment.GetEnvironmentVariable("PLATFORM_SCALAR_CLIENT_ID"))
        .WithEnvironment("Auth__EnableDocs", true.ToString())
        .WithEnvironment("Auth__DevCorsOrigins", "https://localhost:7284,http://localhost:5133,http://localhost:4300")
        .WithEnvironment("Auth__RequireHttpsMetadata", false.ToString())
        .WithExternalHttpEndpoints();
    AttachToGroup(platform_be, ref platformGroup, "b-platform");

    if (platformFE)
    {
        var platform_fe = AddFrontend("b03-platform-fe", "platform", 4300);
        AttachToGroup(platform_fe, ref platformGroup, "b-platform");
    }
}

#endregion

#region Portal

IResourceBuilder<GroupResource>? portalGroup = null;

if (portalBE || portalFE)
{
    var portal_db = builder.AddContainer("c01-portal-db", "postgres:17.6-alpine3.22")
        .WithLifetime(ContainerLifetime.Persistent)
        .WithEnvironment("POSTGRES_USER", Environment.GetEnvironmentVariable("PORTAL_DB_USER"))
        .WithEnvironment("POSTGRES_PASSWORD", Environment.GetEnvironmentVariable("PORTAL_DB_PASSWORD"))
        .WithEnvironment("POSTGRES_DB", Environment.GetEnvironmentVariable("PORTAL_DB"))
        .WithVolume("portal_db_data", "/var/lib/postgresql/data")
        .WithEndpoint(35432, 5432, name: "port");
    AttachToGroup(portal_db, ref portalGroup, "c-portal");

    var portal_be = builder.AddProject<Projects.Portal>("c02-portal-be")
        .WaitFor(portal_db)
        .WithEnvironment("Auth__KeycloakBase", Environment.GetEnvironmentVariable("KEYCLOAK_BASE_URL"))
        .WithEnvironment("Auth__Realm", Environment.GetEnvironmentVariable("PORTAL_REALM"))
        .WithEnvironment("Auth__Audience", Environment.GetEnvironmentVariable("PORTAL_AUDIENCE"))
        .WithEnvironment("Auth__DocsClientId", Environment.GetEnvironmentVariable("PORTAL_DOCS_CLIENT_ID"))
        .WithEnvironment("Auth__DocsClientSecret", Environment.GetEnvironmentVariable("PORTAL_DOCS_CLIENT_SECRET"))
        .WithEnvironment("Auth__DocsCookieName", Environment.GetEnvironmentVariable("PORTAL_DOCS_COOKIE_NAME"))
        .WithEnvironment("Auth__ScalarClientId", Environment.GetEnvironmentVariable("PORTAL_SCALAR_CLIENT_ID"))
        .WithEnvironment("Auth__EnableDocs", true.ToString())
        .WithEnvironment("Auth__DevCorsOrigins", "https://localhost:7294,http://localhost:5100,http://localhost:4400")
        .WithEnvironment("Auth__RequireHttpsMetadata", false.ToString())
        .WithExternalHttpEndpoints();
    AttachToGroup(portal_be, ref portalGroup, "c-portal");

    if (portalFE)
    {
        var portal_fe = AddFrontend("c03-portal-fe", "portal", 4400);
        AttachToGroup(portal_fe, ref portalGroup, "c-portal");
    }
}

#endregion

builder.Build().Run();

// helper to add FE
IResourceBuilder<ContainerResource> AddFrontend(string name, string app, int hostPort) =>
    builder.AddDockerfile(name, feRootDirectory, dockerfileAbs)
           .WithBuildArg("APP", app)
           .WithEnvironment("NITRO_HOST", "0.0.0.0")
           .WithEnvironment("NITRO_PORT", "3000")
           .WithHttpEndpoint(hostPort, 3000);

void AttachToGroup<TRes>(
    IResourceBuilder<TRes> child,
    ref IResourceBuilder<GroupResource>? groupRef,
    string groupName
) where TRes : IResource
{
    groupRef ??= builder.AddGroup(groupName);
    child.WithParentRelationship(groupRef);
}
