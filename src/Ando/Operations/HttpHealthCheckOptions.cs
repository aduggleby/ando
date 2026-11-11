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

    /// <summary>Successful checks in a row needed to pass. Default 1.</summary>
    public int RequiredConsecutiveSuccesses { get; private set; } = 1;

    /// <summary>Minimum seconds between two counted successful checks. Default 0.</summary>
    public int MinimumGapSeconds { get; private set; }

    /// <summary>JSON values the response body must contain (dotted path -> expected string).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExpectedJson => _expectedJson;
    private readonly List<KeyValuePair<string, string>> _expectedJson = [];

    internal HttpResponseExpectations Expectations { get; } = new();

    /// <summary>
    /// Requires the Content-Type header to start with <paramref name="prefix"/> (case-insensitive),
    /// e.g. "text/html". A mismatch is a failed check and shows what was received.
    /// </summary>
    public HttpHealthCheckOptions ExpectContentType(string prefix)
    {
        Expectations.ExpectContentType(prefix);
        return this;
    }

    /// <summary>Requires the response body to contain <paramref name="text"/> (case-sensitive).</summary>
    public HttpHealthCheckOptions ExpectBodyContains(string text)
    {
        Expectations.ExpectBodyContains(text);
        return this;
    }

    /// <summary>Requires the response body to match the regular expression <paramref name="pattern"/> (.NET syntax).</summary>
    public HttpHealthCheckOptions ExpectBodyMatches(string pattern)
    {
        Expectations.ExpectBodyMatches(pattern);
        return this;
    }

    /// <summary>
    /// Requires this many successful checks in a row (a failure resets the count).
    /// With several URLs, one check means every URL passed.
    /// </summary>
    public HttpHealthCheckOptions ConsecutiveSuccesses(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "Must be at least 1.");
        RequiredConsecutiveSuccesses = count;
        return this;
    }

    /// <summary>
    /// Waits at least this long between counted successful checks, so
    /// ConsecutiveSuccesses(3).MinGapSeconds(11) means three passing checks spread
    /// over at least 22 seconds.
    /// </summary>
    public HttpHealthCheckOptions MinGapSeconds(int seconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        MinimumGapSeconds = seconds;
        return this;
    }

    /// <summary>
    /// Requires the response body to be JSON with <paramref name="expected"/> at
    /// <paramref name="path"/> (dotted path, e.g. "version" or "build.commit").
    /// Numbers and booleans compare by their JSON text. Applies to every URL of
    /// this call; use a separate call for URLs that don't return the value.
    /// </summary>
    public HttpHealthCheckOptions ExpectJson(string path, string expected)
    {
        _expectedJson.Add(new KeyValuePair<string, string>(path, expected));
        return this;
    }
}
