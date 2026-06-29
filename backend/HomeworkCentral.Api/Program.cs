using System.Text;
using AspNetCoreRateLimit;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Trust forwarded headers from the nginx reverse proxy so rate limiting
// buckets by the real client IP rather than the proxy address.
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opts.KnownNetworks.Clear();
    opts.KnownProxies.Clear();
});

// Database
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Auth services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRoleMaskService, RoleMaskService>();
builder.Services.AddScoped<IEffectiveMaskService, EffectiveMaskService>();
builder.Services.AddScoped<IRoleAssignmentService, RoleAssignmentService>();
builder.Services.AddScoped<IAuthorizationHandler, BitmaskAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, BitmaskAuthorizationPolicyProvider>();

// JWT authentication
string jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret must be set via environment variable or user-secrets.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "HomeworkCentral",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "HomeworkCentralUsers",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// Rate limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

// CORS — allow the configured frontend origin; tighten for production
builder.Services.AddCors(opts =>
    opts.AddPolicy("Frontend", p =>
        p.WithOrigins(
                builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
            .AllowCredentials()
            .AllowAnyHeader()
            .AllowAnyMethod()));

WebApplication app = builder.Build();
bool devBypassEnabled = DevBypass.IsEnabled(builder.Configuration, app.Environment);

// ForwardedHeaders must run before any middleware that inspects the IP
app.UseForwardedHeaders();

// Security headers on every response
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = devBypassEnabled
        ? "default-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; frame-ancestors 'none';"
        : "default-src 'self'; frame-ancestors 'none';";
    await next();
});

app.UseIpRateLimiting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health probe for Docker / load balancers
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// Localhost-only dev landing routes and linked favicon (see DevAssets.CanonicalFaviconRepoPath).
if (devBypassEnabled)
{
    app.MapGet("/", DevRootPage.ForbiddenDirectoryPage);
    app.MapGet("/favicon.svg", DevRootPage.Favicon);
}

// Auto-migrate only in Development to avoid blocking production deploys
// and concurrent startup races. In production, run migrations explicitly.
if (app.Environment.IsDevelopment())
{
    using IServiceScope scope = app.Services.CreateScope();
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

using (IServiceScope seedScope = app.Services.CreateScope())
{
    AppDbContext seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
    IRoleMaskService roleMaskService = seedScope.ServiceProvider.GetRequiredService<IRoleMaskService>();
    IEffectiveMaskService effectiveMaskService = seedScope.ServiceProvider.GetRequiredService<IEffectiveMaskService>();
    await AuthorizationSeedData.SeedAsync(seedDb, roleMaskService);
    if (devBypassEnabled)
        await DevBypassSeedData.SeedAsync(seedDb, effectiveMaskService);
}

app.Run();
