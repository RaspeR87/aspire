using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Common.Auth;

public sealed class KeycloakRoleClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity id || !id.IsAuthenticated) 
            return Task.FromResult(principal);

        // 1) flat "roles" claims (may be array or single)
        foreach (var c in principal.FindAll("roles"))
        {
            TryAddRole(id, c.Value);
            TryParseArrayAndAdd(id, c.Value);
        }

        // 2) realm_access.roles (classic Keycloak)
        var realmAccessJson = principal.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrWhiteSpace(realmAccessJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccessJson);
                if (doc.RootElement.TryGetProperty("roles", out var rolesEl) &&
                    rolesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in rolesEl.EnumerateArray())
                        TryAddRole(id, r.GetString());
                }
            }
            catch { /* ignore */ }
        }

        return Task.FromResult(principal);

        static void TryAddRole(ClaimsIdentity id, string? role)
        {
            if (!string.IsNullOrWhiteSpace(role) &&
                !id.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == role))
            {
                id.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        static void TryParseArrayAndAdd(ClaimsIdentity id, string value)
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var r in doc.RootElement.EnumerateArray())
                        TryAddRole(id, r.GetString());
            }
            catch { /* not JSON array */ }
        }
    }
}