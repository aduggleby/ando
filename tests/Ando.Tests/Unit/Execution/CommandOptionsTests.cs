// =============================================================================
// CommandOptionsTests.cs
//
// Summary: Unit tests for CommandOptions class.
//
// Tests verify that:
// - Default values are correct
// - Properties can be set
// - Environment dictionary works correctly
// =============================================================================

using Ando.Execution;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class CommandOptionsTests
{
    [Fact]
    public void Default_HasNullWorkingDirectory()
    {
        var options = new CommandOptions();

        options.WorkingDirectory.ShouldBeNull();
    }

    [Fact]
    public void Default_HasEmptyEnvironment()
    {
        var options = new CommandOptions();

        options.Environment.ShouldBeEmpty();
    }

    [Fact]
    public void Default_HasNullTimeout()
    {
        var options = new CommandOptions();

        options.TimeoutMs.ShouldBeNull();
    }

    [Fact]
    public void WorkingDirectory_CanBeSet()
    {
        var options = new CommandOptions();

        options.WorkingDirectory = "/custom/path";

        options.WorkingDirectory.ShouldBe("/custom/path");
    }

    [Fact]
    public void TimeoutMs_CanBeSet()
    {
        var options = new CommandOptions();

        options.TimeoutMs = 30000;

        options.TimeoutMs.ShouldBe(30000);
    }

    [Fact]
    public void Environment_CanAddVariables()
    {
        var options = new CommandOptions();

        options.Environment["KEY1"] = "value1";
        options.Environment["KEY2"] = "value2";

        options.Environment.Count.ShouldBe(2);
        options.Environment["KEY1"].ShouldBe("value1");
        options.Environment["KEY2"].ShouldBe("value2");
    }

    [Fact]
    public void Environment_CanOverwriteVariables()
    {
        var options = new CommandOptions();

        options.Environment["KEY"] = "original";
        options.Environment["KEY"] = "updated";

        options.Environment["KEY"].ShouldBe("updated");
    }

    [Fact]
    public void AllProperties_CanBeSetTogether()
    {
        var options = new CommandOptions
        {
            WorkingDirectory = "/app",
            TimeoutMs = 60000
        };
        options.Environment["CI"] = "true";

        options.WorkingDirectory.ShouldBe("/app");
        options.TimeoutMs.ShouldBe(60000);
        options.Environment["CI"].ShouldBe("true");
    }
}
