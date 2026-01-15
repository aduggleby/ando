// =============================================================================
// TestController.cs
//
// Summary: Test-only API endpoints for E2E testing.
//
// This controller is ONLY available in the Testing environment. It provides
// endpoints for creating test users, projects, builds, and simulating events
// without requiring actual GitHub integration.
//
// Design Decisions:
// - Environment check in constructor to fail fast if misconfigured
// - All endpoints require a test API key for additional security
// - Returns structured responses for easy consumption by test code
// =============================================================================

using System.Security.Claims;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Hubs;
using Ando.Server.Models;
using Ando.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Controllers;

/// <summary>
/// Test-only API endpoints for E2E testing.
/// </summary>
[ApiController]
[Route("api/test")]
[AllowAnonymous]
public class TestController : ControllerBase
{
    private readonly AndoDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWebHostEnvironment _env;
    private readonly IBuildService _buildService;
    private readonly IEncryptionService _encryption;
    private readonly IHubContext<BuildLogHub> _hubContext;
    private readonly string _testApiKey;

    public TestController(
        AndoDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWebHostEnvironment env,
        IBuildService buildService,
        IEncryptionService encryption,
        IHubContext<BuildLogHub> hubContext,
        IOptions<TestSettings> testSettings)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _env = env;
        _buildService = buildService;
        _encryption = encryption;
        _hubContext = hubContext;
        _testApiKey = testSettings.Value.ApiKey;

