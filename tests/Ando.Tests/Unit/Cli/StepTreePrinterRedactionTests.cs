// =============================================================================
// StepTreePrinterRedactionTests.cs
//
// Summary: `--dry-run` output must not print registered secret values.
// =============================================================================

using Ando.Cli;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
[Collection("SecretValues")]
public class StepTreePrinterRedactionTests : IDisposable
{
    public StepTreePrinterRedactionTests() => SecretValues.Clear();

    public void Dispose() => SecretValues.Clear();

    [Fact]
    public void Render_RedactsSecretsInStepContext()
    {
        var registry = new StepRegistry();
        registry.Register("Shell.Bash", () => Task.FromResult(true), "login --password hunter2-secret-value");
        SecretValues.Add("hunter2-secret-value");

        var lines = StepTreePrinter.Render(registry.Root);

        lines.ShouldBe(["Shell.Bash (login --password [REDACTED])"]);
    }
}
