// =============================================================================
// HttpResponseExpectations.cs
//
// Summary: Content-type and body expectations shared by Http.Get and
// Http.WaitForHealthy.
//
// Collects the expectations configured on HttpGetOptions or
// HttpHealthCheckOptions and checks a response against them. A mismatch is
// described with what was received (content type and a truncated body) so the
// failing step explains itself.
//
// Design Decisions:
// - Content type is a case-insensitive prefix match, so "text/html" also
//   accepts "text/html; charset=utf-8".
// - Regexes are validated when configured and run with a timeout so a bad
//   pattern cannot hang a build.
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Operations;

/// <summary>Expectations about a response's content type and body.</summary>
internal sealed class HttpResponseExpectations
{
    private const int MaxReceivedChars = 200;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private string? _contentTypePrefix;
    private readonly List<string> _bodyContains = [];
    private readonly List<Regex> _bodyMatches = [];

    /// <summary>True when the response body must be read to evaluate the expectations.</summary>
    public bool NeedsBody => _bodyContains.Count > 0 || _bodyMatches.Count > 0;

    /// <summary>True when any expectation is configured.</summary>
    public bool Any => _contentTypePrefix != null || NeedsBody;

    public void ExpectContentType(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _contentTypePrefix = prefix.Trim();
    }

    public void ExpectBodyContains(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        _bodyContains.Add(text);
    }

    public void ExpectBodyMatches(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        try
        {
            _bodyMatches.Add(new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regular expression \"{pattern}\": {ex.Message}", nameof(pattern));
        }
    }

    /// <summary>Returns null when the response meets every expectation, otherwise a description of the first mismatch.</summary>
    /// <param name="contentType">The Content-Type header value, or null when absent.</param>
    /// <param name="body">The response body; only needed when <see cref="NeedsBody"/>.</param>
    public string? Check(string? contentType, string body)
    {
        if (_contentTypePrefix != null &&
            contentType?.StartsWith(_contentTypePrefix, StringComparison.OrdinalIgnoreCase) != true)
        {
            return $"content type \"{contentType ?? "(none)"}\" does not start with \"{_contentTypePrefix}\"; received: {Preview(body)}";
        }

        foreach (var text in _bodyContains)
        {
            if (!body.Contains(text, StringComparison.Ordinal))
            {
                return $"body does not contain \"{text}\"; received: {Preview(body)}";
            }
        }

        foreach (var regex in _bodyMatches)
        {
            bool matched;
            try
            {
                matched = regex.IsMatch(body);
            }
            catch (RegexMatchTimeoutException)
            {
                return $"body regex /{regex}/ timed out";
            }

            if (!matched)
            {
                return $"body does not match /{regex}/; received: {Preview(body)}";
            }
        }

        return null;
    }

    // First characters of the body on one line, with a note when it was cut.
    internal static string Preview(string body)
    {
        var text = body.Length <= MaxReceivedChars ? body : body[..MaxReceivedChars];
        text = text.Replace("\r", "").Replace("\n", "\\n");
        var more = body.Length - MaxReceivedChars;
        return more > 0 ? $"\"{text}\" (+{more} more characters)" : $"\"{text}\"";
    }
}
