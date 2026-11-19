// =============================================================================
// SecretsOperationsTests.cs
//
// Summary: Tests for the Secrets global (UserSecrets, EnvFile) and for Env()
// redaction. SecretValues is static, so these tests run in its collection.
// =============================================================================

using Ando.Logging;
using Ando.Scripting;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Ando.Workflow;

namespace Ando.Tests.Unit.Scripting;

[Trait("Category", "Unit")]
[Collection("SecretValues")]
public class SecretsOperationsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ando-secrets-").FullName;
    private readonly string _store;
    private readonly SecretsOperations _secrets;

    public SecretsOperationsTests()
    {
        SecretValues.Clear();
        _store = Path.Combine(_dir, "store");
        _secrets = new SecretsOperations(new RunSession { InvocationRoot = _dir }, _store);
    }

    public void Dispose()
    {
        SecretValues.Clear();
        Directory.Delete(_dir, recursive: true);
    }

    private void WriteStore(string id, string json)
    {
        Directory.CreateDirectory(Path.Combine(_store, id));
        File.WriteAllText(Path.Combine(_store, id, "secrets.json"), json);
    }

    [Fact]
    public void UserSecrets_NestedAndFlatKeys_AreFlattened()
    {
        WriteStore("my-id", """{ "Deploy": { "Azure": { "Secret": "nested-secret-value" } }, "Flat:Key": "flat-secret-value", "Port": 8080 }""");

        var set = _secrets.UserSecrets("my-id");

        set["Deploy:Azure:Secret"].ShouldBe("nested-secret-value");
        set["Flat:Key"].ShouldBe("flat-secret-value");
        set["Port"].ShouldBe("8080");
    }

    [Fact]
    public void UserSecrets_RegistersAValueOnlyWhenItIsRead()
    {
        WriteStore("my-id", """{ "A": { "B": "very-secret-one" }, "Region": "westeurope-1", "C": "very-secret-two" }""");

        var set = _secrets.UserSecrets("my-id");
        SecretValues.Redact("very-secret-one westeurope-1").ShouldBe("very-secret-one westeurope-1");

        _ = set["A:B"];
        set.TryGet("C", out _);

        SecretValues.Redact("x very-secret-one y very-secret-two westeurope-1")
            .ShouldBe("x [REDACTED] y [REDACTED] westeurope-1");
    }

    [Fact]
    public void MultiLineValue_EachLineIsRedactedOnItsOwn()
    {
        WriteStore("my-id", "{ \"Key\": \"-----BEGIN KEY-----\\r\\nabcdefghijkl\\nshort\\n-----END KEY-----\" }");

        _ = _secrets.UserSecrets("my-id")["Key"];

        SecretValues.Redact("line: abcdefghijkl").ShouldBe("line: [REDACTED]");
        SecretValues.Redact("-----END KEY-----").ShouldBe("[REDACTED]");
        SecretValues.Redact("short").ShouldBe("short");
    }

    [Fact]
    public void UserSecrets_ProjectFile_ReadsUserSecretsId()
    {
        WriteStore("guid-1234", """{ "K": "value-from-project" }""");
        Directory.CreateDirectory(Path.Combine(_dir, "src/App"));
        File.WriteAllText(Path.Combine(_dir, "src/App/App.csproj"),
            "<Project><PropertyGroup><UserSecretsId>guid-1234</UserSecretsId></PropertyGroup></Project>");

        _secrets.UserSecrets("src/App/App.csproj")["K"].ShouldBe("value-from-project");
        _secrets.UserSecrets("src/App")["K"].ShouldBe("value-from-project");
    }

    [Fact]
    public void UserSecrets_FolderWithTwoProjects_Fails()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "two"));
        File.WriteAllText(Path.Combine(_dir, "two/A.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_dir, "two/B.csproj"), "<Project />");

        Should.Throw<InvalidOperationException>(() => _secrets.UserSecrets("two")).Message.ShouldContain("exactly one");
    }

    [Fact]
    public void UserSecrets_ProjectWithoutId_Fails()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");

        Should.Throw<InvalidOperationException>(() => _secrets.UserSecrets("App.csproj")).Message.ShouldContain("UserSecretsId");
    }

    [Fact]
    public void UserSecrets_MissingStore_Fails()
    {
        Should.Throw<InvalidOperationException>(() => _secrets.UserSecrets("nope")).Message.ShouldContain("not found");
    }

    [Fact]
    public void Indexer_MissingKey_NamesKeyButNeverValues()
    {
        WriteStore("my-id", """{ "Present": "do-not-leak-this" }""");
        var set = _secrets.UserSecrets("my-id");

        var error = Should.Throw<InvalidOperationException>(() => set["Absent"]);

        error.Message.ShouldContain("Absent");
        error.Message.ShouldNotContain("do-not-leak-this");
    }

    [Fact]
    public void TryGet_ReportsPresence()
    {
        WriteStore("my-id", """{ "Present": "present-value" }""");
        var set = _secrets.UserSecrets("my-id");

        set.TryGet("Present", out var value).ShouldBeTrue();
        value.ShouldBe("present-value");
        set.TryGet("Absent", out var none).ShouldBeFalse();
        none.ShouldBeNull();
    }

    [Fact]
    public void EnvFile_ReadsOnlyNamedKeys_AndRegistersThem()
    {
        File.WriteAllText(Path.Combine(_dir, ".env.test"), "CF_TOKEN=\"cf-token-value\"\nOTHER=other-secret-value\nexport CF_ACCOUNT=acct-value-123\n");

        var set = _secrets.EnvFile(".env.test", "CF_TOKEN", "CF_ACCOUNT");

        set["CF_TOKEN"].ShouldBe("cf-token-value");
        set["CF_ACCOUNT"].ShouldBe("acct-value-123");
        set.TryGet("OTHER", out _).ShouldBeFalse();
        SecretValues.Redact("cf-token-value acct-value-123 other-secret-value")
            .ShouldBe("[REDACTED] [REDACTED] other-secret-value");
    }

    [Fact]
    public void EnvFile_DoesNotTouchProcessEnvironmentOrTrackedKeys()
    {
        var tracked = Environment.GetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar);
        File.WriteAllText(Path.Combine(_dir, ".env.test"), "ANDO_TEST_SCOPED_KEY_XYZ=scoped-value-1\n");

        _secrets.EnvFile(".env.test", "ANDO_TEST_SCOPED_KEY_XYZ");

        Environment.GetEnvironmentVariable("ANDO_TEST_SCOPED_KEY_XYZ").ShouldBeNull();
        Environment.GetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar).ShouldBe(tracked);
    }

    [Fact]
    public void EnvFile_MissingKey_Fails()
    {
        File.WriteAllText(Path.Combine(_dir, ".env.test"), "A=a-value-123\n");

        var error = Should.Throw<InvalidOperationException>(() => _secrets.EnvFile(".env.test", "A", "B"));

        error.Message.ShouldContain("'B'");
        error.Message.ShouldNotContain("a-value-123");
    }

    [Fact]
    public void EnvFile_AutoLoadedFile_WarnsUnlessNoEnv()
    {
        File.WriteAllText(Path.Combine(_dir, ".env.ando"), "CF_TOKEN=cf-token-value\n");
        var logger = new TestLogger();

        new SecretsOperations(new RunSession { InvocationRoot = _dir }, _store, logger).EnvFile(".env.ando", "CF_TOKEN");
        logger.WarningMessages.ShouldContain(m => m.Contains("forwarded") && m.Contains(".env.ando"));

        var quiet = new TestLogger();
        new SecretsOperations(new RunSession { InvocationRoot = _dir, NoEnv = true }, _store, quiet).EnvFile(".env.ando", "CF_TOKEN");
        quiet.WarningMessages.ShouldBeEmpty();

        File.WriteAllText(Path.Combine(_dir, ".env.secrets"), "CF_TOKEN=cf-token-value\n");
        var other = new TestLogger();
        new SecretsOperations(new RunSession { InvocationRoot = _dir }, _store, other).EnvFile(".env.secrets", "CF_TOKEN");
        other.WarningMessages.ShouldBeEmpty();
    }

    [Fact]
    public void EnvFile_InGitRepoAndNotIgnored_Fails_ButIgnoredPasses()
    {
        var git = (string args) =>
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", args) { WorkingDirectory = _dir, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(info)!;
            p.WaitForExit();
            return p.ExitCode;
        };
        git("init -q").ShouldBe(0);
        File.WriteAllText(Path.Combine(_dir, ".env.secrets"), "CF_TOKEN=cf-token-value\n");

        var error = Should.Throw<InvalidOperationException>(() => _secrets.EnvFile(".env.secrets", "CF_TOKEN"));
        error.Message.ShouldContain("not gitignored");
        error.Message.ShouldNotContain("cf-token-value");

        File.WriteAllText(Path.Combine(_dir, ".gitignore"), ".env.secrets\n");
        _secrets.EnvFile(".env.secrets", "CF_TOKEN")["CF_TOKEN"].ShouldBe("cf-token-value");
    }

    [Fact]
    public void EnvFile_MissingFile_Fails()
    {
        Should.Throw<InvalidOperationException>(() => _secrets.EnvFile("missing.env", "A"));
    }

    // ---- Env() ----------------------------------------------------------------

    private static ScriptGlobals CreateGlobals() => new(new BuildContext("/test/root", new TestLogger()));

    [Fact]
    public void Env_SecretLookingName_RegistersValue()
    {
        Environment.SetEnvironmentVariable("ANDO_TEST_SOMETHING_TOKEN", "token-value-abc");
        try
        {
            CreateGlobals().Env("ANDO_TEST_SOMETHING_TOKEN").ShouldBe("token-value-abc");
            SecretValues.Redact("a token-value-abc b").ShouldBe("a [REDACTED] b");
        }
        finally { Environment.SetEnvironmentVariable("ANDO_TEST_SOMETHING_TOKEN", null); }
    }

    [Fact]
    public void Env_PlainName_IsNotRegisteredByDefault_ButIsWithSecretTrue()
    {
        Environment.SetEnvironmentVariable("ANDO_TEST_ACCOUNT_ID", "account-value-abc");
        try
        {
            var globals = CreateGlobals();

            globals.Env("ANDO_TEST_ACCOUNT_ID");
            SecretValues.Redact("account-value-abc").ShouldBe("account-value-abc");

            globals.Env("ANDO_TEST_ACCOUNT_ID", secret: true);
            SecretValues.Redact("account-value-abc").ShouldBe("[REDACTED]");
        }
        finally { Environment.SetEnvironmentVariable("ANDO_TEST_ACCOUNT_ID", null); }
    }

    [Fact]
    public void Env_NotRequiredAndMissing_ReturnsNull()
    {
        CreateGlobals().Env("ANDO_TEST_NOPE_TOKEN", required: false, secret: true).ShouldBeNull();
    }
}
