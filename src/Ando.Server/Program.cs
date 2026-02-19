// =============================================================================
// Program.cs
//
// Summary: Entry point and service configuration for Ando.Server.
//
// This file configures all services including Entity Framework, Hangfire,
// SignalR, authentication, and custom services. It sets up the middleware
// pipeline for the ASP.NET Core MVC application.
//
// Design Decisions:
// - Using top-level statements for simplicity
// - Services are grouped by functionality for readability
// - Configuration is bound using the options pattern
// - Hangfire is configured with SQL Server storage
// =============================================================================

using System.Threading.RateLimiting;
using System.Security.Claims;
using Ando.Server.Auth;
using Ando.Server.BuildExecution;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Hubs;
using Ando.Server.Jobs;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using FastEndpoints.Swagger;
using Hangfire;
using Hangfire.States;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// Configuration
// =============================================================================

builder.Services.Configure<GitHubSettings>(
    builder.Configuration.GetSection(GitHubSettings.SectionName));
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection(EmailSettings.SectionName));
builder.Services.Configure<StorageSettings>(
    builder.Configuration.GetSection(StorageSettings.SectionName));
builder.Services.Configure<BuildSettings>(
    builder.Configuration.GetSection(BuildSettings.SectionName));
builder.Services.Configure<EncryptionSettings>(
    builder.Configuration.GetSection(EncryptionSettings.SectionName));
builder.Services.Configure<TestSettings>(
    builder.Configuration.GetSection(TestSettings.SectionName));
builder.Services.Configure<RateLimitSettings>(
    builder.Configuration.GetSection(RateLimitSettings.SectionName));
builder.Services.Configure<ServerSettings>(
    builder.Configuration.GetSection(ServerSettings.SectionName));

// =============================================================================
// Database
// =============================================================================

// In Testing environment, WebApplicationFactory configures the DbContext
// Skip validation here to allow test-specific database configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString) && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<AndoDbContext>(options =>
        options.UseSqlServer(connectionString));
}

// =============================================================================
// Data Protection (persist keys across container restarts)
// =============================================================================

// We prefer persisting Data Protection keys whenever possible. If keys are lost
// (or unreadable) then Identity auth cookies become invalid and users appear to
// be "randomly logged out" even if their cookie is still present.
//
// Historically we used ephemeral keys in Testing/E2E to avoid file system access
// issues, but this prevents reproducing/validating restart-safe authentication.
// We now attempt persistence first and only fall back to ephemeral keys if the
// configured key path is not writable.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/data/keys";

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("AndoServer");

bool CanWriteToDirectory(string path)
{
    Directory.CreateDirectory(path);
    var probePath = Path.Combine(path, ".dp_write_probe");
    File.WriteAllText(probePath, DateTime.UtcNow.ToString("O"));
    File.Delete(probePath);
    return true;
}

try
{
    // Production (and any environment with a writable mount) should persist keys.
    // Testing/E2E should also persist when possible, to keep auth stable across restarts.
    if (CanWriteToDirectory(dataProtectionKeysPath))
    {
        dataProtection
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
            .SetDefaultKeyLifetime(TimeSpan.FromDays(180));
    }
}
catch (Exception ex) when (builder.Environment.IsEnvironment("Testing") || builder.Environment.IsEnvironment("E2E"))
{
    // Testing/E2E: don't fail startup if persistence isn't available.
    // Without persistence, auth cookies will not survive container restarts.
    builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Warning);
    builder.Logging.AddFilter("Auth.Cookie", LogLevel.Debug);
    builder.Services.AddSingleton(new DataProtectionFallbackWarning(dataProtectionKeysPath, ex));
}

// =============================================================================
// ASP.NET Core Identity
// =============================================================================

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    // Password settings
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;

    // User settings
    options.User.RequireUniqueEmail = true;

    // Sign-in settings (soft email verification - can sign in without confirmation)
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedAccount = false;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<AndoDbContext>()
.AddDefaultTokenProviders();

// Reduce how often Identity re-validates the security stamp against the DB.
// The default (30 minutes) is too aggressive and causes unnecessary sign-outs
// when combined with sliding expiration. 24 hours is sufficient for security
// while keeping sessions alive for long-lived browser tabs.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromHours(24);
});

// =============================================================================
// Authentication (API tokens + cookies)
// =============================================================================

