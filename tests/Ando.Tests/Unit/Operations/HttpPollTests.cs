// =============================================================================
// HttpPollTests.cs
//
// Summary: Tests for HttpOperations.PollAsync (multi-URL checks, consecutive
// successes, minimum gap, JSON body checks, timeout) using a fake
// HttpMessageHandler and a recording delay function, so no network or real
// waiting is involved.
// =============================================================================

using System.Net;
using Ando.Operations;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class HttpPollTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request, Requests.Count - 1));
        }
    }

    private readonly TestLogger _logger = new();
    private readonly List<TimeSpan> _delays = [];

    private static HttpResponseMessage Response(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body) };

    private Task<bool> PollAsync(FakeHandler handler, string[] urls, Action<HttpHealthCheckOptions>? configure = null)
    {
        var options = new HttpHealthCheckOptions();
        configure?.Invoke(options);
        return HttpOperations.PollAsync(urls, options, _logger,
            delay => { _delays.Add(delay); return Task.CompletedTask; },
            _ => new HttpClient(handler));
    }

    [Fact]
    public async Task SeveralUrls_AllMustPass_AndAllAreRequested()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK));

        var passed = await PollAsync(handler, ["https://a/healthz", "https://b/healthz", "https://c/healthz/db"]);

        passed.ShouldBeTrue();
        handler.Requests.ShouldBe(["https://a/healthz", "https://b/healthz", "https://c/healthz/db"]);
        _delays.ShouldBeEmpty();
    }

    [Fact]
    public async Task SeveralUrls_OneFailing_NeverPasses()
    {
        var handler = new FakeHandler((request, _) =>
            Response(request.RequestUri!.Host == "b" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        var passed = await PollAsync(handler, ["https://a/x", "https://b/x"], o => o.WithTimeoutSeconds(1));

        passed.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("https://b/x returned 503, expected 200"));
    }

    [Fact]
    public async Task NoUrls_Fails()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK));

        (await PollAsync(handler, [])).ShouldBeFalse();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExpectedStatus_IsHonored()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.NoContent));

        (await PollAsync(handler, ["https://a/x"], o => o.WithExpectedStatus(204))).ShouldBeTrue();
    }

    [Fact]
    public async Task ConsecutiveSuccesses_RequiresRunOfPassingChecks()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.ConsecutiveSuccesses(3));

        passed.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(3);
        _delays.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ConsecutiveSuccesses_ResetsOnFailure()
    {
        var statuses = new[]
        {
            HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.InternalServerError,
            HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK
        };
        var handler = new FakeHandler((_, i) => Response(statuses[i]));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.ConsecutiveSuccesses(3).WithTimeoutSeconds(600));

        passed.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(6); // two passes, a failure, then three fresh passes
        _logger.InfoMessages.ShouldContain(m => m.Contains("starting over"));
    }

    [Fact]
    public async Task ConsecutiveSuccesses_WithSeveralUrls_CountsWholeRounds()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK));

        await PollAsync(handler, ["https://a/x", "https://b/x"], o => o.ConsecutiveSuccesses(2));

        handler.Requests.ShouldBe(["https://a/x", "https://b/x", "https://a/x", "https://b/x"]);
    }

    [Fact]
    public async Task MinGapSeconds_IsTheWaitAfterAPass_AndIntervalAfterAFailure()
    {
        var statuses = new[] { HttpStatusCode.OK, HttpStatusCode.InternalServerError, HttpStatusCode.OK, HttpStatusCode.OK };
        var handler = new FakeHandler((_, i) => Response(statuses[i]));

        var passed = await PollAsync(handler, ["https://a/x"],
            o => o.ConsecutiveSuccesses(2).MinGapSeconds(11).WithIntervalSeconds(5).WithTimeoutSeconds(600));

        passed.ShouldBeTrue();
        _delays.ShouldBe([TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(11)]);
    }

    [Fact]
    public async Task MinGapSeconds_SmallerThanInterval_UsesInterval()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK));

        await PollAsync(handler, ["https://a/x"],
            o => o.ConsecutiveSuccesses(2).MinGapSeconds(2).WithIntervalSeconds(5));

        _delays.ShouldBe([TimeSpan.FromSeconds(5)]);
    }

    [Fact]
    public async Task ExpectJson_Match_Passes()
    {
        var handler = new FakeHandler((_, _) =>
            Response(HttpStatusCode.OK, """{"version":"1.2.3","build":{"commit":"abc","ok":true}}"""));

        var passed = await PollAsync(handler, ["https://a/x"],
            o => o.ExpectJson("version", "1.2.3").ExpectJson("build.commit", "abc").ExpectJson("build.ok", "true"));

        passed.ShouldBeTrue();
    }

    [Fact]
    public async Task ExpectJson_Mismatch_FailsWithDescription()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK, """{"version":"1.0.0"}"""));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.ExpectJson("version", "2.0.0").WithTimeoutSeconds(1));

        passed.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("version = 1.0.0, expected 2.0.0"));
    }

    [Fact]
    public async Task ExpectJson_MissingPath_Fails()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK, """{"other":1}"""));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.ExpectJson("version", "1").WithTimeoutSeconds(1));

        passed.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("(missing)"));
    }

    [Fact]
    public async Task ExpectJson_NonJsonBody_Fails()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.OK, "<html>hello</html>"));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.ExpectJson("version", "1").WithTimeoutSeconds(1));

        passed.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("did not return JSON"));
    }

    [Fact]
    public async Task ExpectJson_AppearsAfterRetry_Passes()
    {
        var handler = new FakeHandler((_, i) =>
            Response(HttpStatusCode.OK, i == 0 ? """{"version":"old"}""" : """{"version":"new"}"""));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.ExpectJson("version", "new").WithTimeoutSeconds(600));

        passed.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Timeout_ReturnsFalse_AndReportsLastProblem()
    {
        var handler = new FakeHandler((_, _) => Response(HttpStatusCode.NotFound));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.WithTimeoutSeconds(1));

        passed.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("did not become healthy within 1s") && m.Contains("404"));
    }

    [Fact]
    public async Task ConnectionErrors_AreRetriedUntilHealthy()
    {
        var handler = new FakeHandler((_, i) => i < 2
            ? throw new HttpRequestException("connection refused")
            : Response(HttpStatusCode.OK));

        var passed = await PollAsync(handler, ["https://a/x"], o => o.WithTimeoutSeconds(600));

        passed.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public void WaitForHealthy_Overloads_RegisterOneStep()
    {
        var registry = new Ando.Steps.StepRegistry();
        var http = new HttpOperations(registry, _logger, () => new MockExecutor());

        http.WaitForHealthy(["https://a/x", "https://b/x"]);
        http.WaitForHealthy(() => "https://c/x");

        registry.Steps.Count.ShouldBe(2);
        registry.Steps[0].Context.ShouldBe("https://a/x, https://b/x");
        registry.Steps[1].Context.ShouldBeNull();
    }

    [Fact]
    public void HttpHealthCheckOptions_ConsecutiveSuccesses_RejectsZero()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new HttpHealthCheckOptions().ConsecutiveSuccesses(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new HttpHealthCheckOptions().MinGapSeconds(-1));
    }
}
