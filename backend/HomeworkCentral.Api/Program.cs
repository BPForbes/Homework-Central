using System.Net;
using System.Text;
using AspNetCoreRateLimit;
using HomeworkCentral.Api.Authorization;
using HomeworkCentral.Api.Captcha;
using HomeworkCentral.Api.Captcha.FCaptcha;
using HomeworkCentral.Api.Chat;
using HomeworkCentral.Api.Chat.Mentions;
using HomeworkCentral.Api.Data;
using HomeworkCentral.Api.Dev;
using HomeworkCentral.Api.Hubs;
using HomeworkCentral.Api.Infrastructure;
using HomeworkCentral.Api.Risk;
using HomeworkCentral.Api.ScrapingDetection;
using HomeworkCentral.Api.Services;
using HomeworkCentral.Api.Tenancy;
using HomeworkCentral.Api.Tickets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Trust forwarded headers from the nginx reverse proxy so rate limiting
// buckets by the real client IP rather than the proxy address.
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    opts.KnownIPNetworks.Clear();
    opts.KnownProxies.Clear();

    foreach (string cidr in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        if (System.Net.IPNetwork.TryParse(cidr, out System.Net.IPNetwork network))
            opts.KnownIPNetworks.Add(network);
    }

    foreach (string address in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(address, out IPAddress? proxy) && proxy is not null)
            opts.KnownProxies.Add(proxy);
    }
});

// Database — master registry; tenant databases are resolved dynamically at runtime.
string masterConnection = ConnectionStringHelpers.ResolveMasterConnection(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(masterConnection, npgsql =>
        npgsql.MigrationsHistoryTable(TenancyConstants.AppMigrationsHistoryTable)));

builder.Services.AddDbContext<MasterDbContext>(opts =>
    opts.UseNpgsql(masterConnection, npgsql =>
        npgsql.MigrationsHistoryTable(TenancyConstants.MasterMigrationsHistoryTable)));

builder.Services.AddSingleton<ITenantConnectionResolver, TenantConnectionResolver>();
builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();

