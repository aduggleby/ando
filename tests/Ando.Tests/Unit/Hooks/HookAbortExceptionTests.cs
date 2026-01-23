// =============================================================================
// HookAbortExceptionTests.cs
//
// Unit tests for HookAbortException.
// =============================================================================

using Ando.Hooks;
using Shouldly;

namespace Ando.Tests.Unit.Hooks;

[Trait("Category", "Unit")]
public class HookAbortExceptionTests
{
    [Fact]
    public void Constructor_SetsHookName()
    {
        var ex = new HookAbortException("test-hook.csando", "Test error");

        ex.HookName.ShouldBe("test-hook.csando");
    }

    [Fact]
    public void Constructor_SetsMessage()
    {
        var ex = new HookAbortException("test-hook.csando", "Test error message");

        ex.Message.ShouldBe("Test error message");
    }

    [Fact]
    public void Constructor_DefaultsExitCodeToOne()
    {
        var ex = new HookAbortException("hook.csando", "Error");

        ex.ExitCode.ShouldBe(1);
    }

    [Fact]
    public void Constructor_WithExitCode_SetsExitCode()
    {
        var ex = new HookAbortException("hook.csando", 42, "Error");

        ex.ExitCode.ShouldBe(42);
    }
}
