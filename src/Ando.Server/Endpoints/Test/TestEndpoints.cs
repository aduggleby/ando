// =============================================================================
// TestEndpoints.cs
//
// Summary: Test-only FastEndpoints used by Playwright E2E infrastructure.
//
// These endpoints replace the legacy TestController and are only intended for
// Testing/E2E environments. All mutating routes require X-Test-Api-Key.
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Hubs;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Endpoints.Test;

/// <summary>
/// GET /api/test/health - test API health check.
/// </summary>
public class TestHealthEndpoint : EndpointWithoutRequest<object>
{
    private readonly IWebHostEnvironment _env;

    public TestHealthEndpoint(IWebHostEnvironment env)
    {
        _env = env;
        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
    }

    public override void Configure()
    {
        Get("/test/health");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!TestEndpointGuard.IsTestEnvironment(_env))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(new { status = "ok", environment = _env.EnvironmentName }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/users - create an isolated test user.
/// </summary>
public class CreateTestUserEndpoint : ApiKeyProtectedTestEndpoint<CreateTestUserRequest>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public CreateTestUserEndpoint(
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/users");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateTestUserRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var login = req.Login ?? $"testuser-{uniqueId}";
        var email = req.Email ?? $"{login}@test.example.com";

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = login,
            EmailConfirmed = true,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, $"TestPass123!{uniqueId}");
        if (!result.Succeeded)
        {
            await SendAsync(new { error = "Failed to create user", details = result.Errors }, 400, ct);
            return;
        }

        await _userManager.AddToRoleAsync(user, UserRoles.User);

        await SendAsync(new CreateTestUserResponse
        {
            UserId = user.Id,
            GitHubId = 0,
            Login = login,
            Email = user.Email!,
            AuthToken = TestEndpointGuard.GenerateTestAuthToken(user)
        }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/users/{userId}/login - signs in as an existing test user.
/// </summary>
public class LoginAsTestUserEndpoint : ApiKeyProtectedTestEndpointWithoutRequest
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public LoginAsTestUserEndpoint(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/users/{userId:int}/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var userId = Route<int>("userId");
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendAsync(new { error = "User not found" }, 404, ct);
            return;
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        await SendAsync(new { success = true, userId = user.Id }, cancellation: ct);
    }
}

/// <summary>
/// GET /api/test/users/{userId}/verification-token - get current email verification token.
/// </summary>
public class GetTestUserVerificationTokenEndpoint : ApiKeyProtectedTestEndpointWithoutRequest
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public GetTestUserVerificationTokenEndpoint(
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Get("/test/users/{userId:int}/verification-token");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var userId = Route<int>("userId");
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendAsync(new { error = "User not found" }, 404, ct);
            return;
        }

        await SendAsync(new TestEmailVerificationTokenResponse
        {
            EmailVerified = user.EmailVerified,
            Token = user.EmailVerificationToken
        }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/users/{userId}/password-reset-token - generate reset token.
/// </summary>
public class GenerateTestPasswordResetTokenEndpoint : ApiKeyProtectedTestEndpointWithoutRequest
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public GenerateTestPasswordResetTokenEndpoint(
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/users/{userId:int}/password-reset-token");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var userId = Route<int>("userId");
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendAsync(new { error = "User not found" }, 404, ct);
            return;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        await SendAsync(new TestPasswordResetTokenResponse
        {
            Token = token
        }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/users/{userId}/role - assign a specific role to a test user.
/// </summary>
public class SetTestUserRoleEndpoint : ApiKeyProtectedTestEndpoint<SetTestUserRoleRequest>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public SetTestUserRoleEndpoint(
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/users/{userId:int}/role");
        AllowAnonymous();
    }

    public override async Task HandleAsync(SetTestUserRoleRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        if (!UserRoles.AllRoles.Contains(req.Role, StringComparer.Ordinal))
        {
            await SendAsync(new { error = "Invalid role" }, 400, ct);
            return;
        }

        var userId = Route<int>("userId");
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendAsync(new { error = "User not found" }, 404, ct);
            return;
        }

        foreach (var role in UserRoles.AllRoles)
        {
            if (await _userManager.IsInRoleAsync(user, role))
            {
                await _userManager.RemoveFromRoleAsync(user, role);
            }
        }

        var addResult = await _userManager.AddToRoleAsync(user, req.Role);
        if (!addResult.Succeeded)
        {
            await SendAsync(new { error = "Failed to set role", details = addResult.Errors }, 400, ct);
            return;
        }

        await SendAsync(new { success = true, userId = user.Id, role = req.Role }, cancellation: ct);
    }
}

/// <summary>
/// DELETE /api/test/users/{userId} - removes a test user and associated data.
/// </summary>
public class DeleteTestUserEndpoint : ApiKeyProtectedTestEndpointWithoutRequest
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public DeleteTestUserEndpoint(
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Delete("/test/users/{userId:int}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var userId = Route<int>("userId");
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendAsync(new { error = "User not found" }, 404, ct);
            return;
        }

        await _userManager.DeleteAsync(user);
        await SendAsync(new { success = true }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/projects - create a test project.
/// </summary>
public class CreateTestProjectEndpoint : ApiKeyProtectedTestEndpoint<CreateTestProjectRequest>
{
    private readonly AndoDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public CreateTestProjectEndpoint(
        AndoDbContext db,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/projects");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateTestProjectRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var user = await _userManager.FindByIdAsync(req.UserId.ToString());
        if (user == null)
        {
            await SendAsync(new { error = "User not found" }, 404, ct);
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var repoName = req.RepoName ?? $"test-repo-{uniqueId}";
        var fullName = $"{user.EffectiveDisplayName}/{repoName}";

        var project = new Project
        {
            OwnerId = user.Id,
            GitHubRepoId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RepoFullName = fullName,
            RepoUrl = $"https://github.com/{fullName}",
            DefaultBranch = req.DefaultBranch ?? "main",
            BranchFilter = req.BranchFilter ?? "main",
            Profile = req.Profile,
            EnablePrBuilds = req.EnablePrBuilds,
            TimeoutMinutes = req.TimeoutMinutes ?? 15,
            NotifyOnFailure = req.NotifyOnFailure,
            NotificationEmail = req.NotificationEmail ?? user.Email,
            CreatedAt = DateTime.UtcNow
        };

        if (req.AvailableProfiles is { Count: > 0 })
        {
            project.SetAvailableProfiles(req.AvailableProfiles);
        }

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        await SendAsync(new CreateTestProjectResponse
        {
            ProjectId = project.Id,
            RepoFullName = project.RepoFullName,
            RepoUrl = project.RepoUrl
        }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/projects/{projectId}/secrets - add a test secret.
/// </summary>
public class AddTestSecretEndpoint : ApiKeyProtectedTestEndpoint<AddTestSecretRequest>
{
    private readonly AndoDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public AddTestSecretEndpoint(
        AndoDbContext db,
        IEncryptionService encryption,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _encryption = encryption;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/projects/{projectId:int}/secrets");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AddTestSecretRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var projectId = Route<int>("projectId");
        var project = await _db.Projects.FindAsync([projectId], ct);
        if (project == null)
        {
            await SendAsync(new { error = "Project not found" }, 404, ct);
            return;
        }

        var secret = new ProjectSecret
        {
            ProjectId = projectId,
            Name = req.Name,
            EncryptedValue = _encryption.Encrypt(req.Value),
            CreatedAt = DateTime.UtcNow
        };

        _db.ProjectSecrets.Add(secret);
        await _db.SaveChangesAsync(ct);

        await SendAsync(new { success = true, secretId = secret.Id }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/builds - create a synthetic build.
/// </summary>
public class CreateTestBuildEndpoint : ApiKeyProtectedTestEndpoint<CreateTestBuildRequest>
{
    private readonly AndoDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public CreateTestBuildEndpoint(
        AndoDbContext db,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/builds");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateTestBuildRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var project = await _db.Projects.FindAsync([req.ProjectId], ct);
        if (project == null)
        {
            await SendAsync(new { error = "Project not found" }, 404, ct);
            return;
        }

        var uniqueId = Guid.NewGuid().ToString("N");
        var build = new Build
        {
            ProjectId = project.Id,
            CommitSha = req.CommitSha ?? uniqueId.PadRight(40, '0'),
            Branch = req.Branch ?? project.DefaultBranch,
            CommitMessage = req.CommitMessage ?? "Test commit",
            CommitAuthor = req.CommitAuthor ?? "Test User",
            Status = req.Status ?? BuildStatus.Queued,
            Trigger = req.Trigger ?? BuildTrigger.Manual,
            QueuedAt = DateTime.UtcNow,
            StartedAt = req.Status == BuildStatus.Running ? DateTime.UtcNow : null,
            PullRequestNumber = req.PullRequestNumber,
            Profile = project.Profile,
            HangfireJobId = $"test-job-{uniqueId}"
        };

        _db.Builds.Add(build);
        await _db.SaveChangesAsync(ct);

        project.LastBuildAt = build.QueuedAt;
        await _db.SaveChangesAsync(ct);

        await SendAsync(new CreateTestBuildResponse
        {
            BuildId = build.Id,
            Status = build.Status.ToString()
        }, cancellation: ct);
    }
}

/// <summary>
/// PATCH /api/test/builds/{buildId} - update test build status/details.
/// </summary>
public class UpdateTestBuildEndpoint : ApiKeyProtectedTestEndpoint<UpdateTestBuildRequest>
{
    private readonly AndoDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public UpdateTestBuildEndpoint(
        AndoDbContext db,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Patch("/test/builds/{buildId:int}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateTestBuildRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var buildId = Route<int>("buildId");
        var build = await _db.Builds.FindAsync([buildId], ct);
        if (build == null)
        {
            await SendAsync(new { error = "Build not found" }, 404, ct);
            return;
        }

        if (req.Status.HasValue)
        {
            build.Status = req.Status.Value;

            if (req.Status == BuildStatus.Running && !build.StartedAt.HasValue)
            {
                build.StartedAt = DateTime.UtcNow;
            }

            if (req.Status is BuildStatus.Success or BuildStatus.Failed or BuildStatus.Cancelled or BuildStatus.TimedOut)
            {
                build.FinishedAt = DateTime.UtcNow;
                if (build.StartedAt.HasValue)
                {
                    build.Duration = build.FinishedAt.Value - build.StartedAt.Value;
                }
            }
        }

        if (req.ErrorMessage != null)
        {
            build.ErrorMessage = req.ErrorMessage;
        }

        if (req.StepsTotal.HasValue)
        {
            build.StepsTotal = req.StepsTotal.Value;
        }

        if (req.StepsCompleted.HasValue)
        {
            build.StepsCompleted = req.StepsCompleted.Value;
        }

        if (req.StepsFailed.HasValue)
        {
            build.StepsFailed = req.StepsFailed.Value;
        }

        await _db.SaveChangesAsync(ct);
        await SendAsync(new { success = true }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/builds/{buildId}/logs - append log entries and broadcast.
/// </summary>
public class AddTestLogEntriesEndpoint : ApiKeyProtectedTestEndpoint<AddTestLogEntriesRequest>
{
    private readonly AndoDbContext _db;
    private readonly IHubContext<BuildLogHub> _hubContext;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public AddTestLogEntriesEndpoint(
        AndoDbContext db,
        IHubContext<BuildLogHub> hubContext,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _hubContext = hubContext;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/builds/{buildId:int}/logs");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AddTestLogEntriesRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var buildId = Route<int>("buildId");
        var build = await _db.Builds.FindAsync([buildId], ct);
        if (build == null)
        {
            await SendAsync(new { error = "Build not found" }, 404, ct);
            return;
        }

        var maxSequence = await _db.BuildLogEntries
            .Where(l => l.BuildId == buildId)
            .MaxAsync(l => (int?)l.Sequence, ct) ?? 0;

        var addedEntries = new List<object>();
        foreach (var entry in req.Entries)
        {
            maxSequence++;
            var logEntry = new BuildLogEntry
            {
                BuildId = buildId,
                Sequence = maxSequence,
                Type = entry.Type ?? LogEntryType.Output,
                Message = entry.Message,
                StepName = entry.StepName,
                Timestamp = DateTime.UtcNow
            };

            _db.BuildLogEntries.Add(logEntry);

            addedEntries.Add(new
            {
                sequence = logEntry.Sequence,
                type = logEntry.Type.ToString(),
                message = logEntry.Message,
                stepName = logEntry.StepName,
                timestamp = logEntry.Timestamp
            });
        }

        await _db.SaveChangesAsync(ct);

        var groupName = BuildLogHub.GetGroupName(buildId);
        foreach (var entry in addedEntries)
        {
            await _hubContext.Clients.Group(groupName).SendAsync("LogReceived", entry, ct);
        }

        await SendAsync(new { success = true, lastSequence = maxSequence }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/builds/{buildId}/artifacts - attach synthetic artifact.
/// </summary>
public class AddTestArtifactEndpoint : ApiKeyProtectedTestEndpoint<AddTestArtifactRequest>
{
    private readonly AndoDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public AddTestArtifactEndpoint(
        AndoDbContext db,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/builds/{buildId:int}/artifacts");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AddTestArtifactRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        var buildId = Route<int>("buildId");
        var build = await _db.Builds.FindAsync([buildId], ct);
        if (build == null)
        {
            await SendAsync(new { error = "Build not found" }, 404, ct);
            return;
        }

        var artifact = new BuildArtifact
        {
            BuildId = buildId,
            Name = req.Name,
            StoragePath = req.StoragePath ?? $"/tmp/test-artifacts/{buildId}/{req.Name}",
            SizeBytes = req.SizeBytes ?? 1024,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _db.BuildArtifacts.Add(artifact);
        await _db.SaveChangesAsync(ct);

        await SendAsync(new { success = true, artifactId = artifact.Id }, cancellation: ct);
    }
}

/// <summary>
/// POST /api/test/cleanup - remove specific test entities for isolation.
/// </summary>
public class TestCleanupEndpoint : ApiKeyProtectedTestEndpoint<CleanupRequest>
{
    private readonly AndoDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly string _testApiKey;

    protected override string TestApiKey => _testApiKey;

    public TestCleanupEndpoint(
        AndoDbContext db,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _userManager = userManager;
        _env = env;
        _testApiKey = testSettings.Value.ApiKey;

        TestEndpointGuard.EnsureTestEnvironmentOrThrow(_env);
        TestEndpointGuard.EnsureApiKeyConfiguredOrThrow(_testApiKey);
    }

    public override void Configure()
    {
        Post("/test/cleanup");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CleanupRequest req, CancellationToken ct)
    {
        if (!await EnsureApiKeyAsync(ct))
        {
            return;
        }

        if (req.UserId.HasValue)
        {
            var user = await _userManager.FindByIdAsync(req.UserId.Value.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        }
        else if (req.ProjectId.HasValue)
        {
            var project = await _db.Projects.FindAsync([req.ProjectId.Value], ct);
            if (project != null)
            {
                _db.Projects.Remove(project);
                await _db.SaveChangesAsync(ct);
            }
        }

        await SendAsync(new { success = true }, cancellation: ct);
    }
}
