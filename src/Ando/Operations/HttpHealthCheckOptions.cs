// =============================================================================
// HttpHealthCheckOptions.cs
//
// Summary: Fluent options for Http.WaitForHealthy.
//
// Configures how Http.WaitForHealthy polls an endpoint: the status code that
// marks it healthy, how long to keep polling, the delay between polls, and the
// per-request timeout. It fits the architecture as a plain options object built
// by the script and consumed by HttpOperations when the step runs.
//
// Design Decisions:
// - Values are plain seconds (ints) to keep build scripts simple and readable.
// - Mutating setters return "this" for fluent chaining, matching other options
//   types such as AppServiceDeployOptions.
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Options for <see cref="HttpOperations.WaitForHealthy"/>: expected status code,
/// total timeout, poll interval, and per-request timeout. Defaults: status 200,
/// 300s total timeout, 5s interval, 10s per-request timeout.
/// </summary>
public class HttpHealthCheckOptions
{
    /// <summary>Expected HTTP status code that marks the endpoint healthy. Default 200.</summary>
    public int ExpectedStatus { get; private set; } = 200;

    /// <summary>Total time to keep polling before failing, in seconds. Default 300.</summary>
    public int TimeoutSeconds { get; private set; } = 300;

    /// <summary>Delay between polls, in seconds. Default 5.</summary>
    public int IntervalSeconds { get; private set; } = 5;

    /// <summary>Per-request timeout, in seconds. Default 10.</summary>
    public int RequestTimeoutSeconds { get; private set; } = 10;

    /// <summary>
    /// Sets the HTTP status code that marks the endpoint healthy.
    /// </summary>
    /// <param name="status">Expected status code, for example 200.</param>
    /// <returns>This options instance for chaining.</returns>
    public HttpHealthCheckOptions WithExpectedStatus(int status)
    {
        ExpectedStatus = status;
        return this;
    }

    /// <summary>
    /// Sets the total time to keep polling before the step fails.
    /// </summary>
    /// <param name="seconds">Total timeout in seconds.</param>
    /// <returns>This options instance for chaining.</returns>
    public HttpHealthCheckOptions WithTimeoutSeconds(int seconds)
    {
        TimeoutSeconds = seconds;
        return this;
    }

    /// <summary>
    /// Sets the delay between polls.
    /// </summary>
    /// <param name="seconds">Interval in seconds.</param>
    /// <returns>This options instance for chaining.</returns>
    public HttpHealthCheckOptions WithIntervalSeconds(int seconds)
    {
        IntervalSeconds = seconds;
        return this;
    }

    /// <summary>
    /// Sets the per-request timeout for each poll.
    /// </summary>
    /// <param name="seconds">Per-request timeout in seconds.</param>
    /// <returns>This options instance for chaining.</returns>
    public HttpHealthCheckOptions WithRequestTimeoutSeconds(int seconds)
    {
        RequestTimeoutSeconds = seconds;
        return this;
    }
}
