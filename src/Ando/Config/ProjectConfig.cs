// =============================================================================
// ProjectConfig.cs
//
// Summary: Project-level configuration stored in ando.config file.
//
// This record represents configuration options that can be persisted per-project
// in an ando.config file. Supports DIND and environment file auto-loading.
//
// Architecture:
// - JSON format for human readability and easy editing
// - Load() reads from project root, returns defaults if file doesn't exist
// - Save() writes to project root, creating the file if needed
// - Immutable record ensures thread safety
//
// Design Decisions:
// - Uses System.Text.Json for serialization (no external dependencies)
// - Returns default config (all false) when file is missing or invalid
// - Save() uses indented JSON for readability
// - File name "ando.config" follows common convention for tool config files
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ando.Config;

/// <summary>
/// Project-level configuration stored in ando.config file.
/// </summary>
public record ProjectConfig
{
    /// <summary>
    /// The config file name.
    /// </summary>
    public const string FileName = "ando.config";

    /// <summary>
    /// Whether Docker-in-Docker mode is enabled for this project.
    /// When true, the Docker socket is mounted into the build container.
    /// </summary>
    [JsonPropertyName("dind")]
    public bool Dind { get; init; } = false;

    /// <summary>
    /// Whether to automatically load environment variables from .env.ando or .env.
    /// When true, skips the prompt and loads env vars automatically.
    /// </summary>
    [JsonPropertyName("readEnv")]
    public bool ReadEnv { get; init; } = false;

    /// <summary>
    /// Whether to allow Claude CLI to run with --dangerously-skip-permissions.
    /// When true, skips the confirmation prompt for AI-powered commands.
    /// </summary>
    [JsonPropertyName("allowClaude")]
    public bool AllowClaude { get; init; } = false;

    /// <summary>
    /// Optional explicit path/command for the Claude CLI.
    /// Useful on Windows where npm installs a claude.cmd shim that may not be
    /// discoverable from non-interactive PATH contexts.
    /// </summary>
    [JsonPropertyName("claudePath")]
    public string? ClaudePath { get; init; }

    // Release settings for 'ando bump'. All are optional; absent settings keep the default
    // bump behavior (versions detected from build.csando, AI changelog and commit).

    /// <summary>
    /// Files (relative to the repo root) holding the version. When set, replaces project
    /// detection from build.csando. XML MSBuild files use &lt;Version&gt;, package.json uses "version".
    /// </summary>
    [JsonPropertyName("versionFiles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? VersionFiles { get; init; }

    /// <summary>Version scheme: "semver" (default) or "year-minor-patch" (YYYY.MINOR.PATCH).</summary>
    [JsonPropertyName("versionScheme")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VersionScheme { get; init; }

    /// <summary>Changelog mode: "claude" (default), "template" or "none".</summary>
    [JsonPropertyName("changelog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Changelog { get; init; }

    /// <summary>Repo paths whose commits feed the template changelog (default: whole repo).</summary>
    [JsonPropertyName("changelogPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ChangelogPaths { get; init; }

    /// <summary>Changelog file for template mode (default: CHANGELOG.md).</summary>
    [JsonPropertyName("changelogFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChangelogFile { get; init; }

    /// <summary>Heading of the hand-written section converted into the release (default: "Unreleased").</summary>
    [JsonPropertyName("unreleasedHeading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnreleasedHeading { get; init; }

    /// <summary>Keep an empty Unreleased heading above the new release section (default: false).</summary>
    [JsonPropertyName("keepUnreleased")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool KeepUnreleased { get; init; }

    /// <summary>Release commit message; {version} is replaced (default: "Bump version to {version}").</summary>
    [JsonPropertyName("commitMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CommitMessage { get; init; }

    /// <summary>Prefix of the tag created by 'ando bump --tag' (default: "v").</summary>
    [JsonPropertyName("tagPrefix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TagPrefix { get; init; }

    /// <summary>Glob matching release tags when looking for the last release (default: "v*").</summary>
    [JsonPropertyName("tagPattern")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TagPattern { get; init; }

    /// <summary>Refuse to bump with uncommitted changes instead of running the AI commit.</summary>
    [JsonPropertyName("requireClean")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequireClean { get; init; }

    /// <summary>
    /// Safe release mode: the changelog is a template or none (not Claude), or requireClean is set.
    /// In safe mode a dirty tree is refused and no AI commit is ever run.
    /// </summary>
    /// <param name="changelogOverride">The --changelog flag, which wins over the setting.</param>
    public bool IsSafeReleaseMode(string? changelogOverride = null)
        => (changelogOverride ?? Changelog ?? "claude") != "claude" || RequireClean;

    /// <summary>
    /// Loads project configuration from the specified directory.
    /// Returns default configuration if file doesn't exist or is invalid.
    /// </summary>
    /// <param name="projectRoot">The project root directory.</param>
    /// <returns>The loaded configuration or defaults.</returns>
    public static ProjectConfig Load(string projectRoot)
    {
        var configPath = Path.Combine(projectRoot, FileName);

        if (!File.Exists(configPath))
        {
            return new ProjectConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<ProjectConfig>(json) ?? new ProjectConfig();
        }
        catch (JsonException)
        {
            // Invalid JSON - return defaults
            return new ProjectConfig();
        }
    }

    /// <summary>
    /// Saves this configuration to the specified directory.
    /// Creates the file if it doesn't exist.
    /// </summary>
    /// <param name="projectRoot">The project root directory.</param>
    public void Save(string projectRoot)
    {
        var configPath = Path.Combine(projectRoot, FileName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(configPath, json);
    }
}
