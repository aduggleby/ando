// =============================================================================
// SecretValuesTests.cs
//
// Summary: Tests for the process-wide SecretValues set and its use by
// ConsoleLogger. SecretValues is static, so these tests run in a non-parallel
// collection and clear the set before and after each test.
// =============================================================================

using Ando.Logging;

namespace Ando.Tests.Unit.Logging;

[CollectionDefinition("SecretValues", DisableParallelization = true)]
public class SecretValuesCollection
{
}

[Trait("Category", "Unit")]
[Collection("SecretValues")]
public class SecretValuesTests : IDisposable
{
    public SecretValuesTests() => SecretValues.Clear();

    public void Dispose() => SecretValues.Clear();

    [Fact]
    public void Redact_ReplacesAddedSecret()
    {
        SecretValues.Add("s3cr3t-value");

        SecretValues.Redact("token=s3cr3t-value;end").ShouldBe("token=[REDACTED];end");
    }

    [Fact]
    public void Redact_ReplacesEveryOccurrence()
    {
        SecretValues.Add("s3cr3t-value");

        SecretValues.Redact("s3cr3t-value and s3cr3t-value").ShouldBe("[REDACTED] and [REDACTED]");
    }

    [Fact]
    public void Redact_WithoutSecrets_ReturnsTextUnchanged()
    {
        SecretValues.Redact("nothing to hide").ShouldBe("nothing to hide");
    }

    [Fact]
    public void Redact_EmptyText_ReturnsEmpty()
    {
        SecretValues.Add("s3cr3t-value");

        SecretValues.Redact("").ShouldBe("");
    }

    [Fact]
    public void Add_ShortValues_AreIgnored()
    {
        SecretValues.Add("abcde"); // 5 chars: below the minimum
        SecretValues.Add("abcdef"); // 6 chars: redacted

        SecretValues.Redact("abcde abcdef").ShouldBe("abcde [REDACTED]");
    }

    [Fact]
    public void Add_NullOrEmpty_IsIgnored()
    {
        SecretValues.Add(null);
        SecretValues.Add("");

        SecretValues.Redact("text").ShouldBe("text");
    }

    [Fact]
    public void Redact_LongestSecretFirst_RedactsContainingSecretWhole()
    {
        SecretValues.Add("secret");
        SecretValues.Add("secret-with-suffix");

        SecretValues.Redact("x secret-with-suffix y").ShouldBe("x [REDACTED] y");
    }

    [Fact]
    public void Clear_RemovesSecrets()
    {
        SecretValues.Add("s3cr3t-value");

        SecretValues.Clear();

        SecretValues.Redact("s3cr3t-value").ShouldBe("s3cr3t-value");
    }

    [Theory]
    [InlineData("AZURE_CLIENT_SECRET", true)]
    [InlineData("GITHUB_TOKEN", true)]
    [InlineData("DB_PASSWORD", true)]
    [InlineData("MY_API_KEY", true)]
    [InlineData("APIKEY", true)]
    [InlineData("SSH_PRIVATE_KEY", true)]
    [InlineData("STORAGE_CONNECTION_STRING", true)]
    [InlineData("DEPLOY_CREDENTIALS", true)]
    [InlineData("GITHUB_PAT", true)]
    [InlineData("stripe_key", true)]
    [InlineData("PATH", false)]
    [InlineData("HOME", false)]
    [InlineData("DOTNET_CLI_HOME", false)]
    [InlineData("AZURE_SUBSCRIPTION_ID", false)]
    public void IsSecretName_DetectsSecretLookingNames(string name, bool expected)
    {
        SecretValues.IsSecretName(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("NODE_PATH")]
    [InlineData("GIT_EXEC_PATH")]
    [InlineData("LD_LIBRARY_PATH")]
    public void IsSecretName_PathVariables_AreNotSecrets(string name)
    {
        SecretValues.IsSecretName(name).ShouldBeFalse();
    }

    [Fact]
    public void AddFromEnvironment_AddsValuesOfSecretNamedVariables()
    {
        const string secretName = "ANDO_UNITTEST_API_TOKEN";
        const string plainName = "ANDO_UNITTEST_PLAIN_SETTING";
        Environment.SetEnvironmentVariable(secretName, "env-secret-value");
        Environment.SetEnvironmentVariable(plainName, "env-plain-value");
        try
        {
            SecretValues.AddFromEnvironment();

            SecretValues.Redact("env-secret-value env-plain-value").ShouldBe("[REDACTED] env-plain-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretName, null);
            Environment.SetEnvironmentVariable(plainName, null);
        }
    }

    // ---- ConsoleLogger ------------------------------------------------------

    private static string CaptureOutput(Action action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            action();
            writer.Flush();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void ConsoleLogger_Info_RedactsRegisteredSecrets()
    {
        SecretValues.Add("hunter2-secret");

        var output = CaptureOutput(() => new ConsoleLogger(useColor: false).Info("password is hunter2-secret"));

        output.ShouldContain("password is [REDACTED]");
        output.ShouldNotContain("hunter2-secret");
    }

    [Fact]
    public void ConsoleLogger_StepFailed_RedactsMessageAndStepName()
    {
        SecretValues.Add("hunter2-secret");

        var output = CaptureOutput(() =>
            new ConsoleLogger(useColor: false).StepFailed("Login hunter2-secret", TimeSpan.FromSeconds(1), "bad hunter2-secret"));

        output.ShouldContain("bad [REDACTED]");
        output.ShouldNotContain("hunter2-secret");
    }

    [Fact]
    public void ConsoleLogger_StepStartedAndCompleted_RedactContext()
    {
        SecretValues.Add("hunter2-secret");

        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepStarted("Step", "ctx hunter2-secret");
            logger.StepCompleted("Step", TimeSpan.FromSeconds(1), "ctx hunter2-secret");
        });

        output.ShouldContain("ctx [REDACTED]");
        output.ShouldNotContain("hunter2-secret");
    }

    [Fact]
    public void ConsoleLogger_RedactsSecretsInLogFileToo()
    {
        SecretValues.Add("hunter2-secret");
        var path = Path.Combine(Directory.CreateTempSubdirectory("ando-log-").FullName, "build.log");
        try
        {
            CaptureOutput(() =>
            {
                using var logger = new ConsoleLogger(useColor: false, logFilePath: path);
                logger.Info("x hunter2-secret y");
            });

            var text = File.ReadAllText(path);
            text.ShouldContain("x [REDACTED] y");
            text.ShouldNotContain("hunter2-secret");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
