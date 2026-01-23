// =============================================================================
// EncryptionSettings.cs
//
// Summary: Configuration settings for data encryption.
//
// Used for encrypting sensitive data like project secrets and OAuth tokens.
// The encryption key must be kept secure and consistent across deployments.
//
// Design Decisions:
// - AES-256 requires a 32-byte (256-bit) key
// - Key should be provided via environment variable in production
// - A default key is used for development only
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for data encryption.
/// </summary>
public class EncryptionSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Encryption";

    /// <summary>
    /// Base64-encoded AES-256 encryption key (32 bytes).
    /// MUST be set via environment variable in production.
    /// </summary>
    public string Key { get; set; } = "";
}
