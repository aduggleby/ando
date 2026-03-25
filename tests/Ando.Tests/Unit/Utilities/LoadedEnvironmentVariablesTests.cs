// =============================================================================
// LoadedEnvironmentVariablesTests.cs
//
// Summary: Unit tests for tracking env-file variables across command execution.
//
// These tests verify that env var names can be tracked independently from how
// their values entered the current process. This supports workflows where a
// caller preloads values before invoking ANDO, while ANDO still needs to
// forward those values into containerized subprocesses.
// =============================================================================

using Ando.Utilities;

namespace Ando.Tests.Unit.Utilities;

[Collection("Environment Variables")]
[Trait("Category", "Unit")]
public class LoadedEnvironmentVariablesTests : IDisposable
{
    private readonly string? _originalTrackedKeys = Environment.GetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar);
    private readonly string? _originalOpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar, _originalTrackedKeys);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", _originalOpenAiApiKey);
    }

    [Fact]
    public void Track_WithPreloadedEnvironmentVariable_TracksKeyAndReadsCurrentValue()
    {
        Environment.SetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar, null);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "from-parent-process");

        LoadedEnvironmentVariables.Track(["OPENAI_API_KEY"]);

        LoadedEnvironmentVariables.GetTrackedKeys().ShouldContain("OPENAI_API_KEY");
        LoadedEnvironmentVariables.GetTrackedVariables()["OPENAI_API_KEY"].ShouldBe("from-parent-process");
    }
}
