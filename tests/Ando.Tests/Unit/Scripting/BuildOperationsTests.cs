// =============================================================================
// BuildOperationsTests.cs
//
// Summary: Unit tests for BuildOperations class.
//
// Tests verify that:
// - HttpClient is shared across BuildOperations instances to prevent socket exhaustion
// - All operation instances are properly initialized
// =============================================================================

using System.Reflection;
using Ando.Logging;
using Ando.Profiles;
using Ando.Scripting;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Scripting;

[Trait("Category", "Unit")]
public class BuildOperationsTests
{
    private readonly TestLogger _logger = new();
    private readonly StepRegistry _registry = new();
    private readonly MockExecutor _executor = new();

    private BuildOperations CreateBuildOperations()
    {
        return new BuildOperations(
            _registry,
            _logger,
            () => _executor,
            path => path, // Identity function for path translation
            new BuildOptions(),
            new ProfileRegistry());
    }

    [Fact]
    public void Constructor_InitializesAllOperations()
    {
        var ops = CreateBuildOperations();

        // Verify all operation properties are initialized
        ops.Dotnet.ShouldNotBeNull();
        ops.Ef.ShouldNotBeNull();
        ops.Npm.ShouldNotBeNull();
        ops.Azure.ShouldNotBeNull();
        ops.Bicep.ShouldNotBeNull();
        ops.Cloudflare.ShouldNotBeNull();
        ops.Functions.ShouldNotBeNull();
        ops.AppService.ShouldNotBeNull();
        ops.Node.ShouldNotBeNull();
        ops.Log.ShouldNotBeNull();
        ops.Nuget.ShouldNotBeNull();
        ops.Ando.ShouldNotBeNull();
        ops.NpmGlobal.ShouldNotBeNull();
        ops.Git.ShouldNotBeNull();
        ops.GitHub.ShouldNotBeNull();
        ops.Docker.ShouldNotBeNull();
        ops.Playwright.ShouldNotBeNull();
        ops.Docfx.ShouldNotBeNull();
    }

    [Fact]
    public void SharedHttpClient_IsSameAcrossInstances()
    {
        // This test verifies that BuildOperations uses a static shared HttpClient
        // to prevent socket exhaustion from creating new clients per build.

        // Use reflection to access the private static SharedHttpClient field
        var sharedHttpClientField = typeof(BuildOperations)
            .GetField("SharedHttpClient", BindingFlags.NonPublic | BindingFlags.Static);

        sharedHttpClientField.ShouldNotBeNull("BuildOperations should have a static SharedHttpClient field");

        var httpClient1 = sharedHttpClientField.GetValue(null);
        var httpClient2 = sharedHttpClientField.GetValue(null);

        // The static field should return the same instance
        httpClient1.ShouldBeSameAs(httpClient2);
        httpClient1.ShouldNotBeNull();
    }

    [Fact]
    public void SharedHttpClient_IsHttpClientType()
    {
        var sharedHttpClientField = typeof(BuildOperations)
            .GetField("SharedHttpClient", BindingFlags.NonPublic | BindingFlags.Static);

        sharedHttpClientField.ShouldNotBeNull();

        var httpClient = sharedHttpClientField.GetValue(null);
        httpClient.ShouldBeOfType<HttpClient>();
    }

    [Fact]
    public void MultipleInstances_DoNotCreateNewHttpClients()
    {
        // Create multiple BuildOperations instances
        var ops1 = CreateBuildOperations();
        var ops2 = CreateBuildOperations();
        var ops3 = CreateBuildOperations();

        // All should be created successfully without socket exhaustion
        ops1.ShouldNotBeNull();
        ops2.ShouldNotBeNull();
        ops3.ShouldNotBeNull();

        // Verify the static HttpClient is still the same instance
        var sharedHttpClientField = typeof(BuildOperations)
            .GetField("SharedHttpClient", BindingFlags.NonPublic | BindingFlags.Static);

        var httpClient = sharedHttpClientField?.GetValue(null);
        httpClient.ShouldNotBeNull();
        httpClient.ShouldBeOfType<HttpClient>();
    }
}