        // Safety check - this controller should NEVER be available outside Testing
        if (!_env.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "TestController is only available in the Testing environment");
        }

        // Verify API key is configured
        if (string.IsNullOrEmpty(_testApiKey))
        {
            throw new InvalidOperationException(
                "Test API key is not configured. Set Test__ApiKey environment variable.");
        }
    }

    // -------------------------------------------------------------------------
    // Health Check
    // -------------------------------------------------------------------------

    /// <summary>
    /// Test API health check.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok", environment = _env.EnvironmentName });
    }

    // -------------------------------------------------------------------------
    // User Management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a test user and returns authentication info.
    /// </summary>
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateTestUserRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        // Generate unique identifiers
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var login = request.Login ?? $"testuser-{uniqueId}";
        var email = request.Email ?? $"{login}@test.example.com";

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = login,
            EmailConfirmed = true, // Test users are automatically verified
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        // Create user with a test password
        var result = await _userManager.CreateAsync(user, $"TestPass123!{uniqueId}");
        if (!result.Succeeded)
        {
            return BadRequest(new { error = "Failed to create user", details = result.Errors });
        }

        // Add to User role
        await _userManager.AddToRoleAsync(user, UserRoles.User);

        return Ok(new CreateTestUserResponse
        {
            UserId = user.Id,
            GitHubId = 0, // No GitHub connection for test users
            Login = login,
            Email = user.Email!,
            AuthToken = GenerateTestAuthToken(user)
        });
    }

    /// <summary>
    /// Authenticates as a test user (sets cookies) using SignInManager.
    /// </summary>
    [HttpPost("users/{userId:int}/login")]
    public async Task<IActionResult> LoginAs(int userId)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        // Use SignInManager to properly sign in with Identity's authentication scheme
        await _signInManager.SignInAsync(user, isPersistent: true);

        return Ok(new { success = true, userId = user.Id });
    }

    /// <summary>
    /// Deletes a test user and all associated data.
    /// </summary>
    [HttpDelete("users/{userId:int}")]
    public async Task<IActionResult> DeleteUser(int userId)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        // Delete user (cascade deletes projects via EF)
        await _userManager.DeleteAsync(user);

        return Ok(new { success = true });
    }

    // -------------------------------------------------------------------------
    // Project Management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a test project for a user.
    /// </summary>
    [HttpPost("projects")]
    public async Task<IActionResult> CreateProject([FromBody] CreateTestProjectRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var user = await _userManager.FindByIdAsync(request.UserId.ToString());
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        var repoName = request.RepoName ?? $"test-repo-{uniqueId}";
        var fullName = $"{user.EffectiveDisplayName}/{repoName}";

        var project = new Project
        {
            OwnerId = user.Id,
            GitHubRepoId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RepoFullName = fullName,
            RepoUrl = $"https://github.com/{fullName}",
            DefaultBranch = request.DefaultBranch ?? "main",
            BranchFilter = request.BranchFilter ?? "main",
            EnablePrBuilds = request.EnablePrBuilds,
            TimeoutMinutes = request.TimeoutMinutes ?? 15,
            NotifyOnFailure = request.NotifyOnFailure,
            NotificationEmail = request.NotificationEmail ?? user.Email,
            CreatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return Ok(new CreateTestProjectResponse
        {
            ProjectId = project.Id,
            RepoFullName = project.RepoFullName,
            RepoUrl = project.RepoUrl
        });
    }

    /// <summary>
    /// Adds a secret to a test project.
    /// </summary>
    [HttpPost("projects/{projectId:int}/secrets")]
    public async Task<IActionResult> AddSecret(int projectId, [FromBody] AddTestSecretRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var project = await _db.Projects.FindAsync(projectId);
        if (project == null)
        {
            return NotFound(new { error = "Project not found" });
        }

        var secret = new ProjectSecret
        {
            ProjectId = projectId,
            Name = request.Name,
            EncryptedValue = _encryption.Encrypt(request.Value),
            CreatedAt = DateTime.UtcNow
        };

        _db.ProjectSecrets.Add(secret);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, secretId = secret.Id });
    }

    // -------------------------------------------------------------------------
    // Build Management
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a test build.
    /// </summary>
    [HttpPost("builds")]
    public async Task<IActionResult> CreateBuild([FromBody] CreateTestBuildRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var project = await _db.Projects.FindAsync(request.ProjectId);
        if (project == null)
        {
            return NotFound(new { error = "Project not found" });
        }

        var uniqueId = Guid.NewGuid().ToString("N");
        var build = new Build
        {
            ProjectId = project.Id,
            CommitSha = request.CommitSha ?? uniqueId.PadRight(40, '0'),
            Branch = request.Branch ?? project.DefaultBranch,
            CommitMessage = request.CommitMessage ?? "Test commit",
            CommitAuthor = request.CommitAuthor ?? "Test User",
            Status = request.Status ?? BuildStatus.Queued,
            Trigger = request.Trigger ?? BuildTrigger.Manual,
            QueuedAt = DateTime.UtcNow,
            StartedAt = request.Status == BuildStatus.Running ? DateTime.UtcNow : null,
            PullRequestNumber = request.PullRequestNumber,
            // Set a fake job ID for test builds so cancel works
            HangfireJobId = $"test-job-{uniqueId}"
        };

        _db.Builds.Add(build);
        await _db.SaveChangesAsync();

        // Update project's last build time
        project.LastBuildAt = build.QueuedAt;
        await _db.SaveChangesAsync();

        return Ok(new CreateTestBuildResponse
        {
            BuildId = build.Id,
            Status = build.Status.ToString()
        });
    }

    /// <summary>
    /// Updates a build's status and optionally adds log entries.
    /// </summary>
    [HttpPatch("builds/{buildId:int}")]
    public async Task<IActionResult> UpdateBuild(int buildId, [FromBody] UpdateTestBuildRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var build = await _db.Builds.FindAsync(buildId);
        if (build == null)
        {
            return NotFound(new { error = "Build not found" });
        }

        if (request.Status.HasValue)
        {
            build.Status = request.Status.Value;

            if (request.Status == BuildStatus.Running && !build.StartedAt.HasValue)
            {
                build.StartedAt = DateTime.UtcNow;
            }

            if (request.Status is BuildStatus.Success or BuildStatus.Failed
                or BuildStatus.Cancelled or BuildStatus.TimedOut)
            {
                build.FinishedAt = DateTime.UtcNow;
                if (build.StartedAt.HasValue)
                {
                    build.Duration = build.FinishedAt.Value - build.StartedAt.Value;
                }
            }
        }

        if (request.ErrorMessage != null)
        {
            build.ErrorMessage = request.ErrorMessage;
        }

        if (request.StepsTotal.HasValue)
        {
            build.StepsTotal = request.StepsTotal.Value;
        }

        if (request.StepsCompleted.HasValue)
        {
            build.StepsCompleted = request.StepsCompleted.Value;
        }

        if (request.StepsFailed.HasValue)
        {
            build.StepsFailed = request.StepsFailed.Value;
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    /// <summary>
    /// Adds log entries to a build and broadcasts them via SignalR.
    /// </summary>
    [HttpPost("builds/{buildId:int}/logs")]
    public async Task<IActionResult> AddLogEntries(int buildId, [FromBody] AddTestLogEntriesRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var build = await _db.Builds.FindAsync(buildId);
        if (build == null)
        {
            return NotFound(new { error = "Build not found" });
        }

        var maxSequence = await _db.BuildLogEntries
            .Where(l => l.BuildId == buildId)
            .MaxAsync(l => (int?)l.Sequence) ?? 0;

        var addedEntries = new List<object>();
        foreach (var entry in request.Entries)
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

            // Store entry data for SignalR broadcast
            addedEntries.Add(new
            {
                sequence = logEntry.Sequence,
                type = logEntry.Type.ToString(),
                message = logEntry.Message,
                stepName = logEntry.StepName,
                timestamp = logEntry.Timestamp
            });
        }

        await _db.SaveChangesAsync();

        // Broadcast log entries via SignalR
        var groupName = BuildLogHub.GetGroupName(buildId);
        foreach (var entry in addedEntries)
        {
            await _hubContext.Clients.Group(groupName).SendAsync("LogReceived", entry);
        }

        return Ok(new { success = true, lastSequence = maxSequence });
    }

    /// <summary>
    /// Adds an artifact to a build.
    /// </summary>
    [HttpPost("builds/{buildId:int}/artifacts")]
    public async Task<IActionResult> AddArtifact(int buildId, [FromBody] AddTestArtifactRequest request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        var build = await _db.Builds.FindAsync(buildId);
        if (build == null)
        {
            return NotFound(new { error = "Build not found" });
        }

        var artifact = new BuildArtifact
        {
            BuildId = buildId,
            Name = request.Name,
            StoragePath = request.StoragePath ?? $"/tmp/test-artifacts/{buildId}/{request.Name}",
            SizeBytes = request.SizeBytes ?? 1024,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _db.BuildArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, artifactId = artifact.Id });
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cleans up all test data (for test isolation).
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup([FromBody] CleanupRequest? request)
    {
        if (!ValidateApiKey())
        {
            return Unauthorized(new { error = "Invalid API key" });
        }

        if (request?.UserId.HasValue == true)
        {
            // Clean up specific user
            var user = await _userManager.FindByIdAsync(request.UserId.Value.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        }
        else if (request?.ProjectId.HasValue == true)
        {
            // Clean up specific project
            var project = await _db.Projects.FindAsync(request.ProjectId);
            if (project != null)
            {
                _db.Projects.Remove(project);
                await _db.SaveChangesAsync();
            }
        }

        return Ok(new { success = true });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private bool ValidateApiKey()
    {
        var apiKey = Request.Headers["X-Test-Api-Key"].FirstOrDefault();
        return !string.IsNullOrEmpty(apiKey) && apiKey == _testApiKey;
    }

    private static string GenerateTestAuthToken(ApplicationUser user)
    {
        // For test purposes, we generate a simple token
        // The actual authentication uses cookies set by LoginAs
        return Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{user.Id}:{user.EffectiveDisplayName}:{DateTime.UtcNow.Ticks}"));
    }
}

// =============================================================================
// Request/Response DTOs
// =============================================================================

public record CreateTestUserRequest
{
    public string? Login { get; init; }
    public string? Email { get; init; }
}

public record CreateTestUserResponse
{
    public int UserId { get; init; }
    public long GitHubId { get; init; }
    public string Login { get; init; } = "";
    public string Email { get; init; } = "";
    public string AuthToken { get; init; } = "";
}

public record CreateTestProjectRequest
{
    public int UserId { get; init; }
    public string? RepoName { get; init; }
    public string? DefaultBranch { get; init; }
    public string? BranchFilter { get; init; }
    public bool EnablePrBuilds { get; init; }
    public int? TimeoutMinutes { get; init; }
    public bool NotifyOnFailure { get; init; } = true;
    public string? NotificationEmail { get; init; }
}

public record CreateTestProjectResponse
{
    public int ProjectId { get; init; }
    public string RepoFullName { get; init; } = "";
    public string RepoUrl { get; init; } = "";
}

public record AddTestSecretRequest
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
}

public record CreateTestBuildRequest
{
    public int ProjectId { get; init; }
    public string? CommitSha { get; init; }
    public string? Branch { get; init; }
    public string? CommitMessage { get; init; }
    public string? CommitAuthor { get; init; }
    public BuildStatus? Status { get; init; }
    public BuildTrigger? Trigger { get; init; }
    public int? PullRequestNumber { get; init; }
}

public record CreateTestBuildResponse
{
    public int BuildId { get; init; }
    public string Status { get; init; } = "";
}

public record UpdateTestBuildRequest
{
    public BuildStatus? Status { get; init; }
    public string? ErrorMessage { get; init; }
    public int? StepsTotal { get; init; }
    public int? StepsCompleted { get; init; }
    public int? StepsFailed { get; init; }
}

public record AddTestLogEntriesRequest
{
    public List<TestLogEntry> Entries { get; init; } = [];
}

public record TestLogEntry
{
    public LogEntryType? Type { get; init; }
    public string Message { get; init; } = "";
    public string? StepName { get; init; }
}

public record AddTestArtifactRequest
{
    public string Name { get; init; } = "";
    public string? StoragePath { get; init; }
    public long? SizeBytes { get; init; }
}

public record CleanupRequest
{
    public int? UserId { get; init; }
    public int? ProjectId { get; init; }
}
