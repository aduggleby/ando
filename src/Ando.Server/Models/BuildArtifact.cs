// =============================================================================
// BuildArtifact.cs
//
// Summary: Metadata for an artifact produced by a build.
//
// Artifacts are files produced during a build that are saved for later access.
// The actual file content is stored on disk (Docker volume), while this entity
// tracks metadata and provides download links.
//
// Design Decisions:
// - StoragePath is relative to the artifacts volume root
// - ExpiresAt enables automatic cleanup via retention policy
// - SizeBytes stored for display without hitting disk
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// Metadata for an artifact file produced by a build.
/// </summary>
public class BuildArtifact
{
    /// <summary>
    /// Unique identifier for this artifact.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// ID of the build that produced this artifact.
    /// </summary>
    public int BuildId { get; set; }

    /// <summary>
    /// The build that produced this artifact.
    /// </summary>
    public Build Build { get; set; } = null!;

    /// <summary>
    /// Display name for the artifact (e.g., "myapp-1.0.0.zip").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Path to the artifact file relative to the artifacts volume root.
    /// Format: "{projectId}/{buildId}/{filename}"
    /// </summary>
    public string StoragePath { get; set; } = "";

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// When the artifact was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the artifact will be automatically deleted.
    /// Based on the retention policy at build time.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    // -------------------------------------------------------------------------
    // Helper Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Whether the artifact has expired and should be deleted.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// Human-readable file size.
    /// </summary>
    public string FormattedSize
    {
        get
        {
            string[] sizes = ["B", "KB", "MB", "GB"];
            double size = SizeBytes;
            int order = 0;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }
}
