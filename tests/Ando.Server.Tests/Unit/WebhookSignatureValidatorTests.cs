// =============================================================================
// WebhookSignatureValidatorTests.cs
//
// Summary: Unit tests for GitHub webhook signature validation.
//
// Tests the HMAC-SHA256 signature validation to ensure webhook payloads
// are properly authenticated and protected against tampering.
// =============================================================================

using Ando.Server.GitHub;
using System.Security.Cryptography;
using System.Text;

namespace Ando.Server.Tests.Unit;

public class WebhookSignatureValidatorTests
{
    private const string TestSecret = "test-webhook-secret";

    [Fact]
    public void Validate_WithValidSignature_ReturnsTrue()
    {
        // Arrange
        var payload = "{\"action\":\"push\",\"repository\":{\"name\":\"test\"}}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeSignature(payload, TestSecret);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(signature, payloadBytes);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithInvalidSignature_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var invalidSignature = "sha256=0000000000000000000000000000000000000000000000000000000000000000";
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(invalidSignature, payloadBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithWrongSecret_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeSignature(payload, TestSecret);
        var validator = new WebhookSignatureValidator("wrong-secret");

        // Act
        var result = validator.Validate(signature, payloadBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithTamperedPayload_ReturnsFalse()
    {
        // Arrange
        var originalPayload = "{\"action\":\"push\"}";
        var signature = ComputeSignature(originalPayload, TestSecret);
        var tamperedPayload = "{\"action\":\"delete\"}";
        var tamperedBytes = Encoding.UTF8.GetBytes(tamperedPayload);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(signature, tamperedBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithNullPayload_ReturnsFalse()
    {
        // Arrange
        var signature = "sha256=abc123";
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(signature, null!);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithNullSignature_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(null, payloadBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptySignature_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate("", payloadBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMalformedSignature_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var malformedSignature = "not-a-valid-signature";
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(malformedSignature, payloadBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMissingSha256Prefix_ReturnsFalse()
    {
        // Arrange
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signatureWithoutPrefix = ComputeSignature(payload, TestSecret).Replace("sha256=", "");
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(signatureWithoutPrefix, payloadBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyPayload_ReturnsFalse()
    {
        // Arrange - empty payload array should return false
        var emptyBytes = Array.Empty<byte>();
        var validator = new WebhookSignatureValidator(TestSecret);
        var signature = "sha256=abc123";

        // Act
        var result = validator.Validate(signature, emptyBytes);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithUnicodePayload_WorksCorrectly()
    {
        // Arrange
        var payload = "{\"message\":\"Hello 世界 🌍\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeSignature(payload, TestSecret);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(signature, payloadBytes);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithLargePayload_WorksCorrectly()
    {
        // Arrange
        var largePayload = new string('x', 100000);
        var payloadBytes = Encoding.UTF8.GetBytes(largePayload);
        var signature = ComputeSignature(largePayload, TestSecret);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Act
        var result = validator.Validate(signature, payloadBytes);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_WithNullSecret_ThrowsArgumentException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new WebhookSignatureValidator(null!));
    }

    [Fact]
    public void Constructor_WithEmptySecret_ThrowsArgumentException()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => new WebhookSignatureValidator(""));
    }

    [Theory]
    [InlineData("SHA256=", "sha256=")] // Case variations
    public void Validate_IsCaseInsensitiveForPrefix(string prefix1, string prefix2)
    {
        // Signatures should accept both uppercase and lowercase sha256 prefix
        var payload = "{\"action\":\"push\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var validator = new WebhookSignatureValidator(TestSecret);

        // Compute signature with lowercase
        var signatureHex = ComputeSignature(payload, TestSecret)[7..]; // Remove "sha256="

        var result1 = validator.Validate(prefix1 + signatureHex, payloadBytes);
        var result2 = validator.Validate(prefix2 + signatureHex, payloadBytes);

        result1.ShouldBeTrue();
        result2.ShouldBeTrue();
    }

    // Helper to compute the expected signature
    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
