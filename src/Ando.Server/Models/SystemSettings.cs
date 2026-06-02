// =============================================================================
// SystemSettings.cs
//
// Summary: Global system-level settings controlled by administrators.
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// Global feature toggles and system behavior settings.
/// </summary>
public class SystemSettings
{
    /// <summary>
    /// Primary key. The application keeps a single row for global settings.
    /// </summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Whether new user self-registration is allowed.
    /// </summary>
    public bool AllowUserRegistration { get; set; } = true;

    /// <summary>
    /// Last time any setting was updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