// Auth services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRoleMaskService, RoleMaskService>();
builder.Services.AddScoped<IEffectiveMaskService, EffectiveMaskService>();
builder.Services.AddScoped<IRoleAssignmentService, RoleAssignmentService>();
builder.Services.AddScoped<ISubjectClaimService, SubjectClaimService>();
builder.Services.AddOptions<FCaptchaOptions>()
    .Bind(builder.Configuration.GetSection("FCaptcha"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FCaptchaOptions>, FCaptchaOptionsValidator>();
builder.Services.AddSingleton<IFCaptchaVerifier, FCaptchaVerifier>();
builder.Services.AddScoped<ICaptchaService, CaptchaService>();
builder.Services.AddScoped<ICaptchaRoleService, CaptchaRoleService>();
builder.Services.AddSingleton<IScrapingDetectionService, ScrapingDetectionService>();
builder.Services.Configure<RiskOptions>(builder.Configuration.GetSection("Risk"));
builder.Services.AddSingleton<IIdentityRiskProfileService, IdentityRiskProfileService>();
builder.Services.AddSingleton<IRiskEngine, RiskEngine>();
builder.Services.AddScoped<IAccessScopeAccessor, AccessScopeAccessor>();
builder.Services.AddScoped<IChatRoomAccessService, ChatRoomAccessService>();
builder.Services.AddScoped<IChatRoomDetailService, ChatRoomDetailService>();
builder.Services.AddScoped<IChatMessageService, ChatMessageService>();
builder.Services.AddScoped<IRoleAppearanceService, RoleAppearanceService>();
builder.Services.AddSingleton<ICustomChannelStore, CustomChannelStore>();
builder.Services.AddSingleton<IChatNavNotifier, ChatNavNotifier>();
builder.Services.AddScoped<InfrastructureUserDirectory>();
builder.Services.AddScoped<IInfrastructureService, InfrastructureService>();
builder.Services.AddScoped<IInfoEntryService, InfoEntryService>();
builder.Services.AddScoped<IPasswordConfirmationService, PasswordConfirmationService>();
builder.Services.Configure<TicketOptions>(builder.Configuration.GetSection("Tickets"));
builder.Services.Configure<HomeworkCentral.Api.Assessment.LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<HomeworkCentral.Api.Assessment.NeuralNetTrainingOptions>(
    builder.Configuration.GetSection("NeuralNetTraining"));
builder.Services.Configure<HomeworkCentral.Api.Uploads.UploadOptions>(builder.Configuration.GetSection("Uploads"));
builder.Services.Configure<HomeworkCentral.Api.Uploads.ClamAvOptions>(builder.Configuration.GetSection("ClamAv"));
builder.Services.Configure<HomeworkCentral.Api.Uploads.AttachmentAccessOptions>(
    builder.Configuration.GetSection("AttachmentAccess"));

string? redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "hc:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton<HomeworkCentral.Api.Uploads.HazardDefinitionRegistry>();
builder.Services.AddSingleton<MimeDetective.IContentInspector>(_ =>
{
    MimeDetective.ContentInspectorBuilder inspectorBuilder = new()
    {
        Definitions = MimeDetective.Definitions.DefaultDefinitions.All(),
    };
    return inspectorBuilder.Build();
});
builder.Services.AddScoped<HomeworkCentral.Api.Uploads.IAttachmentTypeInspector,
    HomeworkCentral.Api.Uploads.AttachmentTypeInspector>();
builder.Services.AddScoped<HomeworkCentral.Api.Uploads.IMalwareScanner,
    HomeworkCentral.Api.Uploads.ClamAvMalwareScanner>();
builder.Services.AddScoped<HomeworkCentral.Api.Uploads.IAttachmentAccessTokenService,
    HomeworkCentral.Api.Uploads.AttachmentAccessTokenService>();
builder.Services.AddScoped<HomeworkCentral.Api.Uploads.IOrphanAttachmentCleanupService,
    HomeworkCentral.Api.Uploads.OrphanAttachmentCleanupService>();
builder.Services.AddHostedService<HomeworkCentral.Api.Uploads.OrphanAttachmentCleanupWorker>();
builder.Services.AddHttpClient<OllamaTicketTrackingAnalyzer>((sp, client) =>
{
    TicketOptions ticketOptions = sp.GetRequiredService<IOptions<TicketOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, ticketOptions.RequestTimeoutSeconds));
});
builder.Services.AddHttpClient<HomeworkCentral.Api.Assessment.LlmClient>((sp, client) =>
{
    HomeworkCentral.Api.Assessment.LlmOptions llmOptions =
        sp.GetRequiredService<IOptions<HomeworkCentral.Api.Assessment.LlmOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5, llmOptions.TimeoutSeconds));
});
builder.Services.AddSingleton<NullTicketTrackingAnalyzer>();
builder.Services.AddScoped<ITicketTrackingAnalyzer>(sp =>
{
    TicketOptions ticketOptions = sp.GetRequiredService<IOptions<TicketOptions>>().Value;
    return ticketOptions.OllamaEnabled
        ? sp.GetRequiredService<OllamaTicketTrackingAnalyzer>()
        : sp.GetRequiredService<NullTicketTrackingAnalyzer>();
});
builder.Services.AddSingleton<HomeworkCentral.Api.Tickets.Preface.ITicketPrefaceCheck>(
    HomeworkCentral.Api.Tickets.Preface.TutorSubjectPrefaceCheck.Instance);
builder.Services.AddSingleton<HomeworkCentral.Api.Tickets.Preface.ITicketPrefaceCheck>(
    HomeworkCentral.Api.Tickets.Preface.ModerationConceptPrefaceCheck.Instance);
builder.Services.AddSingleton<HomeworkCentral.Api.Tickets.Preface.ITicketPrefaceCheckResolver,
    HomeworkCentral.Api.Tickets.Preface.TicketPrefaceCheckResolver>();
