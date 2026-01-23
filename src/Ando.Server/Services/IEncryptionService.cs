// =============================================================================
// IEncryptionService.cs
//
// Summary: Interface for encrypting and decrypting sensitive data.
//
// Used to protect secrets like project environment variables and OAuth tokens.
// Implementation uses AES-256 encryption with a configured key.
// =============================================================================

namespace Ando.Server.Services;

/// <summary>
/// Service for encrypting and decrypting sensitive data.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Encrypts a plaintext string.
    /// </summary>
    /// <param name="plainText">The text to encrypt.</param>
    /// <returns>Base64-encoded encrypted string.</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypts an encrypted string.
    /// </summary>
    /// <param name="cipherText">Base64-encoded encrypted string.</param>
    /// <returns>The decrypted plaintext.</returns>
    string Decrypt(string cipherText);
}
