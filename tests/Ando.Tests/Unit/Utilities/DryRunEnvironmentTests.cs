// =============================================================================
// DryRunEnvironmentTests.cs
//
// Summary: A --dry-run uses placeholders for missing required environment
// variables (Env, EnvironmentHelper, Cloudflare/NuGet/Azure credentials) and
// lists them in one warning; real runs still fail.
//
// Design Decisions:
// - Variable names are unique per test (never set in the process), so these
//   tests run in parallel without touching shared state.
// - Azure and NuGet dry-run tests live in AzureOperationsTests and
//   NugetOperationsTests, which own those fixed variable names.
// - The Cloudflare test uses the fixed CLOUDFLARE_* names and joins the
//   SecretValues collection with the other tests that change them.
// =============================================================================

using Ando.Operations;
using Ando.Scripting;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class DryRunEnvironmentTests
{
    private static string UniqueName() => "ANDO_TEST_MISSING_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    [Fact]
    public void GetRequired_InDryRun_ReturnsPlaceholderAndRecordsName()
    {
        var name = UniqueName();
        var tracker = new DryRunEnvironment();

        using (DryRunEnvironment.Activate(tracker))
        {
            EnvironmentHelper.GetRequired(name, "Test").ShouldBe($"<{name}>");
            EnvironmentHelper.GetRequired(name).ShouldBe($"<{name}>");
        }

        tracker.MissingNames.ShouldBe([name]);
        Environment.GetEnvironmentVariable(name).ShouldBeNull();
    }

    [Fact]
    public void GetRequiredOrPrompt_InDryRun_ReturnsPlaceholderWithoutPromptingOrSettingEnv()
    {
        var name = UniqueName();
        var tracker = new DryRunEnvironment();

        using (DryRunEnvironment.Activate(tracker))
        {
            EnvironmentHelper.GetRequiredOrPrompt(name, "Secret", isSecret: true).ShouldBe($"<{name}>");
        }

        tracker.MissingNames.ShouldBe([name]);
        Environment.GetEnvironmentVariable(name).ShouldBeNull();
    }

    [Fact]
    public void GetRequired_OutsideDryRun_StillThrows()
    {
        var name = UniqueName();

        Should.Throw<InvalidOperationException>(() => EnvironmentHelper.GetRequired(name));
        Should.Throw<InvalidOperationException>(() => EnvironmentHelper.GetRequiredOrPrompt(name, "Secret"));
    }

    [Fact]
    public void Activate_ScopeEnds_RestoresPreviousState()
    {
        var name = UniqueName();
        using (DryRunEnvironment.Activate(new DryRunEnvironment()))
        {
        }

        DryRunEnvironment.Current.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => EnvironmentHelper.GetRequired(name));
    }

    [Fact]
    public void FormatWarning_ListsNamesOnceInFirstSeenOrder()
    {
        var tracker = new DryRunEnvironment();
        tracker.FormatWarning().ShouldBeNull();

        tracker.RecordMissing("B_TOKEN");
        tracker.RecordMissing("A_ID");
        tracker.RecordMissing("B_TOKEN");

        tracker.FormatWarning().ShouldBe(
            "Dry run: these required environment variables are not set; placeholders were used: B_TOKEN, A_ID. " +
            "A real run fails without them.");
    }

    [Fact]
    public async Task LoadScriptAsync_DryRunTracker_EnvReturnsPlaceholderAndScriptCompletes()
    {
        var name = UniqueName();
        var dir = Directory.CreateTempSubdirectory("ando-dryenv-").FullName;
        try
        {
            var script = Path.Combine(dir, "build.csando");
            await File.WriteAllTextAsync(script, $$"""
                var value = Env("{{name}}");
                var optional = Env("{{name}}_OPTIONAL", required: false);
                Log.Info($"value={value} optional={optional ?? "null"}");
                """);
            var tracker = new DryRunEnvironment();
            var logger = new TestLogger();
            var host = new ScriptHost(logger)
            {
                ConfigureContext = ctx => ctx.Options.Session.DryRunEnvironment = tracker
            };

            var context = await host.LoadScriptAsync(script, dir);

            // Only required variables are reported; optional ones still return null.
            tracker.MissingNames.ShouldBe([name]);
            context.StepRegistry.Steps.Single().LogMessage.ShouldBe($"value=<{name}> optional=null");
            DryRunEnvironment.Current.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_WithoutTracker_MissingEnvStillThrows()
    {
        var name = UniqueName();
        var dir = Directory.CreateTempSubdirectory("ando-dryenv-").FullName;
        try
        {
            var script = Path.Combine(dir, "build.csando");
            await File.WriteAllTextAsync(script, $"var value = Env(\"{name}\");");
            var host = new ScriptHost(new TestLogger());

            var ex = await Should.ThrowAsync<InvalidOperationException>(() => host.LoadScriptAsync(script, dir));
            ex.Message.ShouldContain(name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

[Trait("Category", "Unit")]
[Collection("SecretValues")] // shares CLOUDFLARE_* env vars with CloudflareCredentialsTests
public class DryRunEnvironmentCloudflareTests : IDisposable
{
    private readonly string? _token = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
    private readonly string? _account = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");

    private readonly string? _project = Environment.GetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME");

    public DryRunEnvironmentCloudflareTests()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", null);
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", null);
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_TOKEN", _token);
        Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", _account);
        Environment.SetEnvironmentVariable("CLOUDFLARE_PROJECT_NAME", _project);
    }

    [Fact]
    public void PagesDeploy_InDryRunWithoutProjectName_UsesPlaceholder()
    {
        var registry = new StepRegistry();
        var cf = new CloudflareOperations(registry, new TestLogger(), () => new MockExecutor());
        var tracker = new DryRunEnvironment();

        using (DryRunEnvironment.Activate(tracker))
        {
            cf.PagesDeploy(new Ando.References.DirectoryRef("./dist"), _ => { });
        }

        tracker.MissingNames.ShouldContain("CLOUDFLARE_PROJECT_NAME");
        registry.Steps.ShouldContain(s => s.Name == "Cloudflare.Pages.Deploy" && s.Context == "<CLOUDFLARE_PROJECT_NAME>");
        Should.Throw<InvalidOperationException>(() =>
            new CloudflareOperations(new StepRegistry(), new TestLogger(), () => new MockExecutor())
                .PagesDeploy(new Ando.References.DirectoryRef("./dist"), _ => { }));
    }

    [Fact]
    public void EnsureAuthenticated_InDryRun_RegistersStepAndRecordsBothNames()
    {
        var registry = new StepRegistry();
        var cf = new CloudflareOperations(registry, new TestLogger(), () => new MockExecutor());
        var tracker = new DryRunEnvironment();

        using (DryRunEnvironment.Activate(tracker))
        {
            cf.EnsureAuthenticated();
            cf.PurgeCache("example.com");
        }

        tracker.MissingNames.ShouldBe(["CLOUDFLARE_API_TOKEN", "CLOUDFLARE_ACCOUNT_ID"]);
        registry.Steps.Select(s => s.Name).ShouldBe(["Cloudflare.EnsureAuthenticated", "Cloudflare.PurgeCache"]);
        Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN").ShouldBeNull();
    }

    [Fact]
    public void EnsureAuthenticated_RealRun_StillFailsWithoutCredentials()
    {
        var cf = new CloudflareOperations(new StepRegistry(), new TestLogger(), () => new MockExecutor());

        Should.Throw<InvalidOperationException>(() => cf.EnsureAuthenticated());
    }
}
