// =============================================================================
// CommandResultPropertyTests.cs
//
// Summary: Property-based tests for CommandResult using FsCheck.
//
// These tests verify invariants hold across many randomly generated inputs.
// =============================================================================

using Ando.Execution;
using FsCheck;
using FsCheck.Xunit;

namespace Ando.Tests.Unit.PropertyBased;

[Trait("Category", "Unit")]
[Trait("Category", "PropertyBased")]
public class CommandResultPropertyTests
{
    [Property]
    public bool Ok_AlwaysHasExitCodeZero()
    {
        var result = CommandResult.Ok();
        return result.ExitCode == 0;
    }

    [Property]
    public bool Ok_AlwaysHasSuccessTrue()
    {
        var result = CommandResult.Ok();
        return result.Success == true;
    }

    [Property]
    public bool Failed_AlwaysHasSuccessFalse(int exitCode)
    {
        var result = CommandResult.Failed(exitCode);
        return result.Success == false;
    }

    [Property]
    public bool Failed_PreservesExitCode(int exitCode)
    {
        var result = CommandResult.Failed(exitCode);
        return result.ExitCode == exitCode;
    }

    [Property]
    public bool Failed_PreservesErrorMessage(NonEmptyString error)
    {
        var result = CommandResult.Failed(1, error.Get);
        return result.Error == error.Get;
    }

    [Property]
    public bool Record_Equality_IsSymmetric(int exitCode, bool success)
    {
        var result1 = new CommandResult(exitCode, success);
        var result2 = new CommandResult(exitCode, success);

        return result1.Equals(result2) == result2.Equals(result1);
    }

    [Property]
    public bool Record_Equality_IsReflexive(int exitCode, bool success)
    {
        var result = new CommandResult(exitCode, success);
        return result.Equals(result);
    }

    [Property]
    public bool Record_Equality_IsTransitive(int exitCode, bool success)
    {
        var result1 = new CommandResult(exitCode, success);
        var result2 = new CommandResult(exitCode, success);
        var result3 = new CommandResult(exitCode, success);

        return (result1.Equals(result2) && result2.Equals(result3))
            ? result1.Equals(result3)
            : true;
    }
}
