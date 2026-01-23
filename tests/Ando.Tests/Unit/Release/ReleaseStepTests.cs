// =============================================================================
// ReleaseStepTests.cs
//
// Unit tests for ReleaseStep record.
// =============================================================================

using Ando.Release;
using Shouldly;

namespace Ando.Tests.Unit.Release;

[Trait("Category", "Unit")]
public class ReleaseStepTests
{
    [Fact]
    public void ReleaseStep_Constructor_SetsAllProperties()
    {
        var step = new ReleaseStep("commit", "Commit changes", true, null);

        step.Id.ShouldBe("commit");
        step.Label.ShouldBe("Commit changes");
        step.Enabled.ShouldBeTrue();
        step.DisabledReason.ShouldBeNull();
    }

    [Fact]
    public void ReleaseStep_Disabled_HasReason()
    {
        var step = new ReleaseStep("commit", "Commit changes", false, "no changes");

        step.Enabled.ShouldBeFalse();
        step.DisabledReason.ShouldBe("no changes");
    }

    [Fact]
    public void ReleaseStep_RecordEquality()
    {
        var step1 = new ReleaseStep("bump", "Bump version", true, null);
        var step2 = new ReleaseStep("bump", "Bump version", true, null);

        step1.ShouldBe(step2);
    }

    [Fact]
    public void ReleaseStep_WithExpression()
    {
        var step = new ReleaseStep("push", "Push to remote", false, "no remote");

        var enabled = step with { Enabled = true, DisabledReason = null };

        enabled.Enabled.ShouldBeTrue();
        enabled.DisabledReason.ShouldBeNull();
        enabled.Id.ShouldBe("push");
        enabled.Label.ShouldBe("Push to remote");
    }
}
