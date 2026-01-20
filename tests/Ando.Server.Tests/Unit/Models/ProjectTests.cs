// =============================================================================
// ProjectTests.cs
//
// Summary: Unit tests for the Project model helper methods.
//
// Tests branch filter matching logic and notification email resolution.
// These are pure unit tests with no database dependencies.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Tests.Unit.Models;

public class ProjectTests
{
    // -------------------------------------------------------------------------
    // MatchesBranchFilter Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MatchesBranchFilter_WithExactMatch_ReturnsTrue()
    {
        // Arrange
        var project = new Project { BranchFilter = "main" };

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeTrue();
    }

    [Fact]
    public void MatchesBranchFilter_WithNoMatch_ReturnsFalse()
    {
        // Arrange
        var project = new Project { BranchFilter = "main" };

        // Act & Assert
        project.MatchesBranchFilter("develop").ShouldBeFalse();
    }

    [Fact]
    public void MatchesBranchFilter_WithMultipleBranches_MatchesAny()
    {
        // Arrange
        var project = new Project { BranchFilter = "main,develop,staging" };

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeTrue();
        project.MatchesBranchFilter("develop").ShouldBeTrue();
        project.MatchesBranchFilter("staging").ShouldBeTrue();
        project.MatchesBranchFilter("feature").ShouldBeFalse();
    }

    [Fact]
    public void MatchesBranchFilter_IsCaseInsensitive()
    {
        // Arrange
        var project = new Project { BranchFilter = "Main,DEVELOP" };

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeTrue();
        project.MatchesBranchFilter("MAIN").ShouldBeTrue();
        project.MatchesBranchFilter("Main").ShouldBeTrue();
        project.MatchesBranchFilter("develop").ShouldBeTrue();
        project.MatchesBranchFilter("DEVELOP").ShouldBeTrue();
    }

    [Fact]
    public void MatchesBranchFilter_TrimsWhitespace()
    {
        // Arrange
        var project = new Project { BranchFilter = "  main  ,  develop  " };

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeTrue();
        project.MatchesBranchFilter("develop").ShouldBeTrue();
    }

    [Fact]
    public void MatchesBranchFilter_IgnoresEmptyEntries()
    {
        // Arrange
        var project = new Project { BranchFilter = "main,,develop,," };

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeTrue();
        project.MatchesBranchFilter("develop").ShouldBeTrue();
        project.MatchesBranchFilter("").ShouldBeFalse();
    }

    [Fact]
    public void MatchesBranchFilter_WithEmptyFilter_ReturnsFalse()
    {
        // Arrange
        var project = new Project { BranchFilter = "" };

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeFalse();
    }

    [Fact]
    public void MatchesBranchFilter_WithSlashInBranchName_MatchesExactly()
    {
        // Arrange
        var project = new Project { BranchFilter = "main,feature/auth,release/v1" };

        // Act & Assert
        project.MatchesBranchFilter("feature/auth").ShouldBeTrue();
        project.MatchesBranchFilter("release/v1").ShouldBeTrue();
        project.MatchesBranchFilter("feature/other").ShouldBeFalse();
    }

    [Fact]
    public void MatchesBranchFilter_DefaultFilter_MatchesMainAndMaster()
    {
        // Arrange - default filter is "main,master"
        var project = new Project();

        // Act & Assert
        project.MatchesBranchFilter("main").ShouldBeTrue();
        project.MatchesBranchFilter("master").ShouldBeTrue();
        project.MatchesBranchFilter("develop").ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // GetNotificationEmail Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetNotificationEmail_WithOverride_ReturnsOverride()
    {
        // Arrange
        var owner = new ApplicationUser { Email = "owner@example.com" };
        var project = new Project
        {
            Owner = owner,
            NotificationEmail = "team@example.com"
        };

        // Act & Assert
        project.GetNotificationEmail().ShouldBe("team@example.com");
    }

    [Fact]
    public void GetNotificationEmail_WithoutOverride_ReturnsOwnerEmail()
    {
        // Arrange
        var owner = new ApplicationUser { Email = "owner@example.com" };
        var project = new Project
        {
            Owner = owner,
            NotificationEmail = null
        };

        // Act & Assert
        project.GetNotificationEmail().ShouldBe("owner@example.com");
    }

    [Fact]
    public void GetNotificationEmail_WithNoEmails_ReturnsNull()
    {
        // Arrange
        var owner = new ApplicationUser { Email = null };
        var project = new Project
        {
            Owner = owner,
            NotificationEmail = null
        };

        // Act & Assert
        project.GetNotificationEmail().ShouldBeNull();
    }

    [Fact]
    public void GetNotificationEmail_WithEmptyOverride_ReturnsEmpty()
    {
        // Arrange - empty string is a valid override (disables notifications)
        var owner = new ApplicationUser { Email = "owner@example.com" };
        var project = new Project
        {
            Owner = owner,
            NotificationEmail = ""
        };

        // Act & Assert
        project.GetNotificationEmail().ShouldBe("");
    }

    // -------------------------------------------------------------------------
    // Default Value Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Project_HasCorrectDefaults()
    {
        // Arrange & Act
        var project = new Project();

        // Assert
        project.DefaultBranch.ShouldBe("main");
        project.BranchFilter.ShouldBe("main,master");
        project.EnablePrBuilds.ShouldBeFalse();
        project.TimeoutMinutes.ShouldBe(15);
        project.NotifyOnFailure.ShouldBeTrue();
        project.DockerImage.ShouldBeNull();
        project.NotificationEmail.ShouldBeNull();
    }
}
