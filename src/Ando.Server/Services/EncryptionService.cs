// =============================================================================
// EncryptionService.cs
//
// Summary: AES-256 encryption service for protecting sensitive data.
//
// Uses AES-256-CBC with a random IV for each encryption operation. The IV is
// prepended to the ciphertext so it can be extracted during decryption.
//
// Design Decisions:
// - AES-256 for strong encryption
// - Random IV per encryption for semantic security
// - IV prepended to ciphertext (IV is not secret)
// - Key from configuration (must be 32 bytes base64 encoded)
// =============================================================================

using System.Security.Cryptography;
using Ando.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// AES-256 encryption service implementation.
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IOptions<EncryptionSettings> settings)
    {
        var keyString = settings.Value.Key;

        if (string.IsNullOrEmpty(keyString))
        {
            throw new InvalidOperationException(
                "Encryption key is not configured. Set the Encryption:Key configuration value.");
        }

        _key = Convert.FromBase64String(keyString);

        if (_key.Length != 32)
        {
            throw new InvalidOperationException(
                "Encryption key must be 32 bytes (256 bits). " +
                "Generate one with: openssl rand -base64 32");
        }
    }

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Prepend IV to ciphertext
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        // Extract IV from beginning of ciphertext
        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullCipher.Length - iv.Length];

        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
