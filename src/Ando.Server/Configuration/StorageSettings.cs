// =============================================================================
// StorageSettings.cs
//
// Summary: Configuration settings for artifact and data storage.
//
// Controls where build artifacts are stored and how long they are retained.
// The artifacts path should be a Docker volume for persistence.
//
// Design Decisions:
// - ArtifactsPath is the root directory for all artifact storage
// - Retention is configurable with sensible defaults
// - Separate retention for artifacts vs build logs
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for artifact and data storage.
/// </summary>
public class StorageSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Storage";

    /// <summary>
    /// Root path for artifact storage. Should be a Docker volume.
    /// </summary>
    public string ArtifactsPath { get; set; } = "/data/artifacts";

    /// <summary>
    /// Number of days to retain build artifacts before automatic deletion.
    /// Default is 30 days.
    /// </summary>
    public int ArtifactRetentionDays { get; set; } = 30;

    /// <summary>
    /// Number of days to retain build logs before deletion.
    /// Default is 90 days.
    /// </summary>
    public int BuildLogRetentionDays { get; set; } = 90;
}
