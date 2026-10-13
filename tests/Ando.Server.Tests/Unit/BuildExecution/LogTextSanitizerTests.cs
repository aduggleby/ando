// =============================================================================
// LogTextSanitizerTests.cs
//
// Summary: Unit tests for LogTextSanitizer.
//
// Verifies NUL characters are stripped from log messages and step names (they
// are rejected by PostgreSQL text columns) and that message truncation to the
// column length still applies after stripping.
// =============================================================================

using Ando.Server.BuildExecution;

namespace Ando.Server.Tests.Unit.BuildExecution;

public class LogTextSanitizerTests
{
    [Fact]
    public void SanitizeMessage_StripsNulCharacters()
    {
        LogTextSanitizer.SanitizeMessage("a\0b\0\0c").ShouldBe("abc");
    }

    [Fact]
    public void SanitizeMessage_WithoutNul_ReturnsSameText()
    {
        const string message = "plain output";

        LogTextSanitizer.SanitizeMessage(message).ShouldBeSameAs(message);
    }

    [Fact]
    public void SanitizeMessage_TruncatesToMaxLength()
    {
        var message = new string('x', LogTextSanitizer.MaxMessageLength + 50);

        LogTextSanitizer.SanitizeMessage(message).Length.ShouldBe(LogTextSanitizer.MaxMessageLength);
    }

    [Fact]
    public void SanitizeMessage_StripsBeforeTruncating()
    {
        // 4000 visible characters interleaved with NULs: after stripping, all
        // 4000 visible characters fit and none are lost to truncation.
        var message = string.Concat(Enumerable.Repeat("x\0", LogTextSanitizer.MaxMessageLength));

        var result = LogTextSanitizer.SanitizeMessage(message);

        result.ShouldBe(new string('x', LogTextSanitizer.MaxMessageLength));
    }

    [Fact]
    public void SanitizeMessage_LongMessageWithNul_IsStrippedAndTruncated()
    {
        var message = "\0" + new string('y', LogTextSanitizer.MaxMessageLength + 10);

        var result = LogTextSanitizer.SanitizeMessage(message);

        result.Length.ShouldBe(LogTextSanitizer.MaxMessageLength);
        result.ShouldNotContain('\0');
    }

    [Fact]
    public void SanitizeStepName_StripsNulCharacters()
    {
        LogTextSanitizer.SanitizeStepName("Dotnet\0.Build").ShouldBe("Dotnet.Build");
    }

    [Fact]
    public void SanitizeStepName_Null_ReturnsNull()
    {
        LogTextSanitizer.SanitizeStepName(null).ShouldBeNull();
    }
}
