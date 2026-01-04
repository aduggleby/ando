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
using Ando.Server.Services;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// Configuration
// =============================================================================

builder.Services.Configure<GitHubSettings>(
    builder.Configuration.GetSection(GitHubSettings.SectionName));
builder.Services.Configure<ResendSettings>(
    builder.Configuration.GetSection(ResendSettings.SectionName));
builder.Services.Configure<StorageSettings>(
    builder.Configuration.GetSection(StorageSettings.SectionName));
builder.Services.Configure<BuildSettings>(
    builder.Configuration.GetSection(BuildSettings.SectionName));
builder.Services.Configure<EncryptionSettings>(
    builder.Configuration.GetSection(EncryptionSettings.SectionName));

// =============================================================================
// Database
// =============================================================================

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<AndoDbContext>(options =>
    options.UseSqlServer(connectionString));

// =============================================================================
// Hangfire (Background Jobs)
// =============================================================================

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

// =============================================================================
// SignalR (Real-time)
// =============================================================================

builder.Services.AddSignalR();

// =============================================================================
// Authentication
// =============================================================================

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

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
// MVC & Razor
// =============================================================================

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

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

// Build Execution
builder.Services.AddSingleton<CancellationTokenRegistry>();
builder.Services.AddScoped<IBuildOrchestrator, BuildOrchestrator>();
builder.Services.AddScoped<IBuildService, BuildService>();

// Email
builder.Services.AddScoped<IEmailService, ResendEmailService>();
builder.Services.AddSingleton<ITempDataProvider, SessionStateTempDataProvider>();

// Project Management
builder.Services.AddScoped<IProjectService, ProjectService>();

// Cleanup Jobs
builder.Services.AddScoped<CleanupArtifactsJob>();
builder.Services.AddScoped<CleanupOldBuildsJob>();

// =============================================================================
// Build Application
// =============================================================================

var app = builder.Build();

// =============================================================================
// Middleware Pipeline
// =============================================================================

if (!app.Environment.IsDevelopment())
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
// Recurring Jobs
// =============================================================================

// Skip recurring job registration during testing (no JobStorage configured)
if (!app.Environment.IsEnvironment("Testing"))
{
    // Cleanup expired artifacts every hour
    RecurringJob.AddOrUpdate<CleanupArtifactsJob>(
        "cleanup-artifacts",
        job => job.ExecuteAsync(),
        Cron.Hourly);

    // Cleanup stale builds every 15 minutes
    RecurringJob.AddOrUpdate<CleanupOldBuildsJob>(
        "cleanup-stale-builds",
        job => job.ExecuteAsync(),
        "*/15 * * * *");
}

// MVC routes
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
// Database Migration (Development)
// =============================================================================

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AndoDbContext>();
    db.Database.Migrate();
}

app.Run();

// =============================================================================
// Program Class Marker (for WebApplicationFactory in tests)
// =============================================================================

/// <summary>
/// Partial class marker to make Program accessible for integration tests.
/// </summary>
public partial class Program { }
