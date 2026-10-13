// =============================================================================
// DatabaseTestContainerModeTests.cs
//
// Summary: Unit tests for ANDO_TEST_DB_CONTAINERS parsing.
//
// Design Decisions:
// - Tests Parse() directly so the process environment is never modified
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Unit.TestInfrastructure;

/// <summary>
/// Tests for <see cref="DatabaseTestContainerMode"/>.
/// </summary>
public class DatabaseTestContainerModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("shared")]
    [InlineData(" Shared ")]
    public void Parse_UnsetOrShared_SelectsShared(string? value)
    {
        DatabaseTestContainerMode.Parse(value).ShouldBe(DatabaseContainerMode.Shared);
    }

    [Theory]
    [InlineData("isolated")]
    [InlineData("ISOLATED")]
    public void Parse_Isolated_SelectsIsolated(string value)
    {
        DatabaseTestContainerMode.Parse(value).ShouldBe(DatabaseContainerMode.Isolated);
    }

    [Theory]
    [InlineData("isolate")]
    [InlineData("1")]
    [InlineData("shared,isolated")]
    public void Parse_UnknownValue_Throws(string value)
    {
        var ex = Should.Throw<InvalidOperationException>(() => DatabaseTestContainerMode.Parse(value));
        ex.Message.ShouldContain(DatabaseTestContainerMode.EnvironmentVariable);
        ex.Message.ShouldContain(value);
    }
}
