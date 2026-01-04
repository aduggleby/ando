// =============================================================================
// BuildArtifactTests.cs
//
// Summary: Unit tests for the BuildArtifact model helper properties.
//
// Tests expiration checking and human-readable file size formatting.
// These are pure unit tests with no database dependencies.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Tests.Unit.Models;

public class BuildArtifactTests
{
    // -------------------------------------------------------------------------
    // IsExpired Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsExpired_WithFutureExpiration_ReturnsFalse()
    {
        // Arrange
        var artifact = new BuildArtifact
        {
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };

        // Act & Assert
        artifact.IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_WithPastExpiration_ReturnsTrue()
    {
        // Arrange
        var artifact = new BuildArtifact
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };

        // Act & Assert
        artifact.IsExpired.ShouldBeTrue();
    }

    [Fact]
    public void IsExpired_WithExactlyNow_ReturnsFalse()
    {
        // Arrange - set expiration slightly in the future to avoid race
        var artifact = new BuildArtifact
        {
            ExpiresAt = DateTime.UtcNow.AddSeconds(1)
        };

        // Act & Assert
        artifact.IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void IsExpired_WithJustExpired_ReturnsTrue()
    {
        // Arrange - set expiration slightly in the past
        var artifact = new BuildArtifact
        {
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1)
        };

        // Act & Assert
        artifact.IsExpired.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // FormattedSize Tests - Bytes
    // -------------------------------------------------------------------------

    [Fact]
    public void FormattedSize_WithZeroBytes_ReturnsZeroB()
    {
        // Arrange
        var artifact = new BuildArtifact { SizeBytes = 0 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("0 B");
    }

    [Fact]
    public void FormattedSize_WithSmallBytes_ReturnsBytes()
    {
        // Arrange
        var artifact = new BuildArtifact { SizeBytes = 512 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("512 B");
    }

    [Fact]
    public void FormattedSize_With1023Bytes_ReturnsBytes()
    {
        // Arrange - just under 1 KB
        var artifact = new BuildArtifact { SizeBytes = 1023 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("1023 B");
    }

    // -------------------------------------------------------------------------
    // FormattedSize Tests - Kilobytes
    // -------------------------------------------------------------------------

    [Fact]
    public void FormattedSize_WithExactlyOneKB_ReturnsOneKB()
    {
        // Arrange
        var artifact = new BuildArtifact { SizeBytes = 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("1 KB");
    }

    [Fact]
    public void FormattedSize_WithKilobytes_ReturnsKB()
    {
        // Arrange
        var artifact = new BuildArtifact { SizeBytes = 5 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("5 KB");
    }

    [Fact]
    public void FormattedSize_WithFractionalKB_ShowsDecimals()
    {
        // Arrange - 1.5 KB
        var artifact = new BuildArtifact { SizeBytes = 1536 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("1.5 KB");
    }

    // -------------------------------------------------------------------------
    // FormattedSize Tests - Megabytes
    // -------------------------------------------------------------------------

    [Fact]
    public void FormattedSize_WithExactlyOneMB_ReturnsOneMB()
    {
        // Arrange
        var artifact = new BuildArtifact { SizeBytes = 1024 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("1 MB");
    }

    [Fact]
    public void FormattedSize_WithMegabytes_ReturnsMB()
    {
        // Arrange - 10 MB
        var artifact = new BuildArtifact { SizeBytes = 10 * 1024 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("10 MB");
    }

    [Fact]
    public void FormattedSize_WithFractionalMB_ShowsDecimals()
    {
        // Arrange - 2.5 MB
        var artifact = new BuildArtifact { SizeBytes = (long)(2.5 * 1024 * 1024) };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("2.5 MB");
    }

    // -------------------------------------------------------------------------
    // FormattedSize Tests - Gigabytes
    // -------------------------------------------------------------------------

    [Fact]
    public void FormattedSize_WithExactlyOneGB_ReturnsOneGB()
    {
        // Arrange
        var artifact = new BuildArtifact { SizeBytes = 1024L * 1024 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("1 GB");
    }

    [Fact]
    public void FormattedSize_WithGigabytes_ReturnsGB()
    {
        // Arrange - 5 GB
        var artifact = new BuildArtifact { SizeBytes = 5L * 1024 * 1024 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("5 GB");
    }

    [Fact]
    public void FormattedSize_WithLargeGB_StaysInGB()
    {
        // Arrange - 100 GB (doesn't go to TB)
        var artifact = new BuildArtifact { SizeBytes = 100L * 1024 * 1024 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("100 GB");
    }

    [Fact]
    public void FormattedSize_WithVeryLargeSize_StaysInGB()
    {
        // Arrange - 1 TB worth of bytes stays as GB (no TB unit in array)
        var artifact = new BuildArtifact { SizeBytes = 1024L * 1024 * 1024 * 1024 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("1024 GB");
    }

    // -------------------------------------------------------------------------
    // FormattedSize Tests - Precision
    // -------------------------------------------------------------------------

    [Fact]
    public void FormattedSize_TruncatesToTwoDecimals()
    {
        // Arrange - 1.333... KB
        var artifact = new BuildArtifact { SizeBytes = 1365 };

        // Act & Assert
        // 1365 / 1024 = 1.333...
        artifact.FormattedSize.ShouldBe("1.33 KB");
    }

    [Fact]
    public void FormattedSize_RemovesTrailingZeros()
    {
        // Arrange - exactly 2.00 KB should show as "2 KB"
        var artifact = new BuildArtifact { SizeBytes = 2048 };

        // Act & Assert
        artifact.FormattedSize.ShouldBe("2 KB");
    }

    // -------------------------------------------------------------------------
    // Default Value Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildArtifact_HasCorrectDefaults()
    {
        // Arrange & Act
        var artifact = new BuildArtifact();

        // Assert
        artifact.Name.ShouldBe("");
        artifact.StoragePath.ShouldBe("");
        artifact.SizeBytes.ShouldBe(0);
    }
}
