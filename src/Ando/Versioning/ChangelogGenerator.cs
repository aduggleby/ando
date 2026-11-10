// =============================================================================
// ChangelogGenerator.cs
//
// Summary: Deterministic changelog generation from conventional commits.
//
// Used by 'ando bump --changelog template'. No AI and no network: the section is
// built from the commit subjects and inserted before the first "## " heading (or
// converted from an Unreleased section).
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

    /// <summary>The planned changelog edit: the entry that is written and the resulting file.</summary>
    public sealed record Entry(string Section, string Content);

    /// <summary>
    /// Plans the template changelog edit. A "## Unreleased" section (heading matched
    /// case-insensitively, "[Unreleased]" allowed) is converted into the release: its
    /// hand-written body comes first, then the generated groups. With keepUnreleased a new empty
    /// Unreleased heading stays above. Without such a section the entry is inserted per <see cref="Insert"/>.
    /// </summary>
    public static Entry Plan(string? existing, string version, DateTime date,
        IEnumerable<(string Sha, string Subject)> commits, string? releaseCommitMessage = null,
        string? unreleasedHeading = null, bool keepUnreleased = false)
    {
        var rendered = RenderSection(version, date, commits, releaseCommitMessage);
        if (string.IsNullOrEmpty(existing))
            return new Entry(rendered, Insert(existing, rendered));

        var crlf = existing.Contains("\r\n");
        var text = crlf ? existing.Replace("\r\n", "\n") : existing;
        var wanted = (string.IsNullOrWhiteSpace(unreleasedHeading) ? "Unreleased" : unreleasedHeading).Trim();

        var headings = Level2Headings(text, FrontmatterLength(text));
        var hit = headings.FindIndex(h => IsHeading(h.Line, wanted));
        if (hit < 0)
            return new Entry(rendered, Insert(existing, rendered));

        var (start, end, line) = headings[hit];
        var stop = hit + 1 < headings.Count ? headings[hit + 1].Start : text.Length;
        var body = text[end..stop];
        body = TrimBlankLines(body);
        var tail = rendered[(rendered.IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
        var release = rendered[..(rendered.IndexOf('\n') + 1)] + "\n" + (body.Length > 0 ? body + "\n\n" : "") + tail;
        var section = keepUnreleased ? line + "\n\n" + release : release;

        var head = text[..start];
        if (head.Length > 0) head = head.TrimEnd('\n') + "\n\n";
        if (FrontmatterLength(text) == 0 && !text.StartsWith("# ") && !text.Contains("\n# "))
            head = "# Changelog\n\n" + head;
        var result = head + section + text[stop..];
        if (stop == text.Length) result = result.TrimEnd('\n') + "\n";
        return new Entry(section, crlf ? result.Replace("\n", "\r\n") : result);
    }

    // Removes blank or whitespace-only lines at both ends.
    private static string TrimBlankLines(string body)
    {
        var lines = body.Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static bool IsHeading(string line, string wanted) =>
        StripBrackets(line[2..].Trim()).Equals(StripBrackets(wanted), StringComparison.OrdinalIgnoreCase);

    private static string StripBrackets(string name) =>
        name.Length > 1 && name[0] == '[' && name[^1] == ']' ? name[1..^1].Trim() : name;

    // The run of ``` or ~~~ (3 or more) a line starts with, or null.
    private static string? FenceMarker(string trimmed)
    {
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~')) return null;
        var n = 0;
        while (n < trimmed.Length && trimmed[n] == trimmed[0]) n++;
        return n >= 3 ? trimmed[..n] : null;
    }

    private static int FrontmatterLength(string text)
    {
        var m = FrontmatterRegex().Match(text);
        return m.Success ? m.Length : 0;
    }

    /// <summary>Lines starting with "## " (outside fenced code blocks) from the given offset.</summary>
    private static List<(int Start, int End, string Line)> Level2Headings(string text, int from)
    {
        var result = new List<(int, int, string)>();
        string? fence = null; // the opening fence marker, e.g. "```" or "~~~~"
        var pos = from;
        while (pos < text.Length)
        {
            var nl = text.IndexOf('\n', pos);
            var next = nl < 0 ? text.Length : nl + 1;
            var line = text[pos..(nl < 0 ? text.Length : nl)];
            var marker = FenceMarker(line.TrimStart());
            if (fence == null && marker != null) fence = marker;
            else if (fence != null && marker != null && marker[0] == fence[0] && marker.Length >= fence.Length
                     && line.TrimStart()[marker.Length..].Trim().Length == 0) fence = null;
            else if (fence == null && line.StartsWith("## ")) result.Add((pos, next, line.TrimEnd()));
            pos = next;
        }

        return result;
    }

    /// <summary>
    /// Inserts a section before the first "## " heading, i.e. below the "# " title and any intro
    /// text; without a "## " heading it is appended after the existing content. Creates the
    /// "# Changelog" title when the file has none. A file that starts with YAML frontmatter keeps
    /// it on top (no title is added to such files). "## " lines in fenced code blocks are ignored.
    /// </summary>
    public static string Insert(string? existing, string section)
    {
        if (string.IsNullOrEmpty(existing))
            return $"# Changelog\n\n{section}";

        var crlf = existing.Contains("\r\n");
        var text = crlf ? existing.Replace("\r\n", "\n") : existing;

        var fm = FrontmatterLength(text);
        if (fm > 0 && text[fm - 1] != '\n')
        {
            text = text.Insert(fm, "\n");
            fm++;
        }

        if (fm == 0 && !text.StartsWith("# ") && !text.Contains("\n# "))
            text = "# Changelog\n\n" + text;

        var headings = Level2Headings(text, fm);
        var at = headings.Count > 0 ? headings[0].Start : text.Length;
        var head = text[..at];
        if (head.Length > 0) head = head.TrimEnd('\n') + "\n\n";
        var result = head + section + text[at..];
        if (headings.Count == 0) result = result.TrimEnd('\n') + "\n";

        return crlf ? result.Replace("\n", "\r\n") : result;
    }
}
