using Ando.Context;

namespace Ando.Tests.Unit.Context;

[Trait("Category", "Unit")]
public class VarsContextTests
{
    [Fact]
    public void Indexer_SetsAndGetsValue()
    {
        var vars = new VarsContext();
        vars["key"] = "value";

        Assert.Equal("value", vars["key"]);
    }

    [Fact]
    public void Indexer_ReturnsNullForMissingKey()
    {
        var vars = new VarsContext();

        Assert.Null(vars["nonexistent"]);
    }

    [Fact]
    public void Has_ReturnsTrueForExistingKey()
    {
        var vars = new VarsContext();
        vars["key"] = "value";

        Assert.True(vars.Has("key"));
    }

    [Fact]
    public void Has_ReturnsFalseForMissingKey()
    {
        var vars = new VarsContext();

        Assert.False(vars.Has("nonexistent"));
    }

    [Fact]
    public void All_ReturnsAllEntries()
    {
        var vars = new VarsContext();
        vars["key1"] = "value1";
        vars["key2"] = "value2";

        var all = vars.All();

        Assert.Equal(2, all.Count);
        Assert.Equal("value1", all["key1"]);
        Assert.Equal("value2", all["key2"]);
    }

    [Fact]
    public void Indexer_OverwritesExistingValue()
    {
        var vars = new VarsContext();
        vars["key"] = "original";
        vars["key"] = "updated";

        Assert.Equal("updated", vars["key"]);
    }

    [Fact]
    public void Indexer_SetNull_RemovesKey()
    {
        var vars = new VarsContext();
        vars["key"] = "value";
        vars["key"] = null;

        Assert.False(vars.Has("key"));
        Assert.Null(vars["key"]);
    }

    [Fact]
    public void Env_ReturnsEnvironmentVariable()
    {
        var vars = new VarsContext();
        var home = vars.Env("HOME") ?? vars.Env("USERPROFILE");

        // HOME or USERPROFILE should exist on most systems
        Assert.NotNull(home);
    }

    [Fact]
    public void Env_ReturnsNullForMissingVariable()
    {
        var vars = new VarsContext();

        Assert.Null(vars.Env("NONEXISTENT_VAR_12345"));
    }
}
