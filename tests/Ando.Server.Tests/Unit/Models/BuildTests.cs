// =============================================================================
// BuildTests.cs
//
// Summary: Unit tests for the Build model helper properties.
//
// Tests status-related computed properties (IsFinished, CanCancel, CanRetry)
// and the short commit SHA display. These are pure unit tests.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Tests.Unit.Models;

public class BuildTests
{
    // -------------------------------------------------------------------------
    // ShortCommitSha Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ShortCommitSha_WithFullSha_ReturnsFirst8Characters()
    {
        // Arrange
        var build = new Build { CommitSha = "abc123def456789012345678901234567890abcd" };

        // Act & Assert
        build.ShortCommitSha.ShouldBe("abc123de");
    }

    [Fact]
    public void ShortCommitSha_WithExactly8Characters_ReturnsAll()
    {
        // Arrange
        var build = new Build { CommitSha = "abc123de" };

        // Act & Assert
        build.ShortCommitSha.ShouldBe("abc123de");
    }

    [Fact]
    public void ShortCommitSha_WithShorterThan8Characters_ReturnsAll()
    {
        // Arrange
        var build = new Build { CommitSha = "abc" };

        // Act & Assert
        build.ShortCommitSha.ShouldBe("abc");
    }

    [Fact]
    public void ShortCommitSha_WithEmptyString_ReturnsEmpty()
    {
        // Arrange
        var build = new Build { CommitSha = "" };

        // Act & Assert
        build.ShortCommitSha.ShouldBe("");
    }

    // -------------------------------------------------------------------------
    // IsFinished Tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BuildStatus.Success)]
    [InlineData(BuildStatus.Failed)]
    [InlineData(BuildStatus.Cancelled)]
    [InlineData(BuildStatus.TimedOut)]
    public void IsFinished_WithTerminalStatus_ReturnsTrue(BuildStatus status)
    {
        // Arrange
        var build = new Build { Status = status };

        // Act & Assert
        build.IsFinished.ShouldBeTrue();
    }

    [Theory]
    [InlineData(BuildStatus.Queued)]
    [InlineData(BuildStatus.Running)]
    public void IsFinished_WithNonTerminalStatus_ReturnsFalse(BuildStatus status)
    {
        // Arrange
        var build = new Build { Status = status };

        // Act & Assert
        build.IsFinished.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // CanCancel Tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BuildStatus.Running)]
    [InlineData(BuildStatus.Queued)]
    public void CanCancel_WithCancellableStatus_ReturnsTrue(BuildStatus status)
    {
        // Arrange
        var build = new Build { Status = status };

        // Act & Assert
        build.CanCancel.ShouldBeTrue();
    }

    [Theory]
    [InlineData(BuildStatus.Success)]
    [InlineData(BuildStatus.Failed)]
    [InlineData(BuildStatus.Cancelled)]
    [InlineData(BuildStatus.TimedOut)]
    public void CanCancel_WithNonCancellableStatus_ReturnsFalse(BuildStatus status)
    {
        // Arrange
        var build = new Build { Status = status };

        // Act & Assert
        build.CanCancel.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // CanRetry Tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BuildStatus.Success)]
    [InlineData(BuildStatus.Failed)]
    [InlineData(BuildStatus.Cancelled)]
    [InlineData(BuildStatus.TimedOut)]
    public void CanRetry_WithFinishedStatus_ReturnsTrue(BuildStatus status)
    {
        // Arrange
        var build = new Build { Status = status };

        // Act & Assert
        build.CanRetry.ShouldBeTrue();
    }

    [Theory]
    [InlineData(BuildStatus.Queued)]
    [InlineData(BuildStatus.Running)]
    public void CanRetry_WithNonFinishedStatus_ReturnsFalse(BuildStatus status)
    {
        // Arrange
        var build = new Build { Status = status };

        // Act & Assert
        build.CanRetry.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Status Relationship Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CanCancel_And_CanRetry_AreMutuallyExclusive()
    {
        // All status values should have either CanCancel or CanRetry, never both
        foreach (BuildStatus status in Enum.GetValues<BuildStatus>())
        {
            var build = new Build { Status = status };

            // They should be mutually exclusive
            (build.CanCancel && build.CanRetry).ShouldBeFalse(
                $"Status {status} has both CanCancel and CanRetry");

            // At least one should be true
            (build.CanCancel || build.CanRetry).ShouldBeTrue(
                $"Status {status} has neither CanCancel nor CanRetry");
        }
    }

    [Fact]
    public void IsFinished_Matches_CanRetry()
    {
        // IsFinished and CanRetry should always have the same value
        foreach (BuildStatus status in Enum.GetValues<BuildStatus>())
        {
            var build = new Build { Status = status };
            build.IsFinished.ShouldBe(build.CanRetry,
                $"Status {status}: IsFinished={build.IsFinished} but CanRetry={build.CanRetry}");
        }
    }

    // -------------------------------------------------------------------------
    // Default Value Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_HasCorrectDefaults()
    {
        // Arrange & Act
        var build = new Build();

        // Assert
        build.Status.ShouldBe(BuildStatus.Queued);
        build.CommitSha.ShouldBe("");
        build.Branch.ShouldBe("");
        build.StepsTotal.ShouldBe(0);
        build.StepsCompleted.ShouldBe(0);
        build.StepsFailed.ShouldBe(0);
    }

    [Fact]
    public void NewBuild_IsNotFinished()
    {
        // Arrange & Act
        var build = new Build();

        // Assert
        build.IsFinished.ShouldBeFalse();
        build.CanCancel.ShouldBeTrue();
        build.CanRetry.ShouldBeFalse();
    }
}
