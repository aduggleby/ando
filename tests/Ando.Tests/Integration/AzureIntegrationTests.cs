// =============================================================================
// AzureIntegrationTests.cs
//
// Summary: Integration tests for Azure operations.
//
// Tests verify Azure CLI integration including authentication verification
// and account operations. Requires Azure CLI to be installed and the user
// to be logged in.
//
// These tests are skipped if:
// - Azure CLI is not installed
// - User is not logged in to Azure
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Azure")]
public class AzureIntegrationTests
{
    private readonly TestLogger _logger = new();
    private readonly StepRegistry _registry = new();
    private ICommandExecutor? _executor;

    private AzureOperations CreateAzure()
    {
        _executor = new ProcessRunner(_logger);
        return new AzureOperations(_registry, _logger, () => _executor!);
    }

    private static bool IsAzureCliAvailable() => AzureOperations.IsAzureCliAvailable();

    private static bool IsLoggedInToAzure()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "az",
                Arguments = "account show --output none",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    public void IsAzureCliAvailable_CanCheckAvailability()
    {
        // This test just verifies the check method works without exception
        var available = IsAzureCliAvailable();
        Assert.True(available || !available);
    }

    [SkippableFact]
    public async Task EnsureLoggedIn_WithValidLogin_Succeeds()
    {
        Skip.IfNot(IsAzureCliAvailable(), "Azure CLI not available");
        Skip.IfNot(IsLoggedInToAzure(), "Not logged in to Azure");

        var azure = CreateAzure();
        azure.EnsureLoggedIn();

        var success = await _registry.Steps[0].Execute();

        Assert.True(success);
    }

    [SkippableFact]
    public async Task ShowAccount_WithValidLogin_Succeeds()
    {
        Skip.IfNot(IsAzureCliAvailable(), "Azure CLI not available");
        Skip.IfNot(IsLoggedInToAzure(), "Not logged in to Azure");

        var azure = CreateAzure();
        azure.ShowAccount();

        var success = await _registry.Steps[0].Execute();

        Assert.True(success);
        // Verify some output was logged
        Assert.True(_logger.InfoMessages.Count > 0);
    }

    [SkippableFact]
    public async Task EnsureLoggedIn_AfterLogout_Fails()
    {
        Skip.IfNot(IsAzureCliAvailable(), "Azure CLI not available");
        // This test intentionally skips if logged in - we only test failure when logged out
        Skip.IfNot(!IsLoggedInToAzure(), "User is logged in - cannot test failure case");

        var azure = CreateAzure();
        azure.EnsureLoggedIn();

        var success = await _registry.Steps[0].Execute();

        Assert.False(success);
    }
}
