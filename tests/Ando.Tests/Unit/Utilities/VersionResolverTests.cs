// =============================================================================
// VersionResolverTests.cs
//
// Summary: Unit tests for VersionResolver class.
//
// Tests verify that:
// - API responses are correctly parsed for .NET, Node.js, and npm versions
// - Results are cached to avoid repeated API calls
// - Fallback versions are returned when API calls fail
// =============================================================================

using System.Net;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class VersionResolverTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public async Task GetLatestDotnetSdkVersionAsync_ParsesApiResponse()
    {
        // Arrange - mock response with .NET 10.0 as latest active release
        var handler = new MockHttpMessageHandler("""
        {
            "releases-index": [
                { "channel-version": "10.0", "support-phase": "active" },
                { "channel-version": "9.0", "support-phase": "active" },
                { "channel-version": "8.0", "support-phase": "maintenance" }
            ]
        }
        """);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        var version = await resolver.GetLatestDotnetSdkVersionAsync();

        // Assert
        version.ShouldBe("10.0");
    }

    [Fact]
    public async Task GetLatestDotnetSdkVersionAsync_CachesResult()
    {
        // Arrange
        var handler = new MockHttpMessageHandler("""
        {
            "releases-index": [
                { "channel-version": "10.0", "support-phase": "active" }
            ]
        }
        """);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act - call twice
        var version1 = await resolver.GetLatestDotnetSdkVersionAsync();
        var version2 = await resolver.GetLatestDotnetSdkVersionAsync();

        // Assert - should only make one HTTP call
        version1.ShouldBe("10.0");
        version2.ShouldBe("10.0");
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetLatestDotnetSdkVersionAsync_ReturnsFallbackOnError()
    {
        // Arrange - handler that throws
        var handler = new MockHttpMessageHandler(throwError: true);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        var version = await resolver.GetLatestDotnetSdkVersionAsync();

        // Assert - should return fallback
        version.ShouldBe("9.0");
        _logger.InfoMessages.ShouldContain(m => m.Contains("fallback"));
    }

    [Fact]
    public async Task GetLatestNodeLtsVersionAsync_FindsLtsVersion()
    {
        // Arrange - mock Node.js releases
        var handler = new MockHttpMessageHandler("""
        [
            { "version": "v23.0.0", "lts": false },
            { "version": "v22.12.0", "lts": "Jod" },
            { "version": "v20.18.0", "lts": "Iron" }
        ]
        """);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        var version = await resolver.GetLatestNodeLtsVersionAsync();

        // Assert - should find first LTS (v22)
        version.ShouldBe("22");
    }

    [Fact]
    public async Task GetLatestNodeLtsVersionAsync_CachesResult()
    {
        // Arrange
        var handler = new MockHttpMessageHandler("""
        [
            { "version": "v22.12.0", "lts": "Jod" }
        ]
        """);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        await resolver.GetLatestNodeLtsVersionAsync();
        await resolver.GetLatestNodeLtsVersionAsync();

        // Assert - single call
        handler.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetLatestNodeLtsVersionAsync_ReturnsFallbackOnError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(throwError: true);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        var version = await resolver.GetLatestNodeLtsVersionAsync();

        // Assert
        version.ShouldBe("22");
    }

    [Fact]
    public async Task GetLatestNpmVersionAsync_ParsesDistTags()
    {
        // Arrange
        var handler = new MockHttpMessageHandler("""
        {
            "dist-tags": {
                "latest": "10.8.3",
                "next": "11.0.0-beta.1"
            }
        }
        """);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        var version = await resolver.GetLatestNpmVersionAsync();

        // Assert
        version.ShouldBe("10.8.3");
    }

    [Fact]
    public async Task GetLatestNpmVersionAsync_ReturnsFallbackOnError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(throwError: true);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act
        var version = await resolver.GetLatestNpmVersionAsync();

        // Assert
        version.ShouldBe("latest");
    }

    [Fact]
    public async Task ClearCache_AllowsNewFetch()
    {
        // Arrange
        var handler = new MockHttpMessageHandler("""
        {
            "releases-index": [
                { "channel-version": "10.0", "support-phase": "active" }
            ]
        }
        """);
        var httpClient = new HttpClient(handler);
        var resolver = new VersionResolver(httpClient, _logger);

        // Act - fetch, clear, fetch again
        await resolver.GetLatestDotnetSdkVersionAsync();
        resolver.ClearCache();
        await resolver.GetLatestDotnetSdkVersionAsync();

        // Assert - should make two calls
        handler.CallCount.ShouldBe(2);
    }

    /// <summary>
    /// Mock HTTP handler for testing HTTP calls without making real network requests.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _response;
        private readonly bool _throwError;

        public int CallCount { get; private set; }

        public MockHttpMessageHandler(string? response = null, bool throwError = false)
        {
            _response = response;
            _throwError = throwError;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;

            if (_throwError)
            {
                throw new HttpRequestException("Network error");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response ?? "")
            });
        }
    }
}
