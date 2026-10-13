// =============================================================================
// DatabaseTestProvidersTests.cs
//
// Summary: Unit tests for ANDO_TEST_DB_PROVIDERS parsing.
//
// Design Decisions:
// - Tests Parse() directly so the process environment is never modified
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Unit.TestInfrastructure;

/// <summary>
/// Tests for <see cref="DatabaseTestProviders"/>.
/// </summary>
public class DatabaseTestProvidersTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(",")]
    public void Parse_Unset_SelectsBothProviders(string? value)
    {
        DatabaseTestProviders.Parse(value)
            .ShouldBe([DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql], ignoreOrder: true);
    }

    [Theory]
    [InlineData("SqlServer", DatabaseProvider.SqlServer)]
    [InlineData(" postgresql ", DatabaseProvider.PostgreSql)]
    [InlineData("PostgreSql,postgresql", DatabaseProvider.PostgreSql)]
    public void Parse_SingleProvider_SelectsOnlyThatProvider(string value, DatabaseProvider expected)
    {
        DatabaseTestProviders.Parse(value).ShouldBe([expected]);
    }

    [Fact]
    public void Parse_BothProviders_SelectsBoth()
    {
        DatabaseTestProviders.Parse("SqlServer, PostgreSql").Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("SqlServer,MySql")]
    [InlineData("1")]
    public void Parse_UnknownProvider_Throws(string value)
    {
        var ex = Should.Throw<InvalidOperationException>(() => DatabaseTestProviders.Parse(value));
        ex.Message.ShouldContain(DatabaseTestProviders.EnvironmentVariable);
        ex.Message.ShouldContain("SqlServer, PostgreSql");
    }
}