builder.Services.AddAuthentication(options =>
    {
        // Prefer API tokens when present; otherwise fall back to Identity cookies.
        // NOTE: AddIdentity sets DefaultAuthenticateScheme to the cookie scheme.
        // If we don't override it here, Bearer/X-Api-Token will never be evaluated.
        options.DefaultScheme = ApiTokenAuthentication.PolicyScheme;
        options.DefaultAuthenticateScheme = ApiTokenAuthentication.PolicyScheme;
        options.DefaultChallengeScheme = ApiTokenAuthentication.PolicyScheme;
        // Keep sign-in operations (creating cookies) on the Identity cookie scheme.
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddPolicyScheme(ApiTokenAuthentication.PolicyScheme, "API token or cookie auth", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authz = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authz) &&
                authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return ApiTokenAuthentication.Scheme;
            }

            if (context.Request.Headers.ContainsKey(ApiTokenAuthentication.ApiTokenHeaderName))
            {
                return ApiTokenAuthentication.Scheme;
            }

            return IdentityConstants.ApplicationScheme;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthentication.Scheme,
        _ => { });

// =============================================================================
// Hangfire (Background Jobs)
// Skip in E2E environment - E2E tests don't need background job processing
// =============================================================================

if (!builder.Environment.IsEnvironment("E2E"))
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
            QueuePollInterval = TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true
        }));

    var buildSettings = builder.Configuration
        .GetSection(BuildSettings.SectionName)
        .Get<BuildSettings>() ?? new BuildSettings();

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = buildSettings.WorkerCount;
        options.Queues = ["builds", "default"];
    });
}
else
{
    // E2E environment: provide stub Hangfire services (BuildService depends on IBackgroundJobClient)
    builder.Services.AddSingleton<IBackgroundJobClient, NoOpBackgroundJobClient>();
    builder.Services.AddSingleton<IRecurringJobManager, NoOpRecurringJobManager>();
}

// =============================================================================
// SignalR (Real-time)
// =============================================================================

builder.Services.AddSignalR();

// =============================================================================
// Authentication (configured by Identity above, just set paths and options)
// =============================================================================

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/auth/login";
    options.LogoutPath = "/auth/logout";
    options.AccessDeniedPath = "/auth/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;

    // Explicit cookie settings for proper behavior behind reverse proxy (Caddy).
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

    // Extensive auth diagnostics to troubleshoot "random logouts" and proxy/cookie issues.
    // We intentionally do NOT log raw cookie values.
    options.Events = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationEvents
    {
        // Log cookie details and then delegate to Identity's SecurityStampValidator.
        // Previously this handler ONLY logged and returned Task.CompletedTask, which
        // completely disabled security stamp validation. Now we call the validator
        // so that password changes etc. properly invalidate old sessions, while the
        // ValidationInterval (24h) prevents overly aggressive re-checks.
        OnValidatePrincipal = async context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            var userName = context.Principal?.Identity?.Name ?? "(unknown)";
            var isAuthenticated = context.Principal?.Identity?.IsAuthenticated == true;

            logger.LogDebug(
                "Cookie validating: path={Path} user={User} auth={IsAuthenticated} issuedUtc={IssuedUtc} expiresUtc={ExpiresUtc} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto} hasCookieHeader={HasCookieHeader} cookieHeaderLength={CookieHeaderLength}",
                context.HttpContext.Request.Path,
                userName,
                isAuthenticated,
                context.Properties.IssuedUtc,
                context.Properties.ExpiresUtc,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString(),
                context.HttpContext.Request.Headers.Cookie.Count > 0,
                context.HttpContext.Request.Headers.Cookie.ToString().Length);

            // Run Identity's security stamp validation (respects ValidationInterval).
            await SecurityStampValidator.ValidatePrincipalAsync(context);

            // If the validator rejected the principal, log it prominently.
            if (context.Principal == null)
            {
                logger.LogWarning(
                    "Security stamp validation rejected cookie for user={User}. "
                    + "This means the user's security stamp changed (password change, "
                    + "security update, etc.) since the cookie was issued.",
                    userName);
            }
        },
        OnSigningIn = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            logger.LogInformation(
                "Signing in: path={Path} user={User} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                context.HttpContext.Request.Path,
                context.Principal?.Identity?.Name ?? "(unknown)",
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            return Task.CompletedTask;
        },
        OnSignedIn = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            logger.LogInformation(
                "Signed in: user={User} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                context.Principal?.Identity?.Name ?? "(unknown)",
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            return Task.CompletedTask;
        },
        OnSigningOut = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            logger.LogInformation(
                "Signing out: user={User} path={Path} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                context.HttpContext.User?.Identity?.Name ?? "(unknown)",
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            return Task.CompletedTask;
        },

        // Return 401/403 for API requests instead of redirecting to login.
        OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    // Admin-only policy
    options.AddPolicy("RequireAdmin", policy =>
        policy.RequireRole(UserRoles.Admin));
});

