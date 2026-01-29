// =============================================================================
// EnvFileTests.cs
//
// Summary: Unit tests for environment file loading and parsing.
//
// Tests verify .env file parsing, priority resolution between .env.ando and .env,
// and proper handling of various file formats and edge cases.
// =============================================================================

using Ando.Cli;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class EnvFileTests : IDisposable
{
    private readonly string _tempDir;

    public EnvFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ando-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region ResolveEnvFilePath Tests

    [Fact]
    public void ResolveEnvFilePath_WithBothFiles_ReturnsEnvAndo()
    {
        // Arrange - create both .env.ando and .env
        File.WriteAllText(Path.Combine(_tempDir, ".env.ando"), "KEY=ando");
        File.WriteAllText(Path.Combine(_tempDir, ".env"), "KEY=default");

        // Act
        var result = AndoCli.ResolveEnvFilePath(_tempDir);

        // Assert - .env.ando should take priority
        result.ShouldNotBeNull();
        result.ShouldEndWith(".env.ando");
    }

    [Fact]
    public void ResolveEnvFilePath_WithOnlyEnvAndo_ReturnsEnvAndo()
    {
        // Arrange - create only .env.ando
        File.WriteAllText(Path.Combine(_tempDir, ".env.ando"), "KEY=ando");

        // Act
        var result = AndoCli.ResolveEnvFilePath(_tempDir);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldEndWith(".env.ando");
    }

    [Fact]
    public void ResolveEnvFilePath_WithOnlyEnv_ReturnsEnv()
    {
        // Arrange - create only .env
        File.WriteAllText(Path.Combine(_tempDir, ".env"), "KEY=default");

        // Act
        var result = AndoCli.ResolveEnvFilePath(_tempDir);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldEndWith(".env");
    }

    [Fact]
    public void ResolveEnvFilePath_WithNoFiles_ReturnsNull()
    {
        // Act
        var result = AndoCli.ResolveEnvFilePath(_tempDir);

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region ParseEnvFile Tests

    [Fact]
    public void ParseEnvFile_BasicKeyValue_ParsesCorrectly()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "KEY=value");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("KEY", "value");
    }

    [Fact]
    public void ParseEnvFile_MultipleKeyValues_ParsesAll()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "KEY1=value1\nKEY2=value2\nKEY3=value3");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContainKeyAndValue("KEY1", "value1");
        result.ShouldContainKeyAndValue("KEY2", "value2");
        result.ShouldContainKeyAndValue("KEY3", "value3");
    }

    [Fact]
    public void ParseEnvFile_IgnoresComments()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "# This is a comment\nKEY=value\n# Another comment");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContainKeyAndValue("KEY", "value");
    }

    [Fact]
    public void ParseEnvFile_IgnoresEmptyLines()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "KEY1=value1\n\n\nKEY2=value2\n");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseEnvFile_HandlesDoubleQuotedValues()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "KEY=\"quoted value\"");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("KEY", "quoted value");
    }

    [Fact]
    public void ParseEnvFile_HandlesSingleQuotedValues()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "KEY='quoted value'");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("KEY", "quoted value");
    }

    [Fact]
    public void ParseEnvFile_HandlesValuesWithEqualsSign()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "CONNECTION_STRING=Server=localhost;Database=test");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("CONNECTION_STRING", "Server=localhost;Database=test");
    }

    [Fact]
    public void ParseEnvFile_HandlesEmptyValue()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "EMPTY_KEY=");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("EMPTY_KEY", "");
    }

    [Fact]
    public void ParseEnvFile_TrimsWhitespace()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "  KEY  =  value  ");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("KEY", "value");
    }

    [Fact]
    public void ParseEnvFile_IgnoresLinesWithoutEquals()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "INVALID_LINE\nKEY=value");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.Count.ShouldBe(1);
        result.ShouldContainKeyAndValue("KEY", "value");
    }

    [Fact]
    public void ParseEnvFile_EmptyFile_ReturnsEmptyDictionary()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldBeEmpty();
    }

    #endregion

    #region Export Syntax Tests (Phase 4 Enhancement)

    [Fact]
    public void ParseEnvFile_ExportKeyValue_ParsesCorrectly()
    {
        // Arrange - export KEY=VALUE syntax commonly used in shell scripts
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "export API_KEY=secret123");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("API_KEY", "secret123");
    }

    [Fact]
    public void ParseEnvFile_ExportWithSpaces_ParsesCorrectly()
    {
        // Arrange - handle spaces after export
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "export   DATABASE_URL=postgres://localhost:5432/db");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("DATABASE_URL", "postgres://localhost:5432/db");
    }

    [Fact]
    public void ParseEnvFile_MixedExportAndPlain_ParsesBoth()
    {
        // Arrange - mix of export and plain syntax
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "export KEY1=value1\nKEY2=value2\nexport KEY3=value3");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContainKeyAndValue("KEY1", "value1");
        result.ShouldContainKeyAndValue("KEY2", "value2");
        result.ShouldContainKeyAndValue("KEY3", "value3");
    }

    [Fact]
    public void ParseEnvFile_ExportWithQuotedValue_ParsesCorrectly()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "export SECRET=\"my secret value\"");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("SECRET", "my secret value");
    }

    #endregion

    #region Inline Comment Tests (Phase 4 Enhancement)

    [Fact]
    public void ParseEnvFile_InlineComment_StripsComment()
    {
        // Arrange - inline comments after value
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "API_URL=https://api.example.com # Production API");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("API_URL", "https://api.example.com");
    }

    [Fact]
    public void ParseEnvFile_QuotedValueWithHash_PreservesHash()
    {
        // Arrange - hash inside quotes should be preserved (not treated as comment)
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "COLOR=\"#FF5733\"");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert - hash is part of the value, not a comment
        result.ShouldContainKeyAndValue("COLOR", "#FF5733");
    }

    [Fact]
    public void ParseEnvFile_InlineCommentWithSpaces_StripsCorrectly()
    {
        // Arrange
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "PORT=3000   # Default development port");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("PORT", "3000");
    }

    [Fact]
    public void ParseEnvFile_ExportWithInlineComment_HandlesCorrectly()
    {
        // Arrange - combine export syntax with inline comment
        var envPath = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envPath, "export NODE_ENV=production # Set to development for local");

        // Act
        var result = AndoCli.ParseEnvFile(envPath);

        // Assert
        result.ShouldContainKeyAndValue("NODE_ENV", "production");
    }

    #endregion
}
