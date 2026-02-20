// =============================================================================
// ProjectSecretContracts.cs
//
// Summary: Secret-related request/response contracts for project endpoints.
// =============================================================================

using System.ComponentModel.DataAnnotations;

namespace Ando.Server.Contracts.Projects;

/// <summary>
/// Request to add or update a secret.
/// </summary>
public class SetSecretRequest
{
    /// <summary>
    /// Secret name (uppercase with underscores, e.g., MY_SECRET).
    /// </summary>
    [Required(ErrorMessage = "Secret name is required")]
    [RegularExpression(@"^[A-Z_][A-Z0-9_]*$",
        ErrorMessage = "Secret name must be uppercase with underscores only (e.g., MY_SECRET)")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Secret value to store.
    /// </summary>
    [Required(ErrorMessage = "Secret value is required")]
    public string Value { get; set; } = "";
}

/// <summary>
/// Response from secret operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message if operation failed.</param>
public record SecretResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Request to bulk import secrets from .env format.
/// </summary>
public class BulkImportSecretsRequest
{
    /// <summary>
    /// Content in .env format (KEY=value, one per line).
    /// </summary>
    [Required(ErrorMessage = "Content is required")]
    public string Content { get; set; } = "";
}

/// <summary>
/// Response from bulk import operation.
/// </summary>
/// <param name="Success">Whether import succeeded.</param>
/// <param name="ImportedCount">Number of secrets imported.</param>
/// <param name="Errors">List of import errors, if any.</param>
public record BulkImportSecretsResponse(
    bool Success,
    int ImportedCount,
    IReadOnlyList<string>? Errors = null
);

/// <summary>
/// Response from refreshing required secrets detection.
/// </summary>
/// <param name="Success">Whether refresh succeeded.</param>
/// <param name="DetectedSecrets">Required secrets detected from build.csando.</param>
/// <param name="DetectedProfiles">Available profiles detected from build.csando.</param>
public record RefreshSecretsResponse(
    bool Success,
    IReadOnlyList<string> DetectedSecrets,
    IReadOnlyList<string> DetectedProfiles
);