// =============================================================================
// API Token Service
// =============================================================================

builder.Services.AddScoped<IApiTokenService, ApiTokenService>();

// =============================================================================
// Rate Limiting
// =============================================================================

var rateLimitSettings = builder.Configuration
    .GetSection(RateLimitSettings.SectionName)
    .Get<RateLimitSettings>() ?? new RateLimitSettings();

if (rateLimitSettings.Enabled)
{
    static string GetClientIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
    }

    builder.Services.AddRateLimiter(options =>
    {
        // Webhook rate limiter (public endpoint, stricter limits)
        options.AddPolicy("webhook", context =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"webhook:ip:{GetClientIp(context)}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.Webhook.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.Webhook.WindowSeconds),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitSettings.Webhook.QueueLimit
                }));

        // API rate limiter (partition by user when authenticated, otherwise by IP).
        options.AddPolicy("api", context =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var partitionKey = string.IsNullOrWhiteSpace(userId)
                ? $"api:ip:{GetClientIp(context)}"
                : $"api:user:{userId}";

            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: partitionKey,
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.Api.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.Api.WindowSeconds),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitSettings.Api.QueueLimit
                });
        });

        // Sensitive auth flows (login/register/password operations), strict and low-queue.
        options.AddPolicy("auth-sensitive", context =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"auth-sensitive:ip:{GetClientIp(context)}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.AuthSensitive.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.AuthSensitive.WindowSeconds),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitSettings.AuthSensitive.QueueLimit
                }));

        // Verification-related auth flows, less strict to reduce UX friction.
        options.AddPolicy("auth-verification", context =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"auth-verification:ip:{GetClientIp(context)}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.AuthVerification.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.AuthVerification.WindowSeconds),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitSettings.AuthVerification.QueueLimit
                }));

        // Backwards-compatible alias.
        options.AddPolicy("auth", context =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"auth:ip:{GetClientIp(context)}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.Auth.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.Auth.WindowSeconds),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitSettings.Auth.QueueLimit
                }));

        // Custom rejection response
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.ContentType = "application/json";

            var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                ? retryAfterValue.TotalSeconds
                : 60;

            context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString("F0");

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Too many requests",
                retryAfterSeconds = retryAfter
            }, cancellationToken);
        };
    });
}

// =============================================================================
// Session (for OAuth state)
// =============================================================================

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    // OAuth state is stored in session; 10 minutes was too tight for real flows.
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// =============================================================================
// MVC & Razor (kept for parallel operation during migration)
// =============================================================================

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        // Accept string enum values in JSON (for test API)
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddHttpContextAccessor();

// =============================================================================
// FastEndpoints (REST API layer)
// =============================================================================

builder.Services.AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Ando CI Server API";
            s.Version = "v1";
            s.Description = "REST API for Ando CI Server - Build automation and project management";
        };
    });

// =============================================================================
// HTTP Client for external APIs
// =============================================================================

builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    client.DefaultRequestHeaders.Add("User-Agent", "Ando-Server");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});

// Resend-compatible email API client (raw HttpClient; detailed failure logging is done in ResendEmailService)
builder.Services.AddHttpClient("Resend", client =>
{
    // Keep auth/register bounded even if the email provider is slow/down.
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent", "Ando-Server");
});

// =============================================================================
// Custom Services
// =============================================================================

// Encryption
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();

// GitHub Integration
builder.Services.AddSingleton<GitHubAppAuthenticator>();
builder.Services.AddScoped<IGitHubService, GitHubService>();

// Script Detection (auto-detect required secrets and profiles from build scripts)
builder.Services.AddScoped<IRequiredSecretsDetector, RequiredSecretsDetector>();
builder.Services.AddScoped<IProfileDetector, ProfileDetector>();

// Build Execution
builder.Services.AddSingleton<CancellationTokenRegistry>();
builder.Services.AddScoped<IBuildOrchestrator, BuildOrchestrator>();
builder.Services.AddScoped<IBuildService, BuildService>();

