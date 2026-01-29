// =============================================================================
// ProjectSecret.cs
//
// Summary: Stores encrypted environment variable secrets for a project.
//
// Secrets are passed to builds as environment variables. Values are encrypted
// using AES-256 and can only be written/overwritten, never displayed in the UI.
//
// Design Decisions:
// - Write-only: secrets can be set or updated but never read back via UI
// - Name must be a valid environment variable name
// - Values encrypted at rest with AES-256
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// An encrypted environment variable secret for a project.
/// </summary>
public class ProjectSecret
{
    /// <summary>
    /// Unique identifier for this secret.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// ID of the project this secret belongs to.
    /// </summary>
    public int ProjectId { get; set; }

    /// <summary>
    /// The project this secret belongs to.
    /// </summary>
    public Project Project { get; set; } = null!;

    /// <summary>
    /// Environment variable name (e.g., "NUGET_API_KEY").
    /// Must be a valid environment variable name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// AES-256 encrypted value. Never expose this in the UI.
    /// </summary>
    public string EncryptedValue { get; set; } = "";

    /// <summary>
    /// When the secret was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the secret value was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
