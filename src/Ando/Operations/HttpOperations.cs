// =============================================================================
// HttpOperations.cs
//
// Summary: HTTP build operations, primarily waiting for an endpoint to become
// healthy.
//
// HttpOperations lets a build script gate on an HTTP health check, for example
// before swapping an App Service slot into production. Unlike command-based
// operations (which shell out via the executor), WaitForHealthy registers an
// in-process step that polls with HttpClient on the build host, so it works
// without a curl binary in the build image and can reach public endpoints.
//
// Architecture:
// - Extends OperationsBase to reuse the step Registry and Logger.
// - Registers an in-process delegate step via Registry.Register, not a command,
//   because polling-with-retry is not a single external command.
//
// Design Decisions:
// - Any non-matching status or connection error is treated as "not healthy yet"
//   and retried until the timeout, since a deploying app returns errors briefly.
// - The step returns false on timeout so the workflow stops before a swap.
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// HTTP operations for build scripts, such as waiting for an endpoint to report
/// healthy. Runs in-process on the build host, so it can poll a public URL (for
/// example an App Service health endpoint) as a deployment gate.
/// </summary>
public class HttpOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    // Services asked for this machine's public IPv4 address, in order.
    internal static readonly string[] PublicIpServices =
        ["https://api.ipify.org", "https://ipv4.icanhazip.com", "https://ifconfig.me/ip"];

    /// <summary>
    /// Registers a step that polls <paramref name="url"/> until it returns the
    /// expected status code (default 200) or the timeout elapses. Use it to gate a
    /// deployment on a health check, for example verifying a release slot's
    /// /healthz/db before swapping it into production.
    /// </summary>
    /// <param name="url">Absolute URL to poll, for example https://app.example.com/healthz.</param>
    /// <param name="configure">Optional configuration: expected status, timeout, interval, consecutive successes, JSON body checks.</param>
    public void WaitForHealthy(string url, Action<HttpHealthCheckOptions>? configure = null) =>
        WaitForHealthy(() => new[] { url }, configure, url);

    /// <summary>
    /// Polls several URLs together: a check passes only when every URL passes.
    /// Usage: Http.WaitForHealthy(new[] { $"{b}/healthz", $"{b}/healthz/db" },
    ///            o => o.ConsecutiveSuccesses(3).MinGapSeconds(11).WithTimeoutSeconds(600));
    /// </summary>
    public void WaitForHealthy(IEnumerable<string> urls, Action<HttpHealthCheckOptions>? configure = null)
    {
        var list = urls.ToArray();
        WaitForHealthy(() => list, configure, string.Join(", ", list));
    }

    /// <summary>
    /// Polls a URL computed at execution time, for example from a step output.
    /// Usage: Http.WaitForHealthy(() => $"https://{host.Value}/healthz");
    /// </summary>
    public void WaitForHealthy(Func<string> url, Action<HttpHealthCheckOptions>? configure = null) =>
        WaitForHealthy(() => new[] { url() }, configure, null);

    /// <summary>
    /// Polls several URLs computed at execution time.
    /// Usage: Http.WaitForHealthy(() => new[] { $"https://{host.Value}/healthz", $"https://{host.Value}/healthz/db" });
    /// </summary>
    public void WaitForHealthy(Func<IEnumerable<string>> urls, Action<HttpHealthCheckOptions>? configure = null) =>
        WaitForHealthy(urls, configure, null);

    private void WaitForHealthy(Func<IEnumerable<string>> getUrls, Action<HttpHealthCheckOptions>? configure, string? context)
    {
        var options = new HttpHealthCheckOptions();
        configure?.Invoke(options);

        // In-process delegate step: poll with HttpClient on the host until every
        // URL passes the required number of times in a row or the timeout ends.
        Registry.Register("Http.WaitForHealthy",
            () =>
            {
                var token = StepCancellation.Current;
                return PollAsync(getUrls().ToArray(), options, Logger, d => Task.Delay(d, token), CreateClient, token);
            },
            context);
    }

    internal static HttpClient CreateClient(TimeSpan timeout) => new() { Timeout = timeout };

    internal static async Task<bool> PollAsync(
        string[] urls,
        HttpHealthCheckOptions options,
        IBuildLogger logger,
        Func<TimeSpan, Task> delay,
        Func<TimeSpan, HttpClient> clientFactory,
        CancellationToken cancellationToken = default)
    {
        if (urls.Length == 0)
        {
            logger.Error("    Http.WaitForHealthy: no URLs to check.");
            return false;
        }

        using var client = clientFactory(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
        var label = string.Join(", ", urls);
        var elapsed = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
        var gap = TimeSpan.FromSeconds(options.MinimumGapSeconds);
        var attempt = 0;
        var streak = 0;
        string? lastProblem = null;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();
            var problem = await CheckAllAsync(client, urls, options, cancellationToken);

            if (problem == null)
            {
                streak++;
                logger.Debug($"    {label}: check {streak}/{options.RequiredConsecutiveSuccesses} passed (attempt {attempt}).");
                if (streak >= options.RequiredConsecutiveSuccesses)
                {
                    logger.Info(
                        $"    {label} healthy after {elapsed.Elapsed.TotalSeconds:0}s " +
                        $"({options.RequiredConsecutiveSuccesses} passing check(s), attempt {attempt}).");
                    return true;
                }
            }
            else
            {
                if (streak > 0)
                {
                    logger.Info($"    Check failed after {streak} passing check(s); starting over: {problem}");
                }
                streak = 0;
                lastProblem = problem;
                logger.Debug($"    {problem} (attempt {attempt}).");
            }

            // After a pass, wait at least the minimum gap before the next counted check.
            var wait = streak > 0 && gap > interval ? gap : interval;

            // Stop once another wait would exceed the timeout.
            if (elapsed.Elapsed + wait > timeout)
            {
                logger.Error(
                    $"    {label} did not become healthy within {options.TimeoutSeconds}s" +
                    (lastProblem != null ? $". Last problem: {lastProblem}" : "."));
                return false;
            }

            await delay(wait);
        }
    }

    // Returns null when every URL passes, otherwise a description of the first problem.
    private static async Task<string?> CheckAllAsync(
        HttpClient client, string[] urls, HttpHealthCheckOptions options, CancellationToken cancellationToken)
    {
        foreach (var url in urls)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if ((int)response.StatusCode != options.ExpectedStatus)
                {
                    return $"{url} returned {(int)response.StatusCode}, expected {options.ExpectedStatus}";
                }

                if (options.ExpectedJson.Count > 0)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var output = new StepOutput("Http.WaitForHealthy");
                    output.Set(body);
                    foreach (var (path, expected) in options.ExpectedJson)
                    {
                        string? actual;
                        try
                        {
                            actual = output.Json(path);
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            return $"{url} did not return JSON";
                        }

                        if (actual != expected)
                        {
                            return $"{url} has {path} = {actual ?? "(missing)"}, expected {expected}";
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A deploying app is unreachable or errors briefly; keep retrying.
                return $"{url} not reachable: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a step that sends a GET request and keeps the response body
    /// (Value) and status code (StatusCode). Fails on a non-2xx status unless
    /// <paramref name="allowAnyStatus"/> is true.
    /// Usage: var health = Http.Get("https://eu.example.com/healthz"); Ando.Report("eu.healthz", health);
    /// </summary>
    public StepOutput Get(string url, bool allowAnyStatus = false) => Get(() => url, allowAnyStatus, url);

    /// <summary>Sends a GET request to a URL computed at execution time.</summary>
    public StepOutput Get(Func<string> url, bool allowAnyStatus = false) => Get(url, allowAnyStatus, null);

    /// <summary>Request timeout for <c>Get</c> (settable for tests).</summary>
    internal TimeSpan GetTimeout { get; set; } = TimeSpan.FromSeconds(30);

    private StepOutput Get(Func<string> getUrl, bool allowAnyStatus, string? context)
    {
        var output = new StepOutput("Http.Get");
        Registry.Register("Http.Get", async () =>
        {
            var url = getUrl();
            var token = StepCancellation.Current;
            using var client = CreateClient(GetTimeout);
            HttpResponseMessage response;
            string body;
            try
            {
                response = await client.GetAsync(url, token);
                body = await response.Content.ReadAsStringAsync(token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // HttpClient timeout, not a user cancellation.
                Logger.Error($"    GET {url} timed out after {GetTimeout.TotalSeconds:0.##}s.");
                return false;
            }
            using var _ = response;
            var status = (int)response.StatusCode;
            output.Set(body, statusCode: status);
            Logger.Info($"    GET {url} -> {status}");

            if (allowAnyStatus || response.IsSuccessStatusCode)
            {
                return true;
            }
            Logger.Error($"    GET {url} returned {status}.");
            return false;
        }, context);
        return output;
    }

    /// <summary>
    /// Registers a step that looks up this machine's public IPv4 address (as seen
    /// by public services). Value is the address, e.g. "203.0.113.7".
    /// Usage: var ip = Http.PublicIpV4();
    /// </summary>
    public StepOutput PublicIpV4()
    {
        var output = new StepOutput("Http.PublicIpV4");
        Registry.Register("Http.PublicIpV4", async () =>
        {
            var ip = await LookUpPublicIpV4Async(Logger);
            if (ip == null)
            {
                return false;
            }
            output.Set(ip);
            Logger.Info($"    Public IPv4: {ip}");
            return true;
        });
        return output;
    }

    /// <summary>Asks public services for this machine's IPv4 address; null when none answered.</summary>
    internal static async Task<string?> LookUpPublicIpV4Async(IBuildLogger logger)
    {
        // Force IPv4 so a dual-stack host doesn't report its IPv6 address.
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Stream,
                    System.Net.Sockets.ProtocolType.Tcp);
                try
                {
                    var addresses = await System.Net.Dns.GetHostAddressesAsync(
                        context.DnsEndPoint.Host, System.Net.Sockets.AddressFamily.InterNetwork, cancellationToken);
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        foreach (var service in PublicIpServices)
        {
            try
            {
                var text = (await client.GetStringAsync(service)).Trim();
                if (System.Net.IPAddress.TryParse(text, out var address) &&
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return text;
                }
            }
            catch (Exception ex)
            {
                logger.Debug($"    {service}: {ex.Message}");
            }
        }

        logger.Error("    Could not determine this machine's public IPv4 address.");
        return null;
    }
}
