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
// - Name must be a valid environment variable name: ^[A-Za-z_][A-Za-z0-9_]*$,
//   at most 100 characters (IsValidName). ASCII-only names make the
//   normalization identical in C# (ToUpperInvariant) and in the SQL Server
//   backfill. Existing rows that predate the rule are kept as they are.
// - Values encrypted at rest with AES-256
// - Name keeps the display casing of the first write; NormalizedName
//   (uppercase invariant, produced only by Normalize) carries the unique index
//   and is what lookups compare, so behavior is identical on case-insensitive
//   SQL Server collations and case-sensitive PostgreSQL
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Server.Models;

/// <summary>
/// An encrypted environment variable secret for a project.
/// </summary>
public partial class ProjectSecret
{
    /// <summary>
    /// Maximum length of a secret name (matches the column length).
    /// </summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// User-facing description of the secret name rule.
    /// </summary>
    public const string NameRuleDescription =
        "Secret names must start with a letter or underscore, contain only letters, digits, and underscores, " +
        "and be at most 100 characters long (e.g. MY_SECRET).";

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
    /// <see cref="Name"/> normalized with <see cref="Normalize"/>. Unique per project
    /// and used for every lookup by name, so "nuget_key" and "NUGET_KEY" address
    /// the same secret on every database provider.
    /// </summary>
    public string NormalizedName { get; set; } = "";

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

    /// <summary>
    /// The single normalization function for secret names, used for writes and lookups.
    /// </summary>
    /// <param name="name">The secret name as supplied by the user.</param>
    /// <returns>The name in uppercase invariant form.</returns>
    public static string Normalize(string name) => name.ToUpperInvariant();

    /// <summary>
    /// Returns whether <paramref name="name"/> is an acceptable secret name: an environment
    /// variable name matching <c>^[A-Za-z_][A-Za-z0-9_]*$</c> with at most
    /// <see cref="MaxNameLength"/> characters.
    /// </summary>
    /// <param name="name">The candidate name (not trimmed).</param>
    /// <returns>True when the name may be stored.</returns>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= MaxNameLength && NamePattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
