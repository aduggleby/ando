using Xunit;

namespace WebApp.Tests;

public class UnitTests
{
    [Fact]
    public void Test_Passes()
    {
        Assert.True(true);
    }

    [Fact]
    public void Test_Addition()
    {
        Assert.Equal(4, 2 + 2);
    }
}
