// =============================================================================
// HookContextTests.cs
//
// Unit tests for HookContext environment variable conversion.
// =============================================================================

using Ando.Hooks;
using Shouldly;

namespace Ando.Tests.Unit.Hooks;

[Trait("Category", "Unit")]
public class HookContextTests
{
    [Fact]
    public void ToEnvironment_SetsCommandVariable()
    {
        var context = new HookContext { Command = "bump" };

        var env = context.ToEnvironment();

        env.ShouldContainKey("ANDO_COMMAND");
        env["ANDO_COMMAND"].ShouldBe("bump");
    }

    [Fact]
    public void ToEnvironment_SetsOldVersionWhenProvided()
    {
        var context = new HookContext
        {
            Command = "bump",
            OldVersion = "1.0.0"
        };

        var env = context.ToEnvironment();

        env.ShouldContainKey("ANDO_OLD_VERSION");
        env["ANDO_OLD_VERSION"].ShouldBe("1.0.0");
    }

    [Fact]
    public void ToEnvironment_SetsNewVersionWhenProvided()
    {
        var context = new HookContext
        {
            Command = "bump",
            NewVersion = "1.0.1"
        };

        var env = context.ToEnvironment();

        env.ShouldContainKey("ANDO_NEW_VERSION");
        env["ANDO_NEW_VERSION"].ShouldBe("1.0.1");
    }

    [Fact]
    public void ToEnvironment_SetsBumpTypeWhenProvided()
    {
        var context = new HookContext
        {
            Command = "bump",
            BumpType = "patch"
        };

        var env = context.ToEnvironment();

        env.ShouldContainKey("ANDO_BUMP_TYPE");
        env["ANDO_BUMP_TYPE"].ShouldBe("patch");
    }

    [Fact]
    public void ToEnvironment_OmitsNullValues()
    {
        var context = new HookContext { Command = "commit" };

        var env = context.ToEnvironment();

        env.ShouldContainKey("ANDO_COMMAND");
        env.ShouldNotContainKey("ANDO_OLD_VERSION");
        env.ShouldNotContainKey("ANDO_NEW_VERSION");
        env.ShouldNotContainKey("ANDO_BUMP_TYPE");
    }

    [Fact]
    public void ToEnvironment_AllFieldsSet()
    {
        var context = new HookContext
        {
            Command = "bump",
            OldVersion = "1.0.0",
            NewVersion = "1.1.0",
            BumpType = "minor"
        };

        var env = context.ToEnvironment();

        env.Count.ShouldBe(4);
        env["ANDO_COMMAND"].ShouldBe("bump");
        env["ANDO_OLD_VERSION"].ShouldBe("1.0.0");
        env["ANDO_NEW_VERSION"].ShouldBe("1.1.0");
        env["ANDO_BUMP_TYPE"].ShouldBe("minor");
    }
}
