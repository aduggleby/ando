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
    /// <summary>
    /// Registers a step that polls <paramref name="url"/> until it returns the
    /// expected status code (default 200) or the timeout elapses. Use it to gate a
    /// deployment on a health check, for example verifying a release slot's
    /// /healthz/db before swapping it into production.
    /// </summary>
    /// <param name="url">Absolute URL to poll, for example https://app.example.com/healthz.</param>
    /// <param name="configure">Optional configuration: expected status, timeout, interval.</param>
    public void WaitForHealthy(string url, Action<HttpHealthCheckOptions>? configure = null)
    {
        var options = new HttpHealthCheckOptions();
        configure?.Invoke(options);

        // In-process delegate step: poll with HttpClient on the host until the
        // endpoint returns the expected status or the total timeout is reached.
        Registry.Register("Http.WaitForHealthy", async () =>
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            };

            var elapsed = Stopwatch.StartNew();
            var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
            var attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    var response = await client.GetAsync(url);
                    if ((int)response.StatusCode == options.ExpectedStatus)
                    {
                        Logger.Info(
                            $"    {url} returned {options.ExpectedStatus} after {elapsed.Elapsed.TotalSeconds:0}s (attempt {attempt}).");
                        return true;
                    }

                    Logger.Debug(
                        $"    {url} returned {(int)response.StatusCode}, expected {options.ExpectedStatus} (attempt {attempt}).");
                }
                catch (Exception ex)
                {
                    // A deploying app is unreachable or errors briefly; keep retrying.
                    Logger.Debug($"    {url} not reachable yet: {ex.Message} (attempt {attempt}).");
                }

                // Stop once another full interval would exceed the timeout.
                if (elapsed.Elapsed + interval > timeout)
                {
                    Logger.Error(
                        $"    {url} did not return {options.ExpectedStatus} within {options.TimeoutSeconds}s.");
                    return false;
                }

                await Task.Delay(interval);
            }
        }, url);
    }
}
