// =============================================================================
// NodeSdkEnsurerTests.cs
//
// Summary: Unit tests for NodeSdkEnsurer class.
//
// Tests verify that:
// - Node.js is installed when not present
// - Node.js installation is skipped when already installed (warm container)
// - Node.js installation only happens once per session
// - Manual install flag disables auto-install
// =============================================================================

using System.Net;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class NodeSdkEnsurerTests
{
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private VersionResolver CreateVersionResolver(string nodeVersion = "22")
    {
        var handler = new MockHttpMessageHandler($$"""
        [
            { "version": "v{{nodeVersion}}.12.0", "lts": "Jod" }
        ]
        """);
        return new VersionResolver(new HttpClient(handler), _logger);
    }

    [Fact]
    public async Task EnsureInstalledAsync_SkipsWhenAlreadyInstalled()
    {
        // Arrange - simulate node already installed
        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);

        // Act
        await ensurer.EnsureInstalledAsync();

        // Assert - should only run the check command
        _executor.ExecutedCommands.Count.ShouldBe(1);
        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("bash");
        cmd.Args.ShouldContain(a => a.Contains("command -v node"));
        _logger.DebugMessages.ShouldContain(m => m.Contains("already installed"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_InstallsWhenNotPresent()
    {
        // Arrange - simulate node not installed (check fails)
        _executor.ConditionalFailures["bash"] = args =>
            args.Any(a => a.Contains("command -v node"));

        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);

        // Act
        await ensurer.EnsureInstalledAsync();

        // Assert - should run check and then install
        _executor.ExecutedCommands.Count.ShouldBe(2);
        _logger.InfoMessages.ShouldContain(m => m.Contains("Installing Node.js"));
    }

    [Fact]
    public async Task EnsureInstalledAsync_OnlyInstallsOnce()
    {
        // Arrange
        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);

        // Act - call three times
        await ensurer.EnsureInstalledAsync();
        await ensurer.EnsureInstalledAsync();
        await ensurer.EnsureInstalledAsync();

        // Assert - should only execute once
        _executor.ExecutedCommands.Count.ShouldBe(1);
    }

    [Fact]
    public async Task EnsureInstalledAsync_SkipsWhenManualInstallCalled()
    {
        // Arrange
        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);
        ensurer.MarkManualInstallCalled();

        // Act
        await ensurer.EnsureInstalledAsync();

        // Assert - should not execute anything
        _executor.ExecutedCommands.ShouldBeEmpty();
    }

    [Fact]
    public void MarkManualInstallCalled_SetsFlag()
    {
        // Arrange
        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);

        // Act
        ensurer.MarkManualInstallCalled();

        // Assert
        ensurer.ManualInstallCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureInstalledAsync_SetsIsInstalledFlag()
    {
        // Arrange
        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);

        // Act
        await ensurer.EnsureInstalledAsync();

        // Assert
        ensurer.IsInstalled.ShouldBeTrue();
    }

    [Fact]
    public void Reset_ClearsState()
    {
        // Arrange
        var resolver = CreateVersionResolver();
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);
        ensurer.MarkManualInstallCalled();

        // Act
        ensurer.Reset();

        // Assert
        ensurer.IsInstalled.ShouldBeFalse();
        ensurer.ManualInstallCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task EnsureInstalledAsync_UsesVersionFromResolver()
    {
        // Arrange - use version 20
        _executor.ConditionalFailures["bash"] = args =>
            args.Any(a => a.Contains("command -v node"));

        var resolver = CreateVersionResolver("20");
        var ensurer = new NodeSdkEnsurer(resolver, () => _executor, _logger);

        // Act
        await ensurer.EnsureInstalledAsync();

        // Assert - install command should use version 20
        var installCmd = _executor.ExecutedCommands[1];
        installCmd.Args.ShouldContain(a => a.Contains("setup_20.x"));
    }

    /// <summary>
    /// Mock HTTP handler for testing.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;

        public MockHttpMessageHandler(string response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            });
        }
    }
}