builder.Services.AddScoped<ITicketRecipientResolver, TicketRecipientResolver>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddSingleton<HomeworkCentral.Api.Assessment.IAssessmentQueue, HomeworkCentral.Api.Assessment.AssessmentQueue>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.ILlmClient>(sp =>
    sp.GetRequiredService<HomeworkCentral.Api.Assessment.LlmClient>());
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.IVectorDocumentStore, HomeworkCentral.Api.Assessment.VectorDocumentStore>();
builder.Services.AddSingleton<HomeworkCentral.Api.Assessment.ModerationChatMonitorNeuralNet>();
builder.Services.AddSingleton<HomeworkCentral.Api.Assessment.TutoringChatMonitorNeuralNet>();
builder.Services.AddSingleton<HomeworkCentral.Api.Assessment.IChatMonitoringNeuralModelFactory, HomeworkCentral.Api.Assessment.ChatMonitoringNeuralModelFactory>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.INeuralNetTrainingService, HomeworkCentral.Api.Assessment.NeuralNetTrainingService>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.SyntheticThreadScenarioGenerator>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.NeuralNetCheckpointStore>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.NeuralNetTrainingPromoter>();
builder.Services.AddSingleton<HomeworkCentral.Api.Assessment.INeuralNetTrainingQueue, HomeworkCentral.Api.Assessment.NeuralNetTrainingQueue>();
builder.Services.AddHostedService<HomeworkCentral.Api.Assessment.NeuralNetTrainingWorker>();
builder.Services.AddHostedService<HomeworkCentral.Api.Assessment.NeuralNetCheckpointRefreshService>();
builder.Services.AddHostedService<HomeworkCentral.Api.Assessment.ChatMonitoringNeuralModelWarmupService>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.ICommunityScoreAggregator, HomeworkCentral.Api.Assessment.CommunityScoreAggregator>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.ICandidateStateService, HomeworkCentral.Api.Assessment.CandidateStateService>();
builder.Services.AddScoped<HomeworkCentral.Api.Assessment.IAssessmentPipelineService, HomeworkCentral.Api.Assessment.AssessmentPipelineService>();
builder.Services.AddHostedService<HomeworkCentral.Api.Assessment.AssessmentWorker>();
builder.Services.AddScoped<HomeworkCentral.Api.Chat.IChatMessageVoteService, HomeworkCentral.Api.Chat.ChatMessageVoteService>();
builder.Services.AddScoped<HomeworkCentral.Api.Uploads.IChatAttachmentService, HomeworkCentral.Api.Uploads.ChatAttachmentService>();
builder.Services.AddSingleton<IChatTypingTracker, ChatTypingTracker>();
builder.Services.AddSingleton<IMentionCooldownTracker, MentionCooldownTracker>();
builder.Services.AddSingleton<IChatOnlineTracker, ChatOnlineTracker>();
builder.Services.AddScoped<IMentionRecipientResolver, MentionRecipientResolver>();
builder.Services.AddScoped<IAuthorizationHandler, BitmaskAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PlatformRoleManagementAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ResourceVisibilityHandler>();
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
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                string? accessToken = context.Request.Query["access_token"];
                PathString path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy(AuthorizationPolicyNames.ResourceVisibility,
        policy => policy.AddRequirements(new ResourceVisibilityRequirement()));
    opts.AddPolicy(AuthorizationPolicyNames.ManagePlatformRoles,
        policy => policy.AddRequirements(new PlatformRoleManagementRequirement()));
});
builder.Services.AddSignalR();
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

bool devBypassEnabled = DevBypass.IsEnabled(builder.Configuration, builder.Environment);
bool skipDevStartupWarmup = DevStartupWarmup.ShouldSkip(builder.Configuration, builder.Environment);
bool eagerPersonaProvisioning = DevPersonaEagerProvisioning.IsEnabled(builder.Configuration);
builder.Services.AddSingleton<IApplicationReadiness, ApplicationReadiness>();
// Warmup must be a BackgroundService so Kestrel listens before migrate/seed finishes.
builder.Services.AddHostedService<ApplicationStartupWarmupHostedService>();
if (devBypassEnabled)
{
    builder.Services.AddSingleton<IDevPersonaProvisioner, DevPersonaProvisioner>();
    builder.Services.AddHostedService<DevPersonaProvisioningHostedService>();
}

