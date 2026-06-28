using System.Text;
using AspNetCoreRateLimit;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

// JWT authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
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

builder.Services.AddAuthorization(opts =>
{
    RegisterBitmaskPolicies(opts, MaskType.Moderation, ModerationPermissions.BanMembers);
    RegisterBitmaskPolicies(opts, MaskType.Feature, PlatformFeatures.PublicMessages);
    RegisterBitmaskPolicies(opts, MaskType.Role, PlatformRoles.Tutor);
    RegisterBitmaskPolicies(opts, MaskType.SubjectExpertise, ComputerScienceExpertise.Python, SubjectMaskNames.ComputerScience);
});

static void RegisterBitmaskPolicies(
    Microsoft.AspNetCore.Authorization.AuthorizationOptions opts,
    MaskType maskType,
    short exampleBit,
    string? subjectCategory = null)
{
    opts.AddPolicy(
        AuthorizationPolicyNames.For(maskType, exampleBit, subjectCategory),
        policy => policy.AddRequirements(new BitmaskRequirement(maskType, exampleBit, subjectCategory)));
}
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

var app = builder.Build();

// ForwardedHeaders must run before any middleware that inspects the IP
app.UseForwardedHeaders();

// Security headers on every response
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; frame-ancestors 'none';";
    await next();
});

app.UseIpRateLimiting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health probe for Docker / load balancers
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// Auto-migrate only in Development to avoid blocking production deploys
// and concurrent startup races. In production, run migrations explicitly.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var roleMaskService = scope.ServiceProvider.GetRequiredService<IRoleMaskService>();
    db.Database.Migrate();
    await AuthorizationSeedData.SeedAsync(db, roleMaskService);
}

app.Run();
