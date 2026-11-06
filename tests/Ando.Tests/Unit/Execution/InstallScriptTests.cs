// =============================================================================
// InstallScriptTests.cs
//
// Summary: Tests for the Azure CLI auto-install script and the --ref worktree
// directory name.
//
// Tests verify that:
// - The Azure CLI install script fails on errors, downloads to a file instead
//   of piping into bash, and checks that az exists afterwards.
// - The --ref worktree directory name keeps the commit and a random suffix.
// =============================================================================

using Ando.Cli;
using Ando.Execution;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class InstallScriptTests
{
    [Fact]
    public void AzureCliInstallScript_FailsOnErrorsAndVerifiesInstall()
    {
        var script = ContainerExecutor.AzureCliInstallScript;

        Assert.StartsWith("set -eu;", script);
        Assert.DoesNotContain("| bash", script);
        Assert.Contains("curl -fsSL https://aka.ms/InstallAzureCLIDeb -o /tmp/ando-az-install.sh", script);
        Assert.Contains("bash /tmp/ando-az-install.sh", script);
        Assert.EndsWith("exit 1; }", script);
        Assert.True(script.LastIndexOf("command -v az", StringComparison.Ordinal) >
                    script.IndexOf("bash /tmp/ando-az-install.sh", StringComparison.Ordinal));
    }

    [Fact]
    public void RefWorktreeDirectoryName_KeepsCommitAndRandomSuffix()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";

        var first = RefWorktree.DirectoryName(commit);
        var second = RefWorktree.DirectoryName(commit);

        Assert.StartsWith("ando-ref-0123456789ab-", first);
        Assert.Equal("ando-ref-0123456789ab-".Length + 8, first.Length);
        Assert.NotEqual(first, second);
    }
}
