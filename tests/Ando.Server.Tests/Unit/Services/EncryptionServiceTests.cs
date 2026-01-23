// =============================================================================
// EncryptionServiceTests.cs
//
// Summary: Unit tests for the EncryptionService.
//
// Tests encryption and decryption of secrets using AES-256.
// Verifies security properties like different ciphertext for same plaintext.
// =============================================================================

using Ando.Server.Services;
using Microsoft.Extensions.Options;
using Ando.Server.Configuration;

namespace Ando.Server.Tests.Unit.Services;

public class EncryptionServiceTests
{
    private readonly EncryptionService _service;
    // Base64-encoded 32-byte key for AES-256
    private const string TestKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    public EncryptionServiceTests()
    {
        var settings = new EncryptionSettings { Key = TestKey };
        _service = new EncryptionService(Options.Create(settings));
    }

    [Fact]
    public void Encrypt_ReturnsNonEmptyString()
    {
        // Arrange
        var plaintext = "my-secret-value";

        // Act
        var ciphertext = _service.Encrypt(plaintext);

        // Assert
        ciphertext.ShouldNotBeNullOrEmpty();
        ciphertext.ShouldNotBe(plaintext);
    }

    [Fact]
    public void Decrypt_ReversesEncryption()
    {
        // Arrange
        var plaintext = "my-secret-value";
        var ciphertext = _service.Encrypt(plaintext);

        // Act
        var decrypted = _service.Decrypt(ciphertext);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesDifferentOutputForSameInput()
    {
        // Arrange - due to random IV, same plaintext should produce different ciphertext
        var plaintext = "my-secret-value";

        // Act
        var ciphertext1 = _service.Encrypt(plaintext);
        var ciphertext2 = _service.Encrypt(plaintext);

        // Assert
        ciphertext1.ShouldNotBe(ciphertext2);
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        // Arrange
        var plaintext = "my-secret-value";
        var ciphertext = _service.Encrypt(plaintext);

        // Create service with different key (also base64-encoded 32 bytes)
        var wrongSettings = new EncryptionSettings { Key = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=" };
        var wrongService = new EncryptionService(Options.Create(wrongSettings));

        // Act & Assert
        Should.Throw<Exception>(() => wrongService.Decrypt(ciphertext));
    }

    [Fact]
    public void Encrypt_WithEmptyString_Works()
    {
        // Arrange
        var plaintext = "";

        // Act
        var ciphertext = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(ciphertext);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public void Encrypt_WithUnicode_Works()
    {
        // Arrange
        var plaintext = "密码 🔐 пароль";

        // Act
        var ciphertext = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(ciphertext);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public void Encrypt_WithLongString_Works()
    {
        // Arrange
        var plaintext = new string('x', 10000);

        // Act
        var ciphertext = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(ciphertext);

        // Assert
        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public void Decrypt_WithCorruptedCiphertext_Throws()
    {
        // Arrange
        var plaintext = "my-secret-value";
        var ciphertext = _service.Encrypt(plaintext);

        // Corrupt the ciphertext
        var corrupted = ciphertext[..^5] + "XXXXX";

        // Act & Assert
        Should.Throw<Exception>(() => _service.Decrypt(corrupted));
    }

    [Fact]
    public void Decrypt_WithInvalidBase64_Throws()
    {
        // Arrange
        var invalidCiphertext = "not-valid-base64!!!";

        // Act & Assert
        Should.Throw<Exception>(() => _service.Decrypt(invalidCiphertext));
    }
}
