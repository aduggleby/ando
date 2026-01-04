// =============================================================================
// AndoWebApplicationFactory.cs
//
// Summary: Custom WebApplicationFactory for integration testing.
//
// Configures the test server with in-memory database and mock external services.
// Allows integration tests to exercise the full HTTP pipeline while controlling
// external dependencies.
//
// Design Decisions:
// - Uses in-memory database for speed and isolation
// - Mocks Hangfire to avoid real job scheduling
// - Provides access to database for test setup and verification
// =============================================================================

using Ando.Server.BuildExecution;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Hangfire;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory for integration tests.
/// </summary>
public class AndoWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>
    /// The mock Hangfire job client for verifying job enqueuing.
    /// </summary>
    public Mock<IBackgroundJobClient> MockJobClient { get; } = new();

    /// <summary>
    /// List of job IDs that have been "enqueued".
    /// </summary>
    public List<string> EnqueuedJobIds { get; } = [];

    /// <summary>
    /// Webhook secret used for signature validation.
    /// </summary>
    public string WebhookSecret { get; set; } = "test-webhook-secret-for-integration";

    public AndoWebApplicationFactory()
    {
        // Setup mock job client to return incrementing job IDs
        var jobCounter = 0;
        MockJobClient
            .Setup(x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<IState>()))
            .Returns(() =>
            {
                var jobId = $"test-job-{++jobCounter}";
                EnqueuedJobIds.Add(jobId);
                return jobId;
            });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations to avoid provider conflicts
            var dbContextDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AndoDbContext>)
                    || d.ServiceType == typeof(AndoDbContext)
                    || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database with clean registration
            services.AddDbContext<AndoDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Remove real Hangfire services
            services.RemoveAll<IBackgroundJobClient>();
            services.RemoveAll<IRecurringJobManager>();

            // Add mock Hangfire client
            services.AddSingleton(MockJobClient.Object);
            services.AddSingleton(new Mock<IRecurringJobManager>().Object);

            // Configure settings for testing
            services.Configure<GitHubSettings>(options =>
            {
                options.WebhookSecret = WebhookSecret;
                options.ClientId = "test-client-id";
                options.ClientSecret = "test-client-secret";
                options.AppId = "12345";
            });

            services.Configure<EncryptionSettings>(options =>
            {
                // Valid 32-byte key for AES-256
                options.Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
            });

            services.Configure<StorageSettings>(options =>
            {
                options.ArtifactsPath = "/tmp/ando-test-artifacts";
                options.ArtifactRetentionDays = 30;
            });

            services.Configure<BuildSettings>(options =>
            {
                options.DefaultTimeoutMinutes = 15;
                options.MaxTimeoutMinutes = 60;
                options.WorkerCount = 1;
            });

            // Register CancellationTokenRegistry as singleton (required by BuildService)
            services.RemoveAll<CancellationTokenRegistry>();
            services.AddSingleton<CancellationTokenRegistry>();
        });
    }

    /// <summary>
    /// Creates a new scope and returns the database context for test setup.
    /// </summary>
    public AndoDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AndoDbContext>();
    }

    /// <summary>
    /// Gets a fresh database context using an existing scope.
    /// </summary>
    public AndoDbContext GetDbContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<AndoDbContext>();
    }
}
