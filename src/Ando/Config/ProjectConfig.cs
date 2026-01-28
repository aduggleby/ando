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
