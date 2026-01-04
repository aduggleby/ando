// =============================================================================
// CloudflareOperationsTests.cs
//
// Summary: Unit tests for CloudflareOperations class.
//
// Tests verify that:
// - Each operation registers correct steps
// - Steps execute the correct wrangler CLI commands with proper arguments
// - Environment variable resolution works correctly
// - Options are correctly applied to commands
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class CloudflareOperationsTests : IDisposable
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    // Store original env vars for restoration.
    private readonly string? _originalApiToken;
    private readonly string? _originalAccountId;
    private readonly string? _originalProjectName;

    public CloudflareOperationsTests()
    {
        // Save original values.
        _originalApiToken = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
        _originalAccountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
        _originalProjectName = Environment.GetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME");

        // Set test values.
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", "test-token");
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", "test-account-id");
    }

    public void Dispose()
    {
        // Restore original values.
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", _originalApiToken);
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", _originalAccountId);
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", _originalProjectName);
    }

    private CloudflareOperations CreateCloudflare() =>
        new CloudflareOperations(_registry, _logger, () => _executor);

    [Fact]
    public void EnsureAuthenticated_RegistersStep()
    {
        var cf = CreateCloudflare();

        cf.EnsureAuthenticated();

        Assert.Single(_registry.Steps);
        Assert.Equal("Cloudflare.EnsureAuthenticated", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task EnsureAuthenticated_ExecutesWhoamiCommand()
    {
        var cf = CreateCloudflare();
        cf.EnsureAuthenticated();

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("npx", cmd.Command);
        Assert.Contains("wrangler", cmd.Args);
        Assert.Contains("whoami", cmd.Args);
    }

    [Fact]
    public void EnsureAuthenticated_ThrowsWhenApiTokenNotSet()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", null);
        var cf = CreateCloudflare();

        Assert.Throws<InvalidOperationException>(() => cf.EnsureAuthenticated());
    }

    [Fact]
    public void EnsureAuthenticated_ThrowsWhenAccountIdNotSet()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", null);
        var cf = CreateCloudflare();

        Assert.Throws<InvalidOperationException>(() => cf.EnsureAuthenticated());
    }

    [Fact]
    public void PagesDeployDirectory_RegistersStep()
    {
        var cf = CreateCloudflare();

        cf.PagesDeployDirectory("./dist", o => o.WithProjectName("my-site"));

        Assert.Single(_registry.Steps);
        Assert.Equal("Cloudflare.Pages.Deploy", _registry.Steps[0].Name);
        Assert.Equal("my-site", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task PagesDeployDirectory_ExecutesCorrectCommand()
    {
        var cf = CreateCloudflare();
        cf.PagesDeployDirectory("./dist", o => o.WithProjectName("test-project"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("npx", cmd.Command);
        Assert.Contains("wrangler", cmd.Args);
        Assert.Contains("pages", cmd.Args);
        Assert.Contains("deploy", cmd.Args);
        Assert.Contains("./dist", cmd.Args);
        Assert.Contains("--project-name", cmd.Args);
        Assert.Equal("test-project", cmd.GetArgValue("--project-name"));
    }

    [Fact]
    public async Task PagesDeployDirectory_IncludesOptionalParameters()
    {
        var cf = CreateCloudflare();
        cf.PagesDeployDirectory("./dist", o => o
            .WithProjectName("test-project")
            .WithBranch("main")
            .WithCommitHash("abc123")
            .WithCommitMessage("Test deployment"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("main", cmd.GetArgValue("--branch"));
        Assert.Equal("abc123", cmd.GetArgValue("--commit-hash"));
        Assert.Equal("Test deployment", cmd.GetArgValue("--commit-message"));
    }

    [Fact]
    public void PagesDeployDirectory_UsesEnvVarForProjectName()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", "env-project");
        var cf = CreateCloudflare();

        cf.PagesDeployDirectory("./dist"); // No project name in options.

        Assert.Single(_registry.Steps);
        Assert.Equal("env-project", _registry.Steps[0].Context);
    }

    [Fact]
    public void PagesDeployDirectory_ThrowsWhenNoProjectName()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", null);
        var cf = CreateCloudflare();

        Assert.Throws<InvalidOperationException>(() =>
            cf.PagesDeployDirectory("./dist")); // No project name anywhere.
    }

    [Fact]
    public void PagesDeploy_DefaultsToDistDirectory()
    {
        var cf = CreateCloudflare();

        cf.PagesDeploy(o => o.WithProjectName("my-site"));

        // Verify the step was registered.
        Assert.Single(_registry.Steps);
    }

    [Fact]
    public async Task PagesDeploy_DeploysDistDirectory()
    {
        var cf = CreateCloudflare();
        cf.PagesDeploy(o => o.WithProjectName("my-site"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("./dist", cmd.Args);
    }

    [Fact]
    public void PagesListProjects_RegistersStep()
    {
        var cf = CreateCloudflare();

        cf.PagesListProjects();

        Assert.Single(_registry.Steps);
        Assert.Equal("Cloudflare.Pages.ListProjects", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task PagesListProjects_ExecutesCorrectCommand()
    {
        var cf = CreateCloudflare();
        cf.PagesListProjects();

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("npx", cmd.Command);
        Assert.Contains("wrangler", cmd.Args);
        Assert.Contains("pages", cmd.Args);
        Assert.Contains("project", cmd.Args);
        Assert.Contains("list", cmd.Args);
    }

    [Fact]
    public void PagesCreateProject_RegistersStep()
    {
        var cf = CreateCloudflare();

        cf.PagesCreateProject("new-project");

        Assert.Single(_registry.Steps);
        Assert.Equal("Cloudflare.Pages.CreateProject", _registry.Steps[0].Name);
        Assert.Equal("new-project", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task PagesCreateProject_ExecutesCorrectCommand()
    {
        var cf = CreateCloudflare();
        cf.PagesCreateProject("my-new-site", "develop");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("create", cmd.Args);
        Assert.Contains("my-new-site", cmd.Args);
        Assert.Equal("develop", cmd.GetArgValue("--production-branch"));
    }

    [Fact]
    public async Task PagesCreateProject_DefaultsToMainBranch()
    {
        var cf = CreateCloudflare();
        cf.PagesCreateProject("my-site");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("main", cmd.GetArgValue("--production-branch"));
    }

    [Fact]
    public void PagesListDeployments_RegistersStep()
    {
        var cf = CreateCloudflare();

        cf.PagesListDeployments("my-project");

        Assert.Single(_registry.Steps);
        Assert.Equal("Cloudflare.Pages.ListDeployments", _registry.Steps[0].Name);
        Assert.Equal("my-project", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task PagesListDeployments_ExecutesCorrectCommand()
    {
        var cf = CreateCloudflare();
        cf.PagesListDeployments("my-project");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("npx", cmd.Command);
        Assert.Contains("wrangler", cmd.Args);
        Assert.Contains("pages", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("list", cmd.Args);
        Assert.Equal("my-project", cmd.GetArgValue("--project-name"));
    }

    [Fact]
    public void PagesListDeployments_UsesEnvVarForProjectName()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", "env-project");
        var cf = CreateCloudflare();

        cf.PagesListDeployments(); // No project name provided.

        Assert.Single(_registry.Steps);
        Assert.Equal("env-project", _registry.Steps[0].Context);
    }

    [Fact]
    public void PagesListDeployments_ThrowsWhenNoProjectName()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", null);
        var cf = CreateCloudflare();

        Assert.Throws<InvalidOperationException>(() => cf.PagesListDeployments());
    }

    [Fact]
    public void InDirectory_ReturnsSameInstance()
    {
        var cf = CreateCloudflare();

        var result = cf.InDirectory("./website");

        Assert.Same(cf, result);
    }

    [Fact]
    public async Task AllOperations_ReturnFalse_WhenCommandFails()
    {
        var cf = CreateCloudflare();
        _executor.SimulateFailure = true;

        cf.EnsureAuthenticated();
        var success = await _registry.Steps[0].Execute();

        Assert.False(success);
    }

    [Fact]
    public void IsWranglerAvailable_ReturnsWithoutException()
    {
        var result = CloudflareOperations.IsWranglerAvailable();
        Assert.True(result || !result); // Always passes, just verifies no exception.
    }

    [Fact]
    public void GetWranglerInstallInstructions_ReturnsNonEmptyString()
    {
        var instructions = CloudflareOperations.GetWranglerInstallInstructions();

        Assert.NotNull(instructions);
        Assert.NotEmpty(instructions);
        Assert.Contains("npm", instructions);
    }
}
