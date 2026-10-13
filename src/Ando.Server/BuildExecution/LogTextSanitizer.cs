// =============================================================================
// LogTextSanitizer.cs
//
// Summary: Prepares build log text for storage in BuildLogEntries.
//
// Build output is arbitrary process output and can contain NUL characters.
// PostgreSQL `text`/`varchar` columns reject '\0', which would fail the insert
// and lose the log line, so NULs are stripped before the entity is created.
//
// Design Decisions:
// - Applied on every provider so stored logs look the same everywhere
// - NULs are stripped before truncation, so the 4000-character limit applies to
//   the text that is actually stored
// - Step names are only stripped, not truncated, matching prior behavior
// =============================================================================

namespace Ando.Server.BuildExecution;

/// <summary>
/// Sanitizes build log text before it is written to the database.
/// </summary>
public static class LogTextSanitizer
{
    /// <summary>
    /// Maximum stored length of a log message (matches the BuildLogEntry.Message column).
    /// </summary>
    public const int MaxMessageLength = 4000;

    /// <summary>
    /// Strips NUL characters and truncates the message to <see cref="MaxMessageLength"/>.
    /// </summary>
    /// <param name="message">Raw log message.</param>
    /// <returns>The sanitized message.</returns>
    public static string SanitizeMessage(string message)
    {
        var stripped = StripNul(message);
        return stripped.Length > MaxMessageLength ? stripped[..MaxMessageLength] : stripped;
    }

    /// <summary>
    /// Strips NUL characters from a step name.
    /// </summary>
    /// <param name="stepName">Raw step name, or null.</param>
    /// <returns>The step name without NUL characters, or null.</returns>
    public static string? SanitizeStepName(string? stepName) =>
        stepName is null ? null : StripNul(stepName);

    /// <summary>
    /// Removes every '\0' character from the text.
    /// </summary>
    /// <param name="text">Text to clean.</param>
    /// <returns>The text without NUL characters; the same instance when none are present.</returns>
    public static string StripNul(string text) =>
        text.Contains('\0') ? text.Replace("\0", string.Empty) : text;
}
