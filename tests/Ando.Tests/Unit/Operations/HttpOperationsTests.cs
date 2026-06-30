// =============================================================================
// HttpOperationsTests.cs
//
// Summary: Unit tests for HttpOperations and HttpHealthCheckOptions.
//
// Tests verify that:
// - WaitForHealthy registers a single in-process step with the URL as context.
// - HttpHealthCheckOptions defaults and fluent setters behave as documented.
// - A poll against an unreachable endpoint with a zero total timeout fails fast
//   (returns false) rather than hanging.
//
// Design: Uses StepRegistry and TestLogger; no network server is started, so the
// reachability test targets a closed local port and bounds the timeout to keep
// the test fast and deterministic.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class HttpOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private HttpOperations CreateHttp() =>
        new HttpOperations(_registry, _logger, () => _executor);

    [Fact]
    public void WaitForHealthy_RegistersStep()
    {
        var http = CreateHttp();

        http.WaitForHealthy("https://app.example.com/healthz");

        Assert.Single(_registry.Steps);
        Assert.Equal("Http.WaitForHealthy", _registry.Steps[0].Name);
        Assert.Equal("https://app.example.com/healthz", _registry.Steps[0].Context);
    }

    [Fact]
    public void HttpHealthCheckOptions_HasDocumentedDefaults()
    {
        var options = new HttpHealthCheckOptions();

        Assert.Equal(200, options.ExpectedStatus);
        Assert.Equal(300, options.TimeoutSeconds);
        Assert.Equal(5, options.IntervalSeconds);
        Assert.Equal(10, options.RequestTimeoutSeconds);
    }

    [Fact]
    public void HttpHealthCheckOptions_FluentSettersOverrideValues()
    {
        var options = new HttpHealthCheckOptions()
            .WithExpectedStatus(204)
            .WithTimeoutSeconds(60)
            .WithIntervalSeconds(3)
            .WithRequestTimeoutSeconds(7);

        Assert.Equal(204, options.ExpectedStatus);
        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(3, options.IntervalSeconds);
        Assert.Equal(7, options.RequestTimeoutSeconds);
    }

    [Fact]
    public async Task WaitForHealthy_UnreachableEndpoint_FailsFast()
    {
        var http = CreateHttp();

        // Port 1 is not listening, so the request is refused quickly; a zero total
        // timeout means the step returns false after the first failed attempt.
        http.WaitForHealthy("http://127.0.0.1:1/healthz", o => o
            .WithTimeoutSeconds(0)
            .WithRequestTimeoutSeconds(1));

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
    }
}
