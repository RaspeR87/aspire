using Common.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ===== Aspire defaults =====
builder.AddServiceDefaults();

// Bind + register everything once
builder.Services.AddUrsivAuth(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseUrsivPipeline(builder.Configuration, builder.Environment);

// APIs = Bearer only (add .Policy("ApiAudience") if you want it global)
app.MapControllers().RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
});

if (app.Environment.IsDevelopment())
    app.MapUrsivDocs(builder.Configuration);

// Aspire health + liveness endpoints
app.MapDefaultEndpoints();

app.Run();
