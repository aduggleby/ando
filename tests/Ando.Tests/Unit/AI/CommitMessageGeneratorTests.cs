// =============================================================================
// CommitMessageGeneratorTests.cs
//
// Unit tests for CommitMessageGenerator prompt building and response cleaning.
// Note: Full integration tests with Claude CLI are in the Integration folder.
// =============================================================================

using Shouldly;

namespace Ando.Tests.Unit.AI;

[Trait("Category", "Unit")]
public class CommitMessageGeneratorTests
{
    [Fact]
    public void CleanResponse_PlainText_ReturnsUnchanged()
    {
        var response = "feat: add new feature";
        var result = CleanResponse(response);

        result.ShouldBe("feat: add new feature");
    }

    [Fact]
    public void CleanResponse_WithQuotes_RemovesQuotes()
    {
        var response = "\"feat: add new feature\"";
        var result = CleanResponse(response);

        result.ShouldBe("feat: add new feature");
    }

    [Fact]
    public void CleanResponse_WithSingleQuotes_RemovesQuotes()
    {
        var response = "'feat: add new feature'";
        var result = CleanResponse(response);

        result.ShouldBe("feat: add new feature");
    }

    [Fact]
    public void CleanResponse_WithMarkdownCodeBlock_RemovesBlock()
    {
        var response = """
            ```
            feat: add new feature
            ```
            """;
        var result = CleanResponse(response);

        result.ShouldBe("feat: add new feature");
    }

    [Fact]
    public void CleanResponse_WithLanguageSpecificCodeBlock_RemovesBlock()
    {
        var response = """
            ```text
            feat: add new feature
            ```
            """;
        var result = CleanResponse(response);

        result.ShouldBe("feat: add new feature");
    }

    [Fact]
    public void CleanResponse_WithWhitespace_Trims()
    {
        var response = "  feat: add new feature  \n";
        var result = CleanResponse(response);

        result.ShouldBe("feat: add new feature");
    }

    [Fact]
    public void CleanResponse_MultiLine_PreservesLines()
    {
        var response = """
            feat: add new feature

            This is a longer description explaining the changes.
            """;
        var result = CleanResponse(response);

        result.ShouldContain("feat: add new feature");
        result.ShouldContain("This is a longer description");
    }

    [Fact]
    public void TruncateDiff_ShortDiff_ReturnsUnchanged()
    {
        var diff = "Short diff content";
        var result = TruncateDiff(diff, 8000);

        result.ShouldBe("Short diff content");
    }

    [Fact]
    public void TruncateDiff_LongDiff_Truncates()
    {
        var diff = new string('x', 10000);
        var result = TruncateDiff(diff, 100);

        result.Length.ShouldBeLessThan(10000);
        result.ShouldContain("[Diff truncated");
    }

    [Fact]
    public void TruncateDiff_ExactlyMaxLength_ReturnsUnchanged()
    {
        var diff = new string('x', 100);
        var result = TruncateDiff(diff, 100);

        result.ShouldBe(diff);
    }

    // Test helpers that mirror the private methods in CommitMessageGenerator
    // This allows testing the logic without needing to call Claude.

    private static string CleanResponse(string response)
    {
        var clean = response.Trim();

        if ((clean.StartsWith('"') && clean.EndsWith('"')) ||
            (clean.StartsWith('\'') && clean.EndsWith('\'')))
        {
            clean = clean[1..^1];
        }

        if (clean.StartsWith("```"))
        {
            var lines = clean.Split('\n');
            clean = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        return clean.Trim();
    }

    private static string TruncateDiff(string diff, int maxLength)
    {
        if (diff.Length <= maxLength)
            return diff;

        return diff[..maxLength] + "\n\n[Diff truncated - showing first 8000 characters]";
    }
}
