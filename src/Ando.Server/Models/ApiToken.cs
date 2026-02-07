// =============================================================================
// ApiToken.cs
//
// Summary: Personal API token for authenticating to the Ando.Server REST API.
//
// Tokens are generated once, shown to the user a single time, and stored as a
// hash for verification. Tokens can be revoked without deleting the user.
//
// Design Decisions:
// - Store only a hash (never the raw token) for security
// - Store a short Prefix to enable indexed lookups
// - Track LastUsedAt for audit/debugging
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// API token used for Bearer authentication to the REST API.
/// </summary>
public class ApiToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Friendly name for the token (e.g., "CI script", "Laptop").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Short token prefix used for indexed lookup (e.g., first 8 chars after prefix).
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Hash of the full token value (HMAC/SHA-256).
    /// </summary>
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

