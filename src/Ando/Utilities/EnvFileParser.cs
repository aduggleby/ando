// =============================================================================
// EnvFileParser.cs
//
// Summary: Parses dotenv files (.env.ando, .env). Shared by the CLI's env-file
// loading and by Secrets.EnvFile so both read the format the same way.
//
// Design Decisions:
// - Internal: only the CLI and Secrets use it (tests reach it through
//   InternalsVisibleTo and AndoCli.ParseEnvFile)
// - One parser for both, so a file means the same thing whether the CLI loads it
//   or a script reads it with Secrets.EnvFile
// - Malformed lines (no '=') are skipped silently, like the original loader
// =============================================================================

namespace Ando.Utilities;

internal static class EnvFileParser
{
    // Parses a .env file into key-value pairs.
    // Supports:
    // - KEY=VALUE format
    // - export KEY=VALUE format (shell-compatible)
    // - Comments starting with #
    // - Inline comments: KEY=value # comment
    // - Quoted values (single or double quotes) - hash inside quotes is preserved
    // - Empty lines are ignored
    internal static Dictionary<string, string> Parse(string path)
    {
        var result = new Dictionary<string, string>();

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments.
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Handle 'export KEY=VALUE' syntax (common in shell scripts).
            if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[7..].TrimStart();

            // Find the first = sign.
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            var key = trimmed[..equalsIndex].Trim();
            var value = trimmed[(equalsIndex + 1)..].Trim();

            // Check if value is quoted.
            var isQuoted = (value.StartsWith('"') && value.EndsWith('"')) ||
                          (value.StartsWith('\'') && value.EndsWith('\''));

            if (isQuoted)
            {
                // Remove surrounding quotes, preserve content including any # characters.
                value = value[1..^1];
            }
            else
            {
                // For unquoted values, strip inline comments.
                var commentIndex = value.IndexOf('#');
                if (commentIndex > 0)
                {
                    value = value[..commentIndex].TrimEnd();
                }
            }

            result[key] = value;
        }

        return result;
    }
}
