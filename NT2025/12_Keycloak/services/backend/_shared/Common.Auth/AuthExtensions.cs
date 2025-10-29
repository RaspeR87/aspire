using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

namespace Common.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddUrsivAuth(this IServiceCollection services, IConfiguration cfg, IWebHostEnvironment env)
    {
        services.AddOptions<KeycloakAuthOptions>()
                .Bind(cfg.GetSection("Auth"))
                .ValidateDataAnnotations();

        services.AddScoped<IClaimsTransformation, KeycloakRoleClaimsTransformer>();

        var opts = cfg.GetSection("Auth").Get<KeycloakAuthOptions>() ?? new();

        // CORS (dev only)
        services.AddCors(o =>
        {
            o.AddPolicy("DevCors", p =>
            {
                if (opts.DevCorsOrigins?.Length > 0)
                    p.WithOrigins(opts.DevCorsOrigins);
                p.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            });
        });

        var authority = $"{opts.KeycloakBase.TrimEnd('/')}/realms/{opts.Realm}";

        services.AddAuthentication(o =>
        {
            o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            o.Authority = authority;
            o.RequireHttpsMetadata = opts.RequireHttpsMetadata;

            o.Audience = opts.Audience;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = authority,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
#if DEBUG
                ClockSkew = TimeSpan.FromMinutes(2)
#else
                ClockSkew = TimeSpan.Zero
#endif
                ,
                NameClaimType = "preferred_username",
                RoleClaimType = ClaimTypes.Role
            };
            o.RefreshOnIssuerKeyNotFound = true;

#if DEBUG
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    var has = !string.IsNullOrEmpty(ctx.Request.Headers["Authorization"]);
                    log.LogInformation("Authorization header present: {Has}", has);
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = ctx =>
                {
                    var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    log.LogError(ctx.Exception, "JWT auth failed");
                    return Task.CompletedTask;
                },
                OnChallenge = ctx =>
                {
                    var log = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    log.LogWarning("JWT challenge: {Error} - {Desc}", ctx.Error, ctx.ErrorDescription);
                    return Task.CompletedTask;
                }
            };
#endif
        })
        .AddCookie("DocsCookie", o =>
        {
            o.Cookie.Name = string.IsNullOrWhiteSpace(opts.DocsCookieName) ? "docs_auth" : opts.DocsCookieName;
            o.SlidingExpiration = true;
        })
        .AddOpenIdConnect("DocsOidc", o =>
        {
            o.Authority = authority;
            o.RequireHttpsMetadata = opts.RequireHttpsMetadata;

            o.ClientId = opts.DocsClientId;
            o.ClientSecret = opts.DocsClientSecret;
            o.ResponseType = "code";
            o.UsePkce = true;
            o.SaveTokens = true;
            o.CallbackPath = "/signin-oidc";
            o.SignInScheme = "DocsCookie";

            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("profile");

            o.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "preferred_username",
                RoleClaimType = ClaimTypes.Role
            };
        });

        services.AddAuthorization(a =>
        {
            a.AddPolicy("DocsUI", p =>
            {
                p.AddAuthenticationSchemes("DocsCookie", "DocsOidc");
                p.RequireAuthenticatedUser();
                p.RequireRole("admin");
            });
            a.AddPolicy("ApiAudience", p =>
            {
                p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                p.RequireAuthenticatedUser();
                p.RequireClaim("aud", opts.Audience);
            });
        });

        services.AddControllers();

        // OpenAPI (and make Scalar configable later in MapCreaDocs)
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((doc, _, __) =>
            {
                doc.Components ??= new();
                doc.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();

                doc.Components.SecuritySchemes["OAuth2"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth"),
                            TokenUrl         = new Uri($"{authority}/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string>
                            {
                                ["openid"]  = "OpenID",
                                ["profile"] = "Profile"
                            }
                        }
                    }
                };

                doc.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Name = "Authorization"
                };

                doc.SecurityRequirements ??= new List<OpenApiSecurityRequirement>();
                doc.SecurityRequirements.Add(new OpenApiSecurityRequirement
                {
                    [ doc.Components.SecuritySchemes["OAuth2"] ] = new[] { "openid", "profile" }
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static WebApplication UseUrsivPipeline(this WebApplication app, IConfiguration cfg, IWebHostEnvironment env)
    {
        var opts = cfg.GetSection("Auth").Get<KeycloakAuthOptions>() ?? new();

#if DEBUG
        app.UseCors("DevCors");
#endif
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapUrsivDocs(this WebApplication app, IConfiguration cfg)
    {
        var opts = cfg.GetSection("Auth").Get<KeycloakAuthOptions>() ?? new();
        if (!opts.EnableDocs) return app;

        app.MapOpenApi().RequireAuthorization("DocsUI");

        app.MapScalarApiReference(options =>
        {
            options.AddPreferredSecuritySchemes("OAuth2")
                   .AddAuthorizationCodeFlow("OAuth2", flow =>
                   {
                       flow.ClientId = opts.ScalarClientId;
                       flow.Pkce = Pkce.Sha256;
                       flow.SelectedScopes = new[] { "openid", "profile" };
                   });
        }).RequireAuthorization("DocsUI");

        return app;
    }
}