// =============================================================================
// ArgumentBuilderPropertyTests.cs
//
// Summary: Property-based tests for ArgumentBuilder using FsCheck.
//
// These tests verify invariants hold across many randomly generated inputs.
// =============================================================================

using Ando.Execution;
using FsCheck;
using FsCheck.Xunit;

namespace Ando.Tests.Unit.PropertyBased;

[Trait("Category", "Unit")]
[Trait("Category", "PropertyBased")]
public class ArgumentBuilderPropertyTests
{
    [Property]
    public bool Build_AlwaysReturnsArray(string[] args)
    {
        if (args == null) return true;

        var builder = new ArgumentBuilder();
        foreach (var arg in args.Where(a => a != null))
        {
            builder.Add(arg);
        }

        var result = builder.Build();

        return result != null && result.GetType() == typeof(string[]);
    }

    [Property]
    public bool Add_PreservesOrder(string[] args)
    {
        if (args == null || args.Any(a => a == null)) return true;

        var builder = new ArgumentBuilder();
        foreach (var arg in args)
        {
            builder.Add(arg);
        }

        var result = builder.Build();

        return result.SequenceEqual(args);
    }

    [Property]
    public bool AddIf_TrueCondition_EquivalentToAdd(NonEmptyString arg)
    {
        var value = arg.Get;

        var withAdd = new ArgumentBuilder().Add(value).Build();
        var withAddIf = new ArgumentBuilder().AddIf(true, value).Build();

        return withAdd.SequenceEqual(withAddIf);
    }

    [Property]
    public bool AddIf_FalseCondition_HasNoEffect(NonEmptyString arg)
    {
        var value = arg.Get;

        var builder = new ArgumentBuilder();
        builder.AddIf(false, value);
        var result = builder.Build();

        return result.Length == 0;
    }

    [Property]
    public bool AddIfNotNull_WithNonNullValue_AddsValue(NonEmptyString arg)
    {
        var value = arg.Get;

        var builder = new ArgumentBuilder();
        builder.AddIfNotNull(value);
        var result = builder.Build();

        return result.Length == 1 && result[0] == value;
    }

    [Property]
    public bool AddIfNotNull_WithNull_HasNoEffect()
    {
        var builder = new ArgumentBuilder();
        builder.AddIfNotNull((string?)null);
        var result = builder.Build();

        return result.Length == 0;
    }

    [Property]
    public bool Build_CanBeCalledMultipleTimes_ReturnsSameContent(string[] args)
    {
        if (args == null || args.Any(a => a == null)) return true;

        var builder = new ArgumentBuilder();
        foreach (var arg in args)
        {
            builder.Add(arg);
        }

        var result1 = builder.Build();
        var result2 = builder.Build();

        return result1.SequenceEqual(result2);
    }

    [Property]
    public bool Build_ReturnsNewArrayEachTime(NonEmptyString arg)
    {
        var builder = new ArgumentBuilder().Add(arg.Get);

        var result1 = builder.Build();
        var result2 = builder.Build();

        return !ReferenceEquals(result1, result2);
    }
}