// The FCaptcha widget script, its challenge iframe, and its background requests all come from
// this origin — self-hosted, so it's whatever docker-compose.yml's fcaptcha service (or a real
// deployment) is reachable at, not a fixed third-party domain.
string fCaptchaOrigin = builder.Configuration["FCaptcha:PublicUrl"] ?? "http://localhost:3010";

WebApplication app = builder.Build();

// Localhost-only developer bypass (HC_DEV_BYPASS=1 + Development + loopback).

// ForwardedHeaders must run before any middleware that inspects the IP
app.UseForwardedHeaders();

// Security headers on every response
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Content-Security-Policy"] = devBypassEnabled
        ? $"default-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com {fCaptchaOrigin}; font-src 'self' https://fonts.gstatic.com; script-src 'self' {fCaptchaOrigin}; frame-src {fCaptchaOrigin}; connect-src 'self' {fCaptchaOrigin}; frame-ancestors 'none';"
        : $"default-src 'self'; script-src 'self' {fCaptchaOrigin}; frame-src {fCaptchaOrigin}; connect-src 'self' {fCaptchaOrigin}; style-src 'self' {fCaptchaOrigin}; frame-ancestors 'none';";
    await next();
});

app.UseIpRateLimiting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ScrapingDetectionMiddleware>();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Health probe for Docker / load balancers and the Vite BackendGate.
// [FromServices] is required for IDevPersonaProvisioner: it is only registered when the
// dev bypass is enabled. Without an explicit binding source, ASP.NET Core cannot decide
// whether the optional parameter is DI or a request body and fails endpoint metadata build
// for every route when the bypass is off.
app.MapGet("/healthz", (
    [FromServices] IApplicationReadiness readiness,
    [FromServices] IDevPersonaProvisioner? personaProvisioner) =>
{
    ApplicationReadyState state = readiness.State;
    string status = state switch
    {
        ApplicationReadyState.Ready => "healthy",
        ApplicationReadyState.Failed => "failed",
        _ => "starting",
    };

    object body = personaProvisioner is null
        ? new { status, ready = state == ApplicationReadyState.Ready, failure = readiness.FailureMessage }
        : new
        {
            status,
            ready = state == ApplicationReadyState.Ready,
            failure = readiness.FailureMessage,
            personasProvisioned = personaProvisioner.ProvisionedCount,
            personasTotal = personaProvisioner.TotalPersonaCount,
            // On-demand persona mode has nothing to wait for at boot; eager mode reports
            // whether the background sweep has finished.
            personasReady = !eagerPersonaProvisioning
                || personaProvisioner.ProvisionedCount >= personaProvisioner.TotalPersonaCount,
        };

    return state switch
    {
        ApplicationReadyState.Failed => Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => Results.Ok(body),
    };
});

// Localhost-only dev landing routes and linked favicon (see DevAssets.CanonicalFaviconRepoPath).
if (devBypassEnabled)
{
    app.MapGet("/", DevRootPage.ForbiddenDirectoryPage);
    app.MapGet("/favicon.svg", DevRootPage.Favicon);
}

if (builder.Configuration.GetValue<bool>("KubernetesTraining:RunOneQueued"))
{
    // One-shot worker exits without hosting; warmup must finish inline before training.
    await ApplicationStartupWarmup.RunAsync(
        app.Services,
        app.Environment.IsDevelopment(),
        skipDevStartupWarmup,
        devBypassEnabled,
        eagerPersonaProvisioning);
    app.Services.GetRequiredService<IApplicationReadiness>().MarkReady();

    using IServiceScope kubernetesJobScope = app.Services.CreateScope();
    HomeworkCentral.Api.Assessment.INeuralNetTrainingService training =
        kubernetesJobScope.ServiceProvider.GetRequiredService<HomeworkCentral.Api.Assessment.INeuralNetTrainingService>();
    bool claimed = await training.RunNextSyntheticSessionAsync(CancellationToken.None);
    app.Logger.LogInformation("Kubernetes training worker completed. Claimed queued session: {Claimed}", claimed);
    return;
}

app.Run();

/// <summary>Entry point type for WebApplicationFactory integration tests.</summary>
public partial class Program
{
}
