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
using Microsoft.AspNetCore.DataProtection;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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

// In Testing/E2E environment, use ephemeral keys to avoid file system access issues
if (builder.Environment.IsEnvironment("Testing") || builder.Environment.IsEnvironment("E2E"))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("AndoServer");
}
else
{
    var keysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/data/keys";
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
        .SetApplicationName("AndoServer");
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

    // Return 401 for API requests instead of redirecting to login
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    // Admin-only policy
    options.AddPolicy("RequireAdmin", policy =>
        policy.RequireRole(UserRoles.Admin));
});

// =============================================================================
// Session (for OAuth state)
// =============================================================================

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(10);
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

builder.Services.AddHttpClient("Resend", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
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
    var viewEngine = sp.GetRequiredService<IRazorViewEngine>();
    var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    return settings.Value.Provider switch
    {
        EmailProvider.Azure => new AzureEmailService(
            settings, viewEngine, tempDataProvider, sp,
            loggerFactory.CreateLogger<AzureEmailService>()),

        EmailProvider.Smtp => new SmtpEmailService(
            settings, viewEngine, tempDataProvider, sp,
            loggerFactory.CreateLogger<SmtpEmailService>()),

        // Default to Resend
        _ => new ResendEmailService(
            sp.GetRequiredService<IHttpClientFactory>(),
            settings, viewEngine, tempDataProvider, sp,
            loggerFactory.CreateLogger<ResendEmailService>())
    };
});

// Admin / Impersonation
builder.Services.AddScoped<IImpersonationService, ImpersonationService>();

// Project Management
builder.Services.AddScoped<IProjectService, ProjectService>();

// Cleanup Jobs
builder.Services.AddScoped<CleanupArtifactsJob>();
builder.Services.AddScoped<CleanupOldBuildsJob>();

// Configuration Validation
builder.Services.AddConfigurationValidation();

// =============================================================================
// Build Application
// =============================================================================

var app = builder.Build();

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
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

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

    // Ensure database and tables exist
    db.Database.EnsureCreated();

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

// MVC routes (kept for parallel operation during migration)
app.MapControllerRoute(
    name: "projects",
    pattern: "projects/{action=Index}/{id?}",
    defaults: new { controller = "Projects" });

app.MapControllerRoute(
    name: "builds",
    pattern: "builds/{action=Details}/{id?}",
    defaults: new { controller = "Builds" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// =============================================================================
// SPA Fallback (React App)
// Serves the React SPA for client-side routing
// =============================================================================

// Fallback to SPA for routes that don't match API or MVC
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