// Email (provider selected via configuration)
builder.Services.AddSingleton<ITempDataProvider, SessionStateTempDataProvider>();
builder.Services.AddScoped<IEmailService>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<EmailSettings>>();
    var urlService = sp.GetRequiredService<IUrlService>();
    var db = sp.GetRequiredService<AndoDbContext>();
    var viewEngine = sp.GetRequiredService<IRazorViewEngine>();
    var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

    return settings.Value.Provider switch
    {
        EmailProvider.Smtp => new SmtpEmailService(
            settings, urlService, db, viewEngine, tempDataProvider, sp,
            loggerFactory.CreateLogger<SmtpEmailService>()),

        // Default to Resend
        _ => new ResendEmailService(
            httpClientFactory,
            settings, urlService, db, viewEngine, tempDataProvider, sp,
            loggerFactory.CreateLogger<ResendEmailService>())
    };
});

// Admin / Impersonation
builder.Services.AddScoped<IImpersonationService, ImpersonationService>();
builder.Services.AddSingleton<IUrlService, UrlService>();

// Project Management
builder.Services.AddScoped<IProjectService, ProjectService>();

// Cleanup Jobs
builder.Services.AddScoped<CleanupArtifactsJob>();
builder.Services.AddScoped<CleanupOldBuildsJob>();

// Configuration Validation
builder.Services.AddConfigurationValidation();

// Audit Logging
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// =============================================================================
// Build Application
// =============================================================================

var app = builder.Build();

// Emit a single warning if we couldn't enable persistent Data Protection keys in E2E/Testing.
if (app.Services.GetService<DataProtectionFallbackWarning>() is { } dpWarning)
{
    app.Logger.LogWarning(
        dpWarning.Exception,
        "Data Protection key persistence is unavailable (path={KeysPath}). Falling back to ephemeral keys. " +
        "Auth cookies will not survive container restarts.",
        dpWarning.KeysPath);
}

// =============================================================================
// Startup Banner
// =============================================================================

var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var banner = $"""

     █████╗ ███╗   ██╗██████╗  ██████╗
    ██╔══██╗████╗  ██║██╔══██╗██╔═══██╗
    ███████║██╔██╗ ██║██║  ██║██║   ██║
    ██╔══██║██║╚██╗██║██║  ██║██║   ██║
    ██║  ██║██║ ╚████║██████╔╝╚██████╔╝
    ╚═╝  ╚═╝╚═╝  ╚═══╝╚═════╝  ╚═════╝
                              v{version}

    Build System Server
    Environment: {app.Environment.EnvironmentName}

""";
Console.WriteLine(banner);
app.Logger.LogInformation("Ando Server v{Version} starting in {Environment} environment", version, app.Environment.EnvironmentName);

// =============================================================================
// Middleware Pipeline
// =============================================================================

// Configuration validation - show error page if required config is missing
app.UseConfigurationValidation();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing") || app.Environment.IsEnvironment("E2E"))
{
    // Show detailed errors for development and testing
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Handle forwarded headers from reverse proxy (Caddy).
// Without this, the app sees all requests as plain HTTP from localhost,
// which breaks cookie Secure flags and scheme detection.
// We clear KnownProxies/KnownNetworks so Docker bridge IPs (172.x) are trusted,
// not just 127.0.0.1 (the default). Without this, the forwarded headers are
// silently ignored when Caddy runs on a Docker network.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownNetworks.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Rate limiting middleware (must be before auth to protect login endpoints)
if (rateLimitSettings.Enabled)
{
    app.UseRateLimiter();
}

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Log 401/403 for key endpoints with enough context to diagnose missing cookies, proxy header issues, etc.
// Keep this after auth so we can tell whether a cookie was accepted.
app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/builds"))
        {
            app.Logger.LogWarning(
                "Auth failure: status={Status} method={Method} path={Path} user={User} auth={IsAuthenticated} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto} hasCookieHeader={HasCookieHeader} cookieHeaderLength={CookieHeaderLength} ua={UserAgent}",
                context.Response.StatusCode,
                context.Request.Method,
                path,
                context.User?.Identity?.Name ?? "(anonymous)",
                context.User?.Identity?.IsAuthenticated == true,
                context.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.Request.Headers["X-Forwarded-For"].ToString(),
                context.Request.Headers["X-Forwarded-Proto"].ToString(),
                context.Request.Headers.Cookie.Count > 0,
                context.Request.Headers.Cookie.ToString().Length,
                context.Request.Headers.UserAgent.ToString());
        }
    }
});

