// =============================================================================
// CloudflareCredentialsTests.cs
//
// Summary: Cloudflare.UseCredentials passes explicit credentials per command
// (environment, never argv) and registers them for redaction.
// =============================================================================

using Ando.Logging;
using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
[Collection("SecretValues")]
public class CloudflareCredentialsTests : IDisposable
{
    private readonly StepRegistry _registry = new();
    private readonly MockExecutor _executor = new();
    private readonly string? _token = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
    private readonly string? _account = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");

    public CloudflareCredentialsTests()
    {
        SecretValues.Clear();
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", null);
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", _token);
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", _account);
        SecretValues.Clear();
    }

    private CloudflareOperations Create() => new(_registry, new TestLogger(), () => _executor);

    [Fact]
    public async Task UseCredentials_WithoutEnvironment_PassesPerCommandEnvironmentNotArgv()
    {
        var cf = Create();
        cf.UseCredentials("explicit-token-value", "explicit-account-id");

        cf.EnsureAuthenticated();
        cf.PagesDeploy(new DirectoryRef("./dist"), "site");
        cf.PurgeCache("0123456789abcdef0123456789abcdef");
        foreach (var step in _registry.Steps)
        {
            (await step.Execute()).ShouldBeTrue();
        }

        _registry.Steps.Count.ShouldBe(4);
        foreach (var command in _executor.ExecutedCommands)
        {
            command.Options!.Environment["CLOUDFLARE_API_TOKEN"].ShouldBe("explicit-token-value");
            command.Options.Environment["CLOUDFLARE_ACCOUNT_ID"].ShouldBe("explicit-account-id");
            command.CommandLine.ShouldNotContain("explicit-token-value");
            command.CommandLine.ShouldNotContain("explicit-account-id");
        }
    }

    [Fact]
    public async Task UseCredentials_OverridesEnvironmentValues()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", "env-token-value");
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", "env-account-id");
        var cf = Create();
        cf.UseCredentials("explicit-token-value", "explicit-account-id");

        cf.EnsureAuthenticated();
        await _registry.Steps[0].Execute();

        _executor.ExecutedCommands[0].Options!.Environment["CLOUDFLARE_API_TOKEN"].ShouldBe("explicit-token-value");
        _executor.ExecutedCommands[0].Options!.Environment["CLOUDFLARE_ACCOUNT_ID"].ShouldBe("explicit-account-id");
    }

    [Fact]
    public async Task UseCredentials_TokenOnly_AccountComesFromEnvironment()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", "env-account-id");
        var cf = Create();
        cf.UseCredentials("explicit-token-value");

        cf.PagesListProjects();
        await _registry.Steps[0].Execute();

        var env = _executor.ExecutedCommands[0].Options!.Environment;
        env["CLOUDFLARE_API_TOKEN"].ShouldBe("explicit-token-value");
        env["CLOUDFLARE_ACCOUNT_ID"].ShouldBe("env-account-id");
    }

    [Fact]
    public void UseCredentials_RegistersTokenAndAccountForRedaction()
    {
        Create().UseCredentials("explicit-token-value", "explicit-account-id");

        SecretValues.Redact("explicit-token-value explicit-account-id").ShouldBe("[REDACTED] [REDACTED]");
    }

    [Fact]
    public void WithoutUseCredentials_MissingEnvironment_StillFails()
    {
        Should.Throw<InvalidOperationException>(() => Create().PagesListProjects());
    }

    [Fact]
    public void UseCredentials_EmptyToken_Throws()
    {
        Should.Throw<ArgumentException>(() => Create().UseCredentials(" "));
    }
}
