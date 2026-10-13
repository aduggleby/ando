// =============================================================================
// ProbeEndpointTests.cs
//
// Summary: HTTP tests for the anonymous /live and /ready probes and for the
// optional rolling file log, using the in-memory AndoWebApplicationFactory.
//
// Design Decisions:
// - The in-memory database always "connects", so /ready returns 200 here; the
//   503 path is unit-tested in ReadyEndpointTests
// - The file log test writes its own warning (Testing filters most framework
//   logs) and disposes the factory to flush the sink before reading the file
// =============================================================================

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Tests for /live, /ready and file logging.
/// </summary>
public class ProbeEndpointTests
{
    [Fact]
    public async Task Live_Returns200WithStatusLive()
    {
        using var factory = new AndoWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().ShouldBe("live");
    }

    [Fact]
    public async Task Ready_DatabaseReachable_Returns200WithProvider()
    {
        using var factory = new AndoWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetString().ShouldBe("ready");
        body.RootElement.GetProperty("database").GetString().ShouldBe("SqlServer");
    }

    [Fact]
    public async Task FileLogging_Enabled_WritesDailyFile()
    {
        var directory = Directory.CreateTempSubdirectory("ando-filelog-");
        try
        {
            var factory = new AndoWebApplicationFactory();
            factory.Settings["Logging:File:Enabled"] = "true";
            factory.Settings["Logging:File:Path"] = directory.FullName;
            var client = factory.CreateClient();
            (await client.GetAsync("/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
            factory.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("FileLoggingTest")
                .LogWarning("file log marker {Marker}", "ando-file-log-test");
            factory.Dispose();

            var files = directory.GetFiles("ando-*.log");
            files.Length.ShouldBe(1);
            Regex.IsMatch(files[0].Name, @"^ando-\d{8}\.log$").ShouldBeTrue(files[0].Name);
            var content = await File.ReadAllTextAsync(files[0].FullName);
            content.ShouldContain("file log marker ando-file-log-test");
            content.ShouldContain("[WRN] FileLoggingTest");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FileLogging_DirectoryNotWritable_StartsWithoutFileSink()
    {
        var file = Path.GetTempFileName();
        try
        {
            using var factory = new AndoWebApplicationFactory();
            factory.Settings["Logging:File:Enabled"] = "true";
            factory.Settings["Logging:File:Path"] = Path.Combine(file, "logs"); // parent is a file
            var client = factory.CreateClient();

            (await client.GetAsync("/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