// Default-secure: require authentication for all /api endpoints unless explicitly AllowAnonymous.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var endpoint = context.GetEndpoint();
        var allowsAnonymous = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null;
        if (!allowsAnonymous && context.User.Identity?.IsAuthenticated != true)
        {
            app.Logger.LogWarning(
                "API unauthorized: method={Method} path={Path} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto} hasCookieHeader={HasCookieHeader} cookieHeaderLength={CookieHeaderLength} hasAuthzHeader={HasAuthzHeader} hasApiTokenHeader={HasApiTokenHeader}",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.Request.Headers["X-Forwarded-For"].ToString(),
                context.Request.Headers["X-Forwarded-Proto"].ToString(),
                context.Request.Headers.Cookie.Count > 0,
                context.Request.Headers.Cookie.ToString().Length,
                context.Request.Headers.Authorization.Count > 0,
                context.Request.Headers.ContainsKey(ApiTokenAuthentication.ApiTokenHeaderName));

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }

    await next();
});

// =============================================================================
// FastEndpoints (REST API)
// =============================================================================

app.UseFastEndpoints(c =>
{
    c.Endpoints.RoutePrefix = "api";
    c.Serializer.Options.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Swagger UI (development, testing, and E2E only)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing") || app.Environment.IsEnvironment("E2E"))
{
    app.UseSwaggerGen();
}

// =============================================================================
// Endpoints
// =============================================================================

// SignalR hub for real-time build logs
app.MapHub<BuildLogHub>("/hubs/build-logs");

// Hangfire dashboard (development only)
if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire");
}

// =============================================================================
// Database Migration and Seeding
// MUST run before Hangfire job registration to ensure database exists
// =============================================================================

// Skip in Testing environment - WebApplicationFactory handles database setup
// E2E environment uses docker-compose and needs normal database initialization
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AndoDbContext>();

    // Apply pending migrations and create database if needed
    db.Database.Migrate();

    // Seed roles
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
    await SeedRolesAsync(roleManager);
}

// =============================================================================
// Recurring Jobs
// =============================================================================

// Skip recurring job registration during testing/E2E (no JobStorage configured)
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("E2E"))
{
    // Use service-based API instead of static RecurringJob class
    var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();

    // Cleanup expired artifacts every hour
    recurringJobManager.AddOrUpdate<CleanupArtifactsJob>(
        "cleanup-artifacts",
        job => job.ExecuteAsync(),
        Cron.Hourly);

    // Cleanup stale builds every 15 minutes
    recurringJobManager.AddOrUpdate<CleanupOldBuildsJob>(
        "cleanup-stale-builds",
        job => job.ExecuteAsync(),
        "*/15 * * * *");
}

// Attribute-routed controllers (webhooks/session/health/test endpoints).
app.MapControllers();

// =============================================================================
// SPA Fallback (React App)
// Serves the React SPA for client-side routing
// =============================================================================

// Fallback to SPA for routes that don't match API/controller endpoints
app.MapFallbackToFile("app/index.html");

app.Run();

// =============================================================================
// Database Seeding Functions
// =============================================================================

/// <summary>
/// Seeds the required application roles.
/// </summary>
static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
{
    foreach (var roleName in UserRoles.AllRoles)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var role = new ApplicationRole
            {
                Name = roleName,
                Description = roleName == UserRoles.Admin
                    ? "Global administrator with full system access"
                    : "Standard user with access to their own projects"
            };
            await roleManager.CreateAsync(role);
        }
    }
}

// =============================================================================
// Program Class Marker (for WebApplicationFactory in tests)
// =============================================================================

/// <summary>
/// Partial class marker to make Program accessible for integration tests.
/// </summary>
public partial class Program { }

// =============================================================================
// Stub Hangfire Services (for E2E environment)
// =============================================================================

/// <summary>
/// No-op implementation of IBackgroundJobClient for E2E testing.
/// </summary>
internal class NoOpBackgroundJobClient : IBackgroundJobClient
{
    private int _jobCounter;

    public string Create(Hangfire.Common.Job job, IState state)
    {
        // Return a fake job ID
        return $"e2e-job-{Interlocked.Increment(ref _jobCounter)}";
    }

    public bool ChangeState(string jobId, IState state, string expectedState)
    {
        return true;
    }
}

/// <summary>
/// No-op implementation of IRecurringJobManager for E2E testing.
/// </summary>
internal class NoOpRecurringJobManager : IRecurringJobManager
{
    public void AddOrUpdate(string recurringJobId, Hangfire.Common.Job job, string cronExpression, RecurringJobOptions options)
    {
        // No-op
    }

    public void Trigger(string recurringJobId)
    {
        // No-op
    }

    public void RemoveIfExists(string recurringJobId)
    {
        // No-op
    }
}

// Used to surface a single warning in E2E/Testing when Data Protection key persistence
// cannot be enabled (e.g., missing/unwritable /data/keys mount).
file sealed record DataProtectionFallbackWarning(string KeysPath, Exception Exception);
