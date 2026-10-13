// =============================================================================
// ProjectSecretTests.cs
//
// Summary: Unit tests for ProjectSecret.Normalize, the single normalization
// function for secret names used by writes and lookups, and for
// ProjectSecret.IsValidName, the secret name policy (environment variable
// name, ASCII only, at most 100 characters).
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Tests.Unit.Models;

public class ProjectSecretTests
{
    [Theory]
    [InlineData("nuget_key", "NUGET_KEY")]
    [InlineData("NUGET_KEY", "NUGET_KEY")]
    [InlineData("Nuget_Key", "NUGET_KEY")]
    [InlineData("api_key_2", "API_KEY_2")]
    public void Normalize_ReturnsUppercaseInvariant(string name, string expected)
    {
        ProjectSecret.Normalize(name).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_IsCultureInvariant()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            // Turkish culture uppercases 'i' to dotted 'İ'; invariant must not.
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

            ProjectSecret.Normalize("pi_key").ShouldBe("PI_KEY");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("MY_SECRET")]
    [InlineData("nuget_key")]
    [InlineData("_PRIVATE")]
    [InlineData("A")]
    [InlineData("api_key_2")]
    public void IsValidName_AcceptsEnvironmentVariableNames(string name)
    {
        ProjectSecret.IsValidName(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1KEY")]
    [InlineData("MY-SECRET")]
    [InlineData("MY SECRET")]
    [InlineData(" MY_SECRET")]
    [InlineData("MY_SECRET\n")]
    [InlineData("MY.SECRET")]
    [InlineData("KEY=VALUE")]
    [InlineData("ı_KEY")]
    [InlineData("İ_KEY")]
    [InlineData("SCHLÜSSEL")]
    public void IsValidName_RejectsInvalidNames(string? name)
    {
        ProjectSecret.IsValidName(name).ShouldBeFalse();
    }

    [Fact]
    public void IsValidName_EnforcesMaxLength()
    {
        ProjectSecret.IsValidName(new string('A', ProjectSecret.MaxNameLength)).ShouldBeTrue();
        ProjectSecret.IsValidName(new string('A', ProjectSecret.MaxNameLength + 1)).ShouldBeFalse();
    }
}
