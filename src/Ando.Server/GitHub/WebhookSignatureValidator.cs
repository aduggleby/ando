// =============================================================================
// WebhookSignatureValidator.cs
//
// Summary: Validates GitHub webhook request signatures.
//
// GitHub signs webhook payloads with HMAC-SHA256 using the configured webhook
// secret. This validator verifies that signatures match, ensuring requests
// are authentic and haven't been tampered with.
//
// Design Decisions:
// - Uses constant-time comparison to prevent timing attacks
// - Supports both sha256 and sha1 signatures (prefers sha256)
// - Returns false for any invalid input rather than throwing
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Ando.Server.GitHub;

/// <summary>
/// Validates GitHub webhook request signatures.
/// </summary>
public class WebhookSignatureValidator
{
    private readonly byte[] _secretBytes;

    public WebhookSignatureValidator(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("Webhook secret cannot be null or empty", nameof(secret));
        }

        _secretBytes = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>
    /// Validates the webhook signature against the payload.
    /// </summary>
    /// <param name="signature">The X-Hub-Signature-256 header value.</param>
    /// <param name="payload">The raw request body bytes.</param>
    /// <returns>True if the signature is valid.</returns>
    public bool Validate(string? signature, byte[] payload)
    {
        if (string.IsNullOrEmpty(signature) || payload == null || payload.Length == 0)
        {
            return false;
        }

        // GitHub sends signature as "sha256=<hex>" or "sha1=<hex>"
        if (signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateSha256(signature[7..], payload);
        }

        if (signature.StartsWith("sha1=", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateSha1(signature[5..], payload);
        }

        return false;
    }

    /// <summary>
    /// Validates using HMAC-SHA256 (preferred).
    /// </summary>
    private bool ValidateSha256(string expectedHex, byte[] payload)
    {
        using var hmac = new HMACSHA256(_secretBytes);
        var computedHash = hmac.ComputeHash(payload);
        var computedHex = Convert.ToHexString(computedHash);

        return ConstantTimeEquals(expectedHex, computedHex);
    }

    /// <summary>
    /// Validates using HMAC-SHA1 (legacy, less secure).
    /// </summary>
    private bool ValidateSha1(string expectedHex, byte[] payload)
    {
        using var hmac = new HMACSHA1(_secretBytes);
        var computedHash = hmac.ComputeHash(payload);
        var computedHex = Convert.ToHexString(computedHash);

        return ConstantTimeEquals(expectedHex, computedHex);
    }

    /// <summary>
    /// Compares two strings in constant time to prevent timing attacks.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var aBytes = Encoding.UTF8.GetBytes(a.ToLowerInvariant());
        var bBytes = Encoding.UTF8.GetBytes(b.ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
