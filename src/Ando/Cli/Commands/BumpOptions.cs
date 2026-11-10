// =============================================================================
// BumpOptions.cs
//
// Summary: Command-line options of 'ando bump' and their parser.
//
// Design Decisions:
// - Unknown options are rejected (like 'ando run'), so a typo never starts a real bump
// - Every option is opt-in; the defaults reproduce the original bump behavior
// =============================================================================

using Ando.Versioning;

namespace Ando.Cli.Commands;

public record BumpOptions
{
    /// <summary>"template", "claude" or "none"; null defers to ando.config.</summary>
    public string? Changelog { get; init; }
    public string? SetVersion { get; init; }
    public bool Suggest { get; init; }
    public bool DryRun { get; init; }
    public bool NoCommit { get; init; }
    public bool Tag { get; init; }

    public static readonly string[] ChangelogModes = ["template", "claude", "none"];

    /// <summary>
    /// Parses the arguments after "bump". Returns an error message, or null on success.
    /// </summary>
    public static string? TryParse(string[] args, out BumpType type, out BumpOptions options)
    {
        type = BumpType.Patch;
        options = new BumpOptions();
        var typeSeen = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--no-color": break; // global option, handled by the CLI
                case "--suggest": options = options with { Suggest = true }; break;
                case "--dry-run": options = options with { DryRun = true }; break;
                case "--no-commit": options = options with { NoCommit = true }; break;
                case "--tag": options = options with { Tag = true }; break;
                case "--changelog" or "--set":
                    if (i + 1 >= args.Length) return $"Option {arg} needs a value.";
                    var value = args[++i];
                    if (arg == "--set")
                    {
                        options = options with { SetVersion = value };
                    }
                    else
                    {
                        if (!ChangelogModes.Contains(value))
                            return $"Invalid --changelog value '{value}'. Use template, claude or none.";
                        options = options with { Changelog = value };
                    }
                    break;
                default:
                    if (arg.StartsWith('-')) return $"Unknown option for bump: {arg}";
                    if (typeSeen) return $"Unexpected argument: {arg}";
                    typeSeen = true;
                    var parsed = arg.ToLowerInvariant() switch
                    {
                        "minor" => BumpType.Minor,
                        "major" => BumpType.Major,
                        "patch" => BumpType.Patch,
                        _ => (BumpType?)null
                    };
                    if (parsed == null) return $"Invalid bump type: {arg}. Use 'patch', 'minor', or 'major'.";
                    type = parsed.Value;
                    break;
            }
        }

        if (options.Tag && options.NoCommit) return "--tag cannot be used with --no-commit.";
        return null;
    }
}
