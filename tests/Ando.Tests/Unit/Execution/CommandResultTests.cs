// =============================================================================
// CommandResultTests.cs
//
// Summary: Unit tests for CommandResult record.
//
// Tests verify that:
// - Factory methods create correct instances
// - Record equality works correctly
// - Deconstruction works as expected
// =============================================================================

using Ando.Execution;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class CommandResultTests
{
    [Fact]
    public void Ok_ReturnsSuccessResult()
    {
        var result = CommandResult.Ok();

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public void Ok_HasExitCodeZero()
    {
        var result = CommandResult.Ok();

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Ok_HasNullError()
    {
        var result = CommandResult.Ok();

        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failed_WithExitCode_SetsExitCode()
    {
        var result = CommandResult.Failed(42);

        result.ExitCode.ShouldBe(42);
    }

    [Fact]
    public void Failed_HasSuccessFalse()
    {
        var result = CommandResult.Failed(1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public void Failed_WithError_SetsErrorMessage()
    {
        var result = CommandResult.Failed(1, "Something went wrong");

        result.Error.ShouldBe("Something went wrong");
    }

    [Fact]
    public void Failed_WithoutError_HasNullError()
    {
        var result = CommandResult.Failed(1);

        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Record_Equality_SameValues_AreEqual()
    {
        var result1 = CommandResult.Ok();
        var result2 = CommandResult.Ok();

        result1.ShouldBe(result2);
    }

    [Fact]
    public void Record_Equality_DifferentSuccess_AreNotEqual()
    {
        var result1 = CommandResult.Ok();
        var result2 = CommandResult.Failed(0);

        result1.ShouldNotBe(result2);
    }

    [Fact]
    public void Record_Equality_DifferentExitCode_AreNotEqual()
    {
        var result1 = CommandResult.Failed(1);
        var result2 = CommandResult.Failed(2);

        result1.ShouldNotBe(result2);
    }

    [Fact]
    public void Record_Deconstruction_Works()
    {
        var result = new CommandResult(42, false, "error", "output");

        var (exitCode, success, error, output) = result;

        exitCode.ShouldBe(42);
        success.ShouldBeFalse();
        error.ShouldBe("error");
        output.ShouldBe("output");
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var result = new CommandResult(123, true, "message");

        result.ExitCode.ShouldBe(123);
        result.Success.ShouldBeTrue();
        result.Error.ShouldBe("message");
    }
}
