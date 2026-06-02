// =============================================================================
// SelfUpdateSettings.cs
//
// Summary: Optional self-update configuration for Ando.Server.
//
// Controls whether the server can check for and apply container image updates
// from an admin-triggered workflow.
//
// Design Decisions:
// - Disabled by default for explicit opt-in
// - Uses compose file + service name to avoid hard-coded host assumptions
// - Check interval is configurable and defaults to 5 minutes
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for optional server self-update features.
/// </summary>
public class SelfUpdateSettings
{
    /// <summary>
    /// Configuration section name in appsettings/environment configuration.
    /// </summary>
    public const string SectionName = "SelfUpdate";

    /// <summary>
    /// Enables self-update checks and admin-triggered updates.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Image reference to check and pull for updates.
    /// </summary>
    public string Image { get; set; } = "ghcr.io/aduggleby/ando-server:latest";

    /// <summary>
    /// Docker compose file path on the host.
    /// </summary>
    public string ComposeFilePath { get; set; } = "/opt/ando/docker-compose.yml";

    /// <summary>
    /// Docker compose service name for the app container.
    /// </summary>
    public string ServiceName { get; set; } = "ando-server";

    /// <summary>
    /// Runtime container name of the app container.
    /// </summary>
    public string ContainerName { get; set; } = "ando-server";

    /// <summary>
    /// Helper image used to run compose commands against the host Docker socket.
    /// </summary>
    public string HelperImage { get; set; } = "docker:27-cli";

    /// <summary>
    /// Interval, in minutes, for automatic update checks.
    /// </summary>
    public int CheckIntervalMinutes { get; set; } = 5;
}

