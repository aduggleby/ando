// =============================================================================
// HttpExpectationsTests.cs
//
// Summary: Tests for response expectations (content type prefix, body contains,
// body regex) on Http.Get and Http.WaitForHealthy, against a local HttpListener.
// =============================================================================

using System.Net;
using System.Net.Sockets;
using System.Text;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public sealed class HttpExpectationsTests : IDisposable
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly HttpListener _listener = new();
    private readonly string _baseUrl;
    private string _contentType = "text/html; charset=utf-8";
    private string _body = "<html><title>Hello</title></html>";

    public HttpExpectationsTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _baseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                var bytes = Encoding.UTF8.GetBytes(_body);
                context.Response.ContentType = _contentType;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
    }

    public void Dispose() => _listener.Close();

    private HttpOperations CreateHttp() => new(_registry, _logger, () => new MockExecutor());

    private async Task<bool> RunGetAsync(Action<HttpGetOptions> configure)
    {
        CreateHttp().Get(_baseUrl, configure);
        return await _registry.Steps[^1].Execute();
    }

    [Fact]
    public async Task Get_ContentTypePrefixAndBodyContains_Pass()
    {
        (await RunGetAsync(o => o.ExpectContentType("text/html").ExpectBodyContains("<title>Hello</title>")
            .ExpectBodyMatches(@"<title>H\w+o</title>"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Get_WrongContentType_FailsAndShowsReceived()
    {
        _contentType = "application/json";
        _body = """{"ok":true}""";

        (await RunGetAsync(o => o.ExpectContentType("text/html"))).ShouldBeFalse();

        var error = _logger.ErrorMessages.ShouldHaveSingleItem();
        error.ShouldContain("application/json");
        error.ShouldContain("text/html");
        error.ShouldContain("""{"ok":true}""");
    }

    [Fact]
    public async Task Get_BodyMissingText_Fails()
    {
        (await RunGetAsync(o => o.ExpectBodyContains("Goodbye"))).ShouldBeFalse();

        _logger.ErrorMessages.ShouldHaveSingleItem().ShouldContain("does not contain \"Goodbye\"");
    }

    [Fact]
    public async Task Get_RegexMismatch_FailsAndTruncatesLongBody()
    {
        _body = new string('x', 500);

        (await RunGetAsync(o => o.ExpectBodyMatches("^y+$"))).ShouldBeFalse();

        var error = _logger.ErrorMessages.ShouldHaveSingleItem();
        error.ShouldContain("(+300 more characters)");
        error.ShouldNotContain(new string('x', 201));
    }

    [Fact]
    public async Task Get_Mismatch_StillExposesBodyAsValue()
    {
        var output = CreateHttp().Get(_baseUrl, o => o.ExpectBodyContains("nope"));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
        output.Value.ShouldBe(_body);
    }

    [Fact]
    public async Task Get_WithoutExpectations_BehavesAsBefore()
    {
        CreateHttp().Get(_baseUrl);

        (await _registry.Steps[0].Execute()).ShouldBeTrue();
    }

    [Fact]
    public void InvalidRegex_ThrowsWhenConfigured()
    {
        Should.Throw<ArgumentException>(() => new HttpGetOptions().ExpectBodyMatches("(unclosed"));
        Should.Throw<ArgumentException>(() => new HttpHealthCheckOptions().ExpectBodyMatches("(unclosed"));
    }

    [Fact]
    public async Task WaitForHealthy_ExpectationsPass()
    {
        CreateHttp().WaitForHealthy(_baseUrl, o => o.WithTimeoutSeconds(5).ExpectContentType("TEXT/HTML").ExpectBodyContains("Hello"));

        (await _registry.Steps[0].Execute()).ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForHealthy_ContentTypeMismatch_FailsWithReceivedInLastProblem()
    {
        _contentType = "text/plain";
        _body = "maintenance";

        CreateHttp().WaitForHealthy(_baseUrl, o => o.WithTimeoutSeconds(0).ExpectContentType("text/html"));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
        var error = _logger.ErrorMessages.ShouldHaveSingleItem();
        error.ShouldContain("text/plain");
        error.ShouldContain("maintenance");
    }

    [Fact]
    public async Task WaitForHealthy_BodyRegexMismatch_Fails()
    {
        CreateHttp().WaitForHealthy(_baseUrl, o => o.WithTimeoutSeconds(0).ExpectBodyMatches("^ready$"));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
        _logger.ErrorMessages.ShouldHaveSingleItem().ShouldContain("does not match /^ready$/");
    }
}
