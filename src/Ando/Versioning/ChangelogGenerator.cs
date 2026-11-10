// =============================================================================
// ChangelogGenerator.cs
//
// Summary: Deterministic changelog generation from conventional commits.
//
// Used by 'ando bump --changelog template'. No AI and no network: the section is
// built from the commit subjects and inserted below the changelog title.
//
// Design Decisions:
// - Groups: Features (feat), Fixes (fix), Other (everything else), in that order
// - Lines keep the full original subject so the scope stays visible
// - chore(release) commits are skipped; git order (newest first) is kept
// - A trailing HTML comment reminds the maintainer to review before committing
// =============================================================================

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ando.Versioning;

public static partial class ChangelogGenerator
{
    public const string ReviewMarker = "<!-- edit before committing -->";

    private static readonly (string Heading, string? Type)[] Groups =
        [("Features", "feat"), ("Fixes", "fix"), ("Other", null)];

    [GeneratedRegex(@"\A---[ \t]*\n(?:.*?\n)??---[ \t]*(?:\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    /// <summary>
    /// Commits that belong in the changelog: chore(release) commits and commits whose subject
    /// matches the release commit message ("{version}" is a wildcard) are removed.
    /// </summary>
    public static List<(string Sha, string Subject)> Filter(IEnumerable<(string Sha, string Subject)> commits,
        string? releaseCommitMessage = null)
    {
        Regex? release = null;
        if (!string.IsNullOrWhiteSpace(releaseCommitMessage))
        {
            var pattern = string.Join(".+", releaseCommitMessage.Split("{version}").Select(Regex.Escape));
            release = new Regex($"^{pattern}$", RegexOptions.CultureInvariant);
        }

        return commits
            .Where(c => !c.Subject.StartsWith("chore(release)", StringComparison.Ordinal)
                        && (release == null || !release.IsMatch(c.Subject)))
            .ToList();
    }

    /// <summary>Conventional commit type of a subject ("type(scope)!: text"), or null.</summary>
    public static string? GetType(string subject)
    {
        var colon = subject.IndexOf(':');
        if (colon <= 0) return null;

        var head = subject[..colon].TrimEnd('!');
        var paren = head.IndexOf('(');
        if (paren >= 0)
        {
            if (!head.EndsWith(')')) return null;
            head = head[..paren];
        }

        return head.Length > 0 && head.All(char.IsAsciiLetter) ? head.ToLowerInvariant() : null;
    }

    /// <summary>Renders the "## version (date)" section with LF line endings.</summary>
    public static string RenderSection(string version, DateTime date, IEnumerable<(string Sha, string Subject)> commits,
        string? releaseCommitMessage = null)
    {
        var filtered = Filter(commits, releaseCommitMessage);
        var sb = new StringBuilder();
        sb.Append($"## {version} ({date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})\n\n");

        foreach (var (heading, type) in Groups)
        {
            var lines = filtered
                .Where(c => type == null
                    ? GetType(c.Subject) is not ("feat" or "fix")
                    : GetType(c.Subject) == type)
                .Select(c => $"- {c.Subject} ({c.Sha})")
                .ToList();
            if (lines.Count == 0) continue;

            sb.Append($"### {heading}\n");
            foreach (var line in lines) sb.Append(line).Append('\n');
            sb.Append('\n');
        }

        sb.Append(ReviewMarker).Append("\n\n");
        return sb.ToString();
    }

    /// <summary>
    /// Inserts a section below the first "# " title line (and one following blank line).
    /// Creates the "# Changelog" title when the file has none. A file that starts with YAML
    /// frontmatter keeps it on top: the section goes after the frontmatter, and after a
    /// "# " title if one follows (no title is added to such files).
    /// </summary>
    public static string Insert(string? existing, string section)
    {
        if (string.IsNullOrEmpty(existing))
            return $"# Changelog\n\n{section}";

        var crlf = existing.Contains("\r\n");
        var text = crlf ? existing.Replace("\r\n", "\n") : existing;

        string result;
        var frontmatter = FrontmatterRegex().Match(text);
        if (frontmatter.Success)
        {
            var fm = frontmatter.Value.EndsWith('\n') ? frontmatter.Value : frontmatter.Value + "\n";
            var body = text[frontmatter.Length..];
            var trimmed = body.TrimStart('\n');
            var leading = body[..(body.Length - trimmed.Length)];
            result = trimmed.StartsWith("# ")
                ? fm + leading + InsertBelowTitle(trimmed, 0, section)
                : fm + (leading.Length > 0 ? leading : "\n") + section + trimmed;
        }
        else
        {
            var titleStart = text.StartsWith("# ") ? 0 : text.IndexOf("\n# ", StringComparison.Ordinal) + 1;
            result = titleStart == 0 && !text.StartsWith("# ")
                ? $"# Changelog\n\n{section}{text}"
                : InsertBelowTitle(text, titleStart, section);
        }

        return crlf ? result.Replace("\n", "\r\n") : result;
    }

    private static string InsertBelowTitle(string text, int titleStart, string section)
    {
        var lineEnd = text.IndexOf('\n', titleStart);
        var afterTitle = lineEnd < 0 ? text.Length : lineEnd + 1;
        if (afterTitle < text.Length && text[afterTitle] == '\n') afterTitle++;
        var head = text[..afterTitle];
        if (!head.EndsWith("\n\n")) head = head.TrimEnd('\n') + "\n\n";
        return head + section + text[afterTitle..];
    }
}
