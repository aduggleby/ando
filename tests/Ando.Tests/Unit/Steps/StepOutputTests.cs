// =============================================================================
// StepOutputTests.cs
//
// Summary: Tests for StepOutput: guarded Value, Json() path reading and
// ParsePath errors.
// =============================================================================

using Ando.Steps;

namespace Ando.Tests.Unit.Steps;

[Trait("Category", "Unit")]
public class StepOutputTests
{
    private static StepOutput With(string value)
    {
        var output = new StepOutput("Test.Step");
        output.Set(value);
        return output;
    }

    private const string Sample = """
        {
          "name": "app",
          "state": "Running",
          "count": 3,
          "enabled": true,
          "nothing": null,
          "siteConfig": {
            "ipSecurityRestrictions": [
              { "name": "first", "priority": 10 },
              { "name": "second", "priority": 20 }
            ],
            "ipSecurityRestrictionsDefaultAction": "Deny"
          },
          "matrix": [[1, 2], [3, 4]]
        }
        """;

    [Fact]
    public void Value_BeforeSet_ThrowsMentioningStepName()
    {
        var output = new StepOutput("Azure.Cli");

        var ex = Should.Throw<InvalidOperationException>(() => output.Value);

        ex.Message.ShouldContain("Azure.Cli");
        output.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ToString_BeforeSet_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new StepOutput("x").ToString());
    }

    [Fact]
    public void Set_TrimsValueAndStoresExitCode()
    {
        var output = new StepOutput("x");

        output.Set("  hello\n", exitCode: 4);

        output.HasValue.ShouldBeTrue();
        output.Value.ShouldBe("hello");
        output.ExitCode.ShouldBe(4);
        output.ToString().ShouldBe("hello");
    }

    [Fact]
    public void Set_Null_GivesEmptyValue()
    {
        var output = new StepOutput("x");

        output.Set(null);

        output.Value.ShouldBe("");
    }

    [Fact]
    public void StatusCode_WithoutHttpStatus_Throws()
    {
        Should.Throw<InvalidOperationException>(() => With("x").StatusCode);
    }

    [Fact]
    public void StatusCode_ReturnsSetStatus()
    {
        var output = new StepOutput("Http.Get");

        output.Set("ok", statusCode: 204);

        output.StatusCode.ShouldBe(204);
    }

    [Fact]
    public void Json_TopLevelProperty()
    {
        With(Sample).Json("name").ShouldBe("app");
    }

    [Fact]
    public void Json_DottedPath()
    {
        With(Sample).Json("siteConfig.ipSecurityRestrictionsDefaultAction").ShouldBe("Deny");
    }

    [Fact]
    public void Json_ArrayIndexAndNestedProperty()
    {
        With(Sample).Json("siteConfig.ipSecurityRestrictions[1].name").ShouldBe("second");
    }

    [Fact]
    public void Json_NestedArrayIndexes()
    {
        With(Sample).Json("matrix[1][0]").ShouldBe("3");
    }

    [Fact]
    public void Json_PropertyNamesAreCaseInsensitive()
    {
        With(Sample).Json("SITECONFIG.ipsecurityrestrictionsdefaultaction").ShouldBe("Deny");
    }

    [Fact]
    public void Json_NumbersAndBooleans_ReturnRawText()
    {
        var output = With(Sample);

        output.Json("count").ShouldBe("3");
        output.Json("enabled").ShouldBe("true");
    }

    [Fact]
    public void Json_JsonNull_ReturnsNull()
    {
        With(Sample).Json("nothing").ShouldBeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("siteConfig.missing")]
    [InlineData("siteConfig.ipSecurityRestrictions[5].name")]
    [InlineData("name.child")]
    [InlineData("name[0]")]
    [InlineData("siteConfig[0]")]
    public void Json_MissingPath_ReturnsNull(string path)
    {
        With(Sample).Json(path).ShouldBeNull();
    }

    [Fact]
    public void Json_ObjectOrArray_ReturnsRawJson()
    {
        var output = With(Sample);

        output.Json("siteConfig.ipSecurityRestrictions[0]").ShouldNotBeNull().ShouldContain("\"name\": \"first\"");
        output.Json("matrix[0]").ShouldNotBeNull().Replace(" ", "").ShouldBe("[1,2]");
    }

    [Fact]
    public void Json_NonJsonOutput_ThrowsJsonException()
    {
        Should.Throw<System.Text.Json.JsonException>(() => With("not json").Json("a"));
    }

    [Fact]
    public void Json_BeforeSet_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new StepOutput("x").Json("a"));
    }

    [Fact]
    public void ParsePath_SplitsNamesAndIndexes()
    {
        StepOutput.ParsePath("a.b[2].c").ToList().ShouldBe(["a", "b", 2, "c"]);
    }

    [Fact]
    public void ParsePath_ConsecutiveIndexes()
    {
        StepOutput.ParsePath("m[1][2]").ToList().ShouldBe(["m", 1, 2]);
    }

    [Theory]
    [InlineData("a[x]")]
    [InlineData("a[1")]
    [InlineData("a[]")]
    public void ParsePath_InvalidIndex_ThrowsArgumentException(string path)
    {
        Should.Throw<ArgumentException>(() => StepOutput.ParsePath(path).ToList());
    }

    [Fact]
    public void Json_InvalidPath_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => With(Sample).Json("name[x]"));
    }
}
