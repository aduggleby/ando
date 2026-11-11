// =============================================================================
// HttpGetOptions.cs
//
// Summary: Fluent options for Http.Get: accept any status and expect a content
// type or body content.
// =============================================================================

namespace Ando.Operations;

/// <summary>Options for <see cref="HttpOperations.Get(string, Action{HttpGetOptions})"/>.</summary>
public class HttpGetOptions
{
    internal bool AllowAnyStatusCode { get; private set; }
    internal HttpResponseExpectations Expectations { get; } = new();

    /// <summary>Do not fail the step on a non-2xx status (same as allowAnyStatus: true).</summary>
    public HttpGetOptions AllowAnyStatus()
    {
        AllowAnyStatusCode = true;
        return this;
    }

    /// <summary>
    /// Requires the Content-Type header to start with <paramref name="prefix"/> (case-insensitive),
    /// e.g. "text/html" or "application/json".
    /// </summary>
    public HttpGetOptions ExpectContentType(string prefix)
    {
        Expectations.ExpectContentType(prefix);
        return this;
    }

    /// <summary>Requires the response body to contain <paramref name="text"/> (case-sensitive).</summary>
    public HttpGetOptions ExpectBodyContains(string text)
    {
        Expectations.ExpectBodyContains(text);
        return this;
    }

    /// <summary>Requires the response body to match the regular expression <paramref name="pattern"/> (.NET syntax).</summary>
    public HttpGetOptions ExpectBodyMatches(string pattern)
    {
        Expectations.ExpectBodyMatches(pattern);
        return this;
    }
}
