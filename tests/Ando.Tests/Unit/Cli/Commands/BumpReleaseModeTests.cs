// =============================================================================
// BumpReleaseModeTests.cs
//
// Tests for the opt-in, AI-free release mode of 'ando bump' (versionFiles,
// year-minor-patch scheme, template changelog, --suggest, --dry-run, --tag).
// They run against real git repositories in a temp directory.
// =============================================================================

using System.Diagnostics;
using Ando.Cli.Commands;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Ando.Versioning;
using Shouldly;

namespace Ando.Tests.Unit.Cli.Commands;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class BumpReleaseModeTests : IDisposable
{
    private const string Config = """
        {"versionFiles":["Directory.Build.props"],"versionScheme":"year-minor-patch","changelog":"template","changelogPaths":["src","services","infra"],"commitMessage":"chore(release): {version}","tagPrefix":"v","tagPattern":"v*"}
        """;

    private readonly string _dir;
    private readonly string _originalDir;
    private readonly TestLogger _logger = new();
    private readonly SpyRunner _runner = new();
    private readonly List<string> _shas = [];

    public BumpReleaseModeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"bump-release-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.SetCurrentDirectory(_originalDir); } catch (DirectoryNotFoundException) { }
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Spy that runs real processes but records them and refuses to start Claude.
    private sealed class SpyRunner : CliProcessRunner
    {
        public List<string> Started { get; } = [];
        public bool ClaudeRequested { get; private set; }

        public override Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin = null,
            int timeoutMs = 60000, string? workingDirectory = null, bool streamOutput = false)
        {
            Started.Add(fileName);
            return base.RunAsync(fileName, arguments, stdin, timeoutMs, workingDirectory, streamOutput);
        }

        public override Task<string> RunClaudeAsync(string prompt, int timeoutMs = 120000, bool streamOutput = false)
        {
            ClaudeRequested = true;
            throw new InvalidOperationException("Claude must not be started.");
        }
    }

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    private static readonly DateTime Nov2026 = new(2026, 11, 10);

    private string Git(string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        p.ExitCode.ShouldBe(0, $"git {args}: {error}");
        return output;
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void CommitFile(string path, string content, string subject)
    {
        Write(path, content);
        Git($"add -- {path}");
        Git($"commit -q -m \"{subject}\"");
        _shas.Add(Git("rev-parse --short=7 HEAD").Trim());
    }

    private string Sha(int index) => _shas[index];

    // Creates the SimpleSoftware365-style repo: tag v2026.13.1 then six commits.
    private void CreateRepo(bool withFeat = true, string config = Config)
    {
        Git("init -q -b main");
        Git("config user.email test@example.com");
        Git("config user.name Test");
        Git("config commit.gpgsign false");
        Git("config tag.gpgsign false");
        Write("Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <Version>2026.13.1</Version>\n  </PropertyGroup>\n</Project>\n");
        Write("CHANGELOG.md", "# Changelog\n\n## 2026.13.1 (2026-10-01)\n\n- old entry\n");
        Write("ando.config", config);
        Git("add -A");
        Git("commit -q -m initial");
        Git("tag -a v2026.13.1 -m v2026.13.1");

        CommitFile("src/fix.txt", "1", "fix(ui): repair button");                       // 0
        if (withFeat) CommitFile("src/feat.txt", "1", "feat(api): add widget");          // 1
        else CommitFile("src/other.txt", "1", "style: format code");                     // 1
        CommitFile("docs/guide.md", "1", "docs: update guide");                          // 2 (outside changelogPaths)
        CommitFile("src/tidy.txt", "1", "refactor: tidy");                               // 3
        CommitFile("infra/release.txt", "1", "chore(release): 2026.13.2");               // 4 (skipped)
        CommitFile("services/fast.txt", "1", "perf(core)!: faster");                     // 5
    }

    private BumpCommand Command(DateTime? now = null) => new(_runner, _logger, new FixedClock(now ?? Nov2026));

    private static BumpOptions Template(bool noCommit = false) => new() { Changelog = "template", NoCommit = noCommit };

    private string[] Status() => Git("status --porcelain").Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private string ReadVersion() => new VersionReader().ReadVersion(
        Path.Combine(_dir, "Directory.Build.props"), ProjectDetector.ProjectType.Dotnet)!;

    private string CaptureConsole(Func<Task> action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { action().GetAwaiter().GetResult(); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    private void AssertNoClaude()
    {
        _runner.ClaudeRequested.ShouldBeFalse();
        _runner.Started.ShouldNotContain("claude");
    }

    private string ExpectedChangelog() =>
        "# Changelog\n\n" +
        "## 2026.14.0 (2026-11-10)\n\n" +
        "### Features\n" +
        $"- feat(api): add widget ({Sha(1)})\n\n" +
        "### Fixes\n" +
        $"- fix(ui): repair button ({Sha(0)})\n\n" +
        "### Other\n" +
        $"- perf(core)!: faster ({Sha(5)})\n" +
        $"- refactor: tidy ({Sha(3)})\n\n" +
        "<!-- edit before committing -->\n\n" +
        "## 2026.13.1 (2026-10-01)\n\n- old entry\n";

    [Fact]
    public async Task TemplateNoCommit_ChangesExactlyVersionFileAndChangelog()
    {
        CreateRepo();

        var result = await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true));

        result.ShouldBe(0);
        Status().OrderBy(l => l).ToArray().ShouldBe([" M CHANGELOG.md", " M Directory.Build.props"]);
        ReadVersion().ShouldBe("2026.14.0");
        File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md")).ShouldBe(ExpectedChangelog());
        AssertNoClaude();
    }

    [Fact]
    public async Task SecondRunOnDirtyTree_Refuses()
    {
        CreateRepo();
        await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true));

        var result = await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true));

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("uncommitted changes"));
        ReadVersion().ShouldBe("2026.14.0");
        AssertNoClaude();
    }

    [Fact]
    public async Task AndoLogFile_DoesNotCountAsDirty()
    {
        CreateRepo();
        Write("build.csando.log", "log");

        var result = await Command().ExecuteAsync(BumpType.Minor, options: Template());

        result.ShouldBe(0);
        Git("show --name-only --format= HEAD").ShouldNotContain("build.csando.log");
    }

    [Fact]
    public async Task RequireClean_RefusesDirtyTreeWithoutRunningCommit()
    {
        CreateRepo(config: """{"versionFiles":["Directory.Build.props"],"requireClean":true,"allowClaude":true}""");
        Write("stray.txt", "x");

        var result = await Command().ExecuteAsync(BumpType.Patch, autoConfirm: true, options: new BumpOptions { NoCommit = true });

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("uncommitted changes"));
        AssertNoClaude();
    }

    [Theory]
    [InlineData(BumpType.Minor)]
    [InlineData(BumpType.Patch)]
    public async Task YearRollover_GivesFirstMinorOfNewYear(BumpType type)
    {
        CreateRepo();

        var result = await Command(new DateTime(2027, 1, 5)).ExecuteAsync(type, options: Template(noCommit: true));

        result.ShouldBe(0);
        ReadVersion().ShouldBe("2027.1.0");
        File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md")).ShouldContain("## 2027.1.0 (2027-01-05)");
    }

    [Fact]
    public async Task SameYear_PatchBumpsPatch()
    {
        CreateRepo();

        (await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true))).ShouldBe(0);

        ReadVersion().ShouldBe("2026.13.2");
    }

    [Fact]
    public async Task Major_IsRejectedInYearScheme()
    {
        CreateRepo();

        var result = await Command().ExecuteAsync(BumpType.Major, options: Template(noCommit: true));

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("major bump is not supported"));
        Status().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026.13.1")]
    [InlineData("2026.12.0")]
    [InlineData("2026.14")]
    [InlineData("v2026.14.0")]
    [InlineData("1999.1.0")]
    [InlineData("2099.1.0")]
    [InlineData("abc")]
    public async Task Set_RejectsInvalidYearVersions(string version)
    {
        CreateRepo();

        var result = await Command().ExecuteAsync(options: new BumpOptions { Changelog = "none", SetVersion = version, NoCommit = true });

        result.ShouldBe(1);
        Status().ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_AcceptsGreaterYearVersion_AndNoneModeLeavesChangelogAlone()
    {
        CreateRepo();

        var result = await Command().ExecuteAsync(options: new BumpOptions { Changelog = "none", SetVersion = "2026.20.3", NoCommit = true });

        result.ShouldBe(0);
        ReadVersion().ShouldBe("2026.20.3");
        Status().ShouldBe([" M Directory.Build.props"]);
        AssertNoClaude();
    }

    [Fact]
    public async Task Set_WorksInSemverScheme()
    {
        CreateRepo(config: """{"versionFiles":["Directory.Build.props"]}""");
        Write("Directory.Build.props", "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        Git("commit -q -am version");

        (await Command().ExecuteAsync(options: new BumpOptions { Changelog = "none", SetVersion = "3.0.0", NoCommit = true })).ShouldBe(0);
        ReadVersion().ShouldBe("3.0.0");
        Git("checkout -q -- .");
        (await Command().ExecuteAsync(options: new BumpOptions { Changelog = "none", SetVersion = "nope", NoCommit = true })).ShouldBe(1);
    }

    [Fact]
    public void Suggest_PrintsMinorWhenFeatExists()
    {
        CreateRepo();

        var output = CaptureConsole(async () => (await Command().ExecuteAsync(options: new BumpOptions { Suggest = true })).ShouldBe(0));

        output.TrimEnd().Split('\n')[^1].Trim().ShouldBe("minor");
        output.ShouldContain($"{Sha(1)} feat(api): add widget");
        output.ShouldNotContain("docs: update guide");
        output.ShouldNotContain("chore(release)");
        Status().ShouldBeEmpty();
        AssertNoClaude();
    }

    [Fact]
    public void Suggest_PrintsPatchWithoutFeat()
    {
        CreateRepo(withFeat: false);

        var output = CaptureConsole(async () => (await Command().ExecuteAsync(options: new BumpOptions { Suggest = true })).ShouldBe(0));

        output.TrimEnd().Split('\n')[^1].Trim().ShouldBe("patch");
    }

    [Fact]
    public void DryRun_ChangesNothingAndPrintsPlan()
    {
        CreateRepo();

        var output = CaptureConsole(async () =>
            (await Command().ExecuteAsync(BumpType.Minor, options: new BumpOptions { Changelog = "template", DryRun = true, Tag = true })).ShouldBe(0));

        Status().ShouldBeEmpty();
        ReadVersion().ShouldBe("2026.13.1");
        output.ShouldContain("Directory.Build.props");
        output.ShouldContain("- feat(api): add widget");
        output.ShouldContain("Commit message: chore(release): 2026.14.0");
        output.ShouldContain("Tag: v2026.14.0");
        AssertNoClaude();
    }

    [Fact]
    public async Task Committed_ContainsOnlyBumpFiles_WithTag()
    {
        CreateRepo();

        var result = await Command().ExecuteAsync(BumpType.Minor,
            options: new BumpOptions { Changelog = "template", Tag = true });

        result.ShouldBe(0);
        Git("show --name-only --format=%s HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(["chore(release): 2026.14.0", "CHANGELOG.md", "Directory.Build.props"]);
        Status().ShouldBeEmpty();
        Git("cat-file -t v2026.14.0").Trim().ShouldBe("tag");
        Git("rev-parse v2026.14.0^{commit}").ShouldBe(Git("rev-parse HEAD"));
        AssertNoClaude();
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("HEAD~1")]
    public async Task Tag_AlreadyExisting_IsRefusedBeforeWritingAnything(string target)
    {
        CreateRepo();
        Git($"tag -a v2026.14.0 -m existing {target}");

        var result = await Command().ExecuteAsync(BumpType.Minor, options: new BumpOptions { Changelog = "template", Tag = true });

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Tag v2026.14.0 already exists"));
        Status().ShouldBeEmpty();
        ReadVersion().ShouldBe("2026.13.1");
    }

    [Fact]
    public async Task TagWithNoCommit_IsAnError()
    {
        CreateRepo();

        var result = await Command().ExecuteAsync(BumpType.Minor, options: new BumpOptions { Changelog = "template", Tag = true, NoCommit = true });

        result.ShouldBe(1);
        Status().ShouldBeEmpty();
    }

    [Fact]
    public async Task VersionFile_WithoutVersionElement_FailsNamingFile()
    {
        CreateRepo(config: """{"versionFiles":["Directory.Build.props","Other.props"]}""");
        Write("Other.props", "<Project/>");
        Git("add -A");
        Git("commit -q -m other");

        var result = await Command().ExecuteAsync(options: Template(noCommit: true));

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Other.props"));
    }

    [Fact]
    public async Task VersionFiles_WithDifferentVersions_Fail()
    {
        CreateRepo(config: """{"versionFiles":["Directory.Build.props","package.json"]}""");
        Write("package.json", "{\n  \"version\": \"2026.12.0\"\n}\n");
        Git("add -A");
        Git("commit -q -m pkg");

        var result = await Command().ExecuteAsync(options: Template(noCommit: true));

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("different versions"));
    }

    [Fact]
    public async Task VersionFiles_UpdatesPackageJsonToo()
    {
        CreateRepo(config: Config.Replace("[\"Directory.Build.props\"]", "[\"Directory.Build.props\",\"package.json\"]"));
        Write("package.json", "{\n  \"name\": \"x\",\n  \"version\": \"2026.13.1\"\n}\n");
        Git("add -A");
        Git("commit -q -m pkg");

        var result = await Command().ExecuteAsync(BumpType.Patch, options: Template());

        result.ShouldBe(0);
        File.ReadAllText(Path.Combine(_dir, "package.json")).ShouldContain("\"version\": \"2026.13.2\"");
        Git("show --name-only --format= HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries).Order()
            .ToArray().ShouldBe(["CHANGELOG.md", "Directory.Build.props", "package.json"]);
    }

    [Fact]
    public async Task Template_CreatesChangelogWhenMissing_AndUsesAllHistoryWithoutTag()
    {
        CreateRepo();
        Git("tag -d v2026.13.1");
        File.Delete(Path.Combine(_dir, "CHANGELOG.md"));
        Git("commit -q -am \"chore: drop changelog\"");

        (await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true))).ShouldBe(0);

        var text = File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md"));
        text.ShouldStartWith("# Changelog\n\n## 2026.14.0 (2026-11-10)\n\n### Features\n");
    }

    [Fact]
    public void Insert_AddsTitleWhenMissing_AndKeepsFrontmatterlessBody()
    {
        ChangelogGenerator.Insert("some text\n", "## 1.0.0 (2026-01-01)\n\n")
            .ShouldBe("# Changelog\n\nsome text\n\n## 1.0.0 (2026-01-01)\n");
        ChangelogGenerator.Insert("# Log\nbody\n", "## 1.0.0 (2026-01-01)\n\n")
            .ShouldBe("# Log\nbody\n\n## 1.0.0 (2026-01-01)\n");
    }

    [Theory]
    [InlineData("1.2.3", BumpType.Minor, "1.3.0")]
    [InlineData("2026.13.1", BumpType.Minor, "2026.14.0")]
    public void Next_SemverSchemeIgnoresYear(string current, BumpType type, string expected)
    {
        VersionCalculator.Next(current, type, VersionCalculator.SemVerScheme, Nov2026).ShouldBe(expected);
    }

    [Fact]
    public void Parse_RejectsUnknownFlagsAndBadCombinations()
    {
        BumpOptions.TryParse(["minor", "--bogus"], out _, out _).ShouldNotBeNull().ShouldContain("Unknown option");
        BumpOptions.TryParse(["--tag", "--no-commit"], out _, out _).ShouldNotBeNull().ShouldContain("--tag");
        BumpOptions.TryParse(["--changelog", "ai"], out _, out _).ShouldNotBeNull().ShouldContain("--changelog");
        BumpOptions.TryParse(["--set"], out _, out _).ShouldNotBeNull().ShouldContain("needs a value");
        BumpOptions.TryParse(["huge"], out _, out _).ShouldNotBeNull().ShouldContain("Invalid bump type");

        BumpOptions.TryParse(["minor", "--changelog", "template", "--set", "1.0.0", "--suggest", "--dry-run", "--no-commit"],
            out var type, out var options).ShouldBeNull();
        type.ShouldBe(BumpType.Minor);
        options.Changelog.ShouldBe("template");
        options.SetVersion.ShouldBe("1.0.0");
        options.Suggest.ShouldBeTrue();
        options.DryRun.ShouldBeTrue();
        options.NoCommit.ShouldBeTrue();
    }

    // ---- review fixes ----

    private void WriteProps(string content)
    {
        File.WriteAllText(Path.Combine(_dir, "Directory.Build.props"), content);
        Git("commit -q -am props");
    }

    private string PropsText() => File.ReadAllText(Path.Combine(_dir, "Directory.Build.props"));

    [Fact]
    public async Task VersionFiles_KeepPackageReferenceVersionChildren()
    {
        CreateRepo();
        WriteProps("<Project>\n  <ItemGroup>\n    <PackageReference Include=\"X\"><Version>9.9.9</Version></PackageReference>\n  </ItemGroup>\n" +
                   "  <PropertyGroup>\n    <Version>2026.13.1</Version>\n  </PropertyGroup>\n</Project>\n");

        (await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true))).ShouldBe(0);

        PropsText().ShouldBe("<Project>\n  <ItemGroup>\n    <PackageReference Include=\"X\"><Version>9.9.9</Version></PackageReference>\n  </ItemGroup>\n" +
                             "  <PropertyGroup>\n    <Version>2026.14.0</Version>\n  </PropertyGroup>\n</Project>\n");
    }

    [Fact]
    public async Task VersionFiles_VersionWithConditionAttribute_IsUpdated()
    {
        CreateRepo();
        WriteProps("<Project>\n  <PropertyGroup>\n    <Version Condition=\"'$(X)' == ''\">2026.13.1</Version>\n  </PropertyGroup>\n</Project>\n");

        (await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true))).ShouldBe(0);

        PropsText().ShouldContain("<Version Condition=\"'$(X)' == ''\">2026.13.2</Version>");
    }

    [Fact]
    public async Task VersionFiles_GreaterThanInsideConditionAndSpaceInEndTag_IsUpdated()
    {
        CreateRepo();
        WriteProps("<Project>\n  <PropertyGroup>\n    <Version Condition=\"'$(A)' > '0'\">2026.13.1</Version >\n  </PropertyGroup>\n</Project>\n");

        (await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true))).ShouldBe(0);

        PropsText().ShouldContain("<Version Condition=\"'$(A)' > '0'\">2026.13.2</Version >");
    }

    [Fact]
    public async Task VersionFiles_NonUtf8File_IsRefusedWithoutRewriting()
    {
        CreateRepo();
        var path = Path.Combine(_dir, "Directory.Build.props");
        var bytes = System.Text.Encoding.ASCII.GetBytes("<Project>\n  <!-- caf_ -->\n  <PropertyGroup>\n    <Version>2026.13.1</Version>\n  </PropertyGroup>\n</Project>\n");
        bytes[Array.IndexOf(bytes, (byte)'_')] = 0xE9; // Windows-1252 "é"
        File.WriteAllBytes(path, bytes);
        Git("commit -q -am latin1");

        (await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true))).ShouldNotBe(0);

        File.ReadAllBytes(path).ShouldBe(bytes);
    }

    [Fact]
    public async Task CommitMessage_OnlyVersionPlaceholder_IsRejected()
    {
        CreateRepo(config: Config.Replace("chore(release): {version}", "{version}"));

        (await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true))).ShouldNotBe(0);

        PropsText().ShouldContain("2026.13.1");
    }

    [Fact]
    public async Task VersionFiles_TwoPropertyGroupVersions_Refuse()
    {
        CreateRepo();
        WriteProps("<Project>\n  <PropertyGroup><Version>2026.13.1</Version></PropertyGroup>\n  <PropertyGroup><Version>2026.13.1</Version></PropertyGroup>\n</Project>\n");

        var result = await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true));

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Directory.Build.props") && m.Contains("expected exactly one"));
        Status().ShouldBeEmpty();
    }

    [Fact]
    public async Task VersionFiles_OnlyPackageReferenceVersion_Refuses()
    {
        CreateRepo();
        WriteProps("<Project><ItemGroup><PackageReference Include=\"X\"><Version>1.0.0</Version></PackageReference></ItemGroup></Project>");

        var result = await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true));

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Directory.Build.props") && m.Contains("PropertyGroup"));
    }

    [Fact]
    public async Task VersionFiles_PreserveBomAndCrlf()
    {
        CreateRepo();
        var original = "<Project>\r\n  <PropertyGroup>\r\n    <Version>2026.13.1</Version>\r\n  </PropertyGroup>\r\n</Project>\r\n";
        File.WriteAllBytes(Path.Combine(_dir, "Directory.Build.props"), [0xEF, 0xBB, 0xBF, .. new System.Text.UTF8Encoding(false).GetBytes(original)]);
        Git("commit -q -am bom");

        (await Command().ExecuteAsync(BumpType.Patch, options: Template(noCommit: true))).ShouldBe(0);

        var bytes = File.ReadAllBytes(Path.Combine(_dir, "Directory.Build.props"));
        bytes[..3].ShouldBe([0xEF, 0xBB, 0xBF]);
        System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).ShouldBe(original.Replace("2026.13.1", "2026.13.2"));
    }

    [Fact]
    public async Task VersionFileProblem_FailsBeforeDirtyCheck()
    {
        CreateRepo(config: """{"versionFiles":["Missing.props"],"changelog":"template"}""");
        Write("stray.txt", "x");

        var result = await Command().ExecuteAsync(BumpType.Patch);

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Missing.props"));
        _logger.ErrorMessages.ShouldNotContain(m => m.Contains("uncommitted"));
    }

    [Fact]
    public async Task DryRun_InClaudeMode_DoesNotAskForClaudePermission()
    {
        CreateRepo(config: """{"versionFiles":["Directory.Build.props"]}""");

        var result = await Command().ExecuteAsync(BumpType.Patch, options: new BumpOptions { DryRun = true });

        result.ShouldBe(0);
        Status().ShouldBeEmpty();
        AssertNoClaude();
    }

    [Fact]
    public void RenderSection_UsesInvariantGregorianDate_UnderThaiCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            ChangelogGenerator.RenderSection("1.0.0", new DateTime(2026, 11, 10), [])
                .ShouldStartWith("## 1.0.0 (2026-11-10)");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Quote_RejectsBackslash()
    {
        var git = new CliGitOperations(_runner);
        Should.Throw<ArgumentException>(() => git.GetLastTagMatchingAsync("v\\*").GetAwaiter().GetResult())
            .Message.ShouldContain("backslash");
    }

    [Fact]
    public void Filter_SkipsPreviousReleaseCommitFromConfiguredMessage()
    {
        (string, string)[] commits =
        [
            ("aaaaaaa", "Release 2026.13.2 now"), ("bbbbbbb", "Release notes: draft"), ("ccccccc", "fix: x.y"),
            ("ddddddd", "chore(release): 2026.13.2")
        ];

        ChangelogGenerator.Filter(commits, "Release {version} now").Select(c => c.Item1).ToArray()
            .ShouldBe(["bbbbbbb", "ccccccc"]);
        ChangelogGenerator.Filter(commits).Select(c => c.Item1).ToArray().ShouldBe(["aaaaaaa", "bbbbbbb", "ccccccc"]);
    }

    [Fact]
    public async Task Template_PreviousReleaseCommitWithCustomMessage_IsNotListed()
    {
        CreateRepo(config: Config.Replace("chore(release): {version}", "Ship {version}"));
        CommitFile("src/rel.txt", "1", "Ship 2026.13.2");

        (await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true))).ShouldBe(0);

        File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md")).ShouldNotContain("Ship 2026.13.2");
    }

    [Fact]
    public void Insert_PlacesSectionAfterFrontmatter_AndAfterTitleIfPresent()
    {
        const string section = "## 1.0.0 (2026-01-01)\n\n";

        ChangelogGenerator.Insert("---\ntitle: Changelog\n---\n\n## 0.9.0\n\n- old\n", section)
            .ShouldBe("---\ntitle: Changelog\n---\n\n" + section + "## 0.9.0\n\n- old\n");
        ChangelogGenerator.Insert("---\ntitle: x\n---\n# Log\n\n## 0.9.0\n", section)
            .ShouldBe("---\ntitle: x\n---\n# Log\n\n" + section + "## 0.9.0\n");
        ChangelogGenerator.Insert("---\r\ntitle: x\r\n---\r\n## 0.9.0\r\n", section)
            .ShouldBe("---\r\ntitle: x\r\n---\r\n\r\n" + section.Replace("\n", "\r\n") + "## 0.9.0\r\n");
        // A horizontal rule that is not frontmatter keeps the old behavior.
        ChangelogGenerator.Insert("intro\n---\n", section).ShouldBe("# Changelog\n\nintro\n---\n\n" + section.TrimEnd('\n') + "\n");
    }

    [Theory]
    [InlineData("""{"versionFiles":["Directory.Build.props"],"changelog":"template"}""")]
    [InlineData("""{"versionFiles":["Directory.Build.props"],"requireClean":true}""")]
    public async Task Release_InSafeMode_RefusesDirtyTreeBeforeAnyStep(string config)
    {
        CreateRepo(config: config);
        Write("stray.txt", "x");

        foreach (var dryRun in new[] { true, false })
        {
            var result = await new ReleaseCommand(_runner, _logger).ExecuteAsync(all: true, dryRun: dryRun, commandName: "ship", includePublish: false);

            result.ShouldBe(1);
        }

        _logger.ErrorMessages.ShouldContain(m => m.Contains("uncommitted changes. Commit or stash them first"));
        _runner.Started.ShouldNotContain("ando");
        AssertNoClaude();
    }

    [Fact]
    public async Task Release_InSafeMode_CleanTreeDryRunShowsYearSchemeVersion()
    {
        CreateRepo();
        Write("build.csando.log", "log");
        var writer = new StringWriter();
        var originalConsole = Spectre.Console.AnsiConsole.Console;
        Spectre.Console.AnsiConsole.Console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
        {
            Out = new Spectre.Console.AnsiConsoleOutput(writer),
            Ansi = Spectre.Console.AnsiSupport.No
        });
        int result;
        try
        {
            result = await new ReleaseCommand(_runner, _logger).ExecuteAsync(all: true, dryRun: true, bumpType: BumpType.Minor, commandName: "ship", includePublish: false);
        }
        finally { Spectre.Console.AnsiConsole.Console = originalConsole; }

        result.ShouldBe(0);
        writer.ToString().ShouldContain("2026.13.1 → 2026.14.0");
        AssertNoClaude();
    }

    // ---- Placement, Unreleased conversion and pathspecs -----------------------------------

    private static readonly (string Sha, string Subject)[] TwoCommits =
        [("abc1234", "feat: a"), ("def5678", "fix: b")];

    private const string Generated =
        "### Features\n- feat: a (abc1234)\n\n### Fixes\n- fix: b (def5678)\n\n<!-- edit before committing -->\n\n";

    private const string ReleaseHeading = "## 2026.14.0 (2026-11-10)\n\n";

    private static ChangelogGenerator.Entry PlanFor(string existing, string? heading = null, bool keep = false,
        (string Sha, string Subject)[]? commits = null) =>
        ChangelogGenerator.Plan(existing, "2026.14.0", Nov2026, commits ?? TwoCommits, null, heading, keep);

    [Fact]
    public void Plan_IntroParagraphStaysAboveNewSection()
    {
        var result = PlanFor("# Changelog\n\nIntro line one.\n\nIntro line two.\n\n## 2026.13.1 (2026-10-01)\n\n- old\n");

        result.Content.ShouldBe("# Changelog\n\nIntro line one.\n\nIntro line two.\n\n" + ReleaseHeading + Generated +
                                "## 2026.13.1 (2026-10-01)\n\n- old\n");
        result.Section.ShouldBe(ReleaseHeading + Generated);
    }

    [Fact]
    public void Plan_InsertsBeforeFirstLevel2Heading_NotBeforeLevel3()
    {
        var result = PlanFor("# Changelog\n\n### Not a release\n\n## 1.0.0 (2026-01-01)\n\n## 0.9.0\n");

        result.Content.ShouldBe("# Changelog\n\n### Not a release\n\n" + ReleaseHeading + Generated +
                                "## 1.0.0 (2026-01-01)\n\n## 0.9.0\n");
    }

    [Fact]
    public void Plan_WithoutLevel2Heading_AppendsAfterIntro()
    {
        PlanFor("# Changelog\n\nOnly an intro.\n").Content
            .ShouldBe("# Changelog\n\nOnly an intro.\n\n" + ReleaseHeading + Generated.TrimEnd('\n') + "\n");
        PlanFor("# Changelog\n").Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + Generated.TrimEnd('\n') + "\n");
    }

    [Fact]
    public void Plan_IgnoresLevel2HeadingsInsideCodeFences()
    {
        const string intro = "# Changelog\n\nFormat:\n\n```md\n## Unreleased\n\n- example\n```\n\n";

        PlanFor(intro + "## 1.0.0 (2026-01-01)\n").Content
            .ShouldBe(intro + ReleaseHeading + Generated + "## 1.0.0 (2026-01-01)\n");
        PlanFor(intro).Content.ShouldBe(intro + ReleaseHeading + Generated.TrimEnd('\n') + "\n");
    }

    [Fact]
    public void Plan_ConvertsUnreleasedWithHandWrittenLines()
    {
        var result = PlanFor("# Changelog\n\nIntro.\n\n## Unreleased\n\n- hand one\n- hand two\n\n\n## 2026.13.1 (2026-10-01)\n\n- old\n");

        result.Section.ShouldBe(ReleaseHeading + "- hand one\n- hand two\n\n" + Generated);
        result.Content.ShouldBe("# Changelog\n\nIntro.\n\n" + ReleaseHeading + "- hand one\n- hand two\n\n" + Generated +
                                "## 2026.13.1 (2026-10-01)\n\n- old\n");
    }

    [Fact]
    public void Plan_ConvertsBracketedAndCaseInsensitiveUnreleased()
    {
        PlanFor("# Changelog\n\n## [Unreleased]\n\n- hand\n\n## 1.0.0\n").Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + "- hand\n\n" + Generated + "## 1.0.0\n");
        PlanFor("# Changelog\n\n##   UNRELEASED  \n- hand\n\n## 1.0.0\n").Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + "- hand\n\n" + Generated + "## 1.0.0\n");
    }

    [Fact]
    public void Plan_UnreleasedWithoutCommits_OmitsGeneratedGroups()
    {
        PlanFor("# Changelog\n\n## Unreleased\n\n- hand\n\n## 1.0.0\n", commits: []).Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + "- hand\n\n<!-- edit before committing -->\n\n## 1.0.0\n");
    }

    [Fact]
    public void Plan_KeepUnreleased_AddsEmptyHeadingAboveRelease()
    {
        var result = PlanFor("# Changelog\n\nIntro.\n\n## Unreleased\n\n- hand one\n\n## 2026.13.1 (2026-10-01)\n\n- old\n", keep: true);

        result.Section.ShouldBe("## Unreleased\n\n" + ReleaseHeading + "- hand one\n\n" + Generated);
        result.Content.ShouldBe("# Changelog\n\nIntro.\n\n## Unreleased\n\n" + ReleaseHeading + "- hand one\n\n" + Generated +
                                "## 2026.13.1 (2026-10-01)\n\n- old\n");
    }

    [Fact]
    public void Plan_KeepUnreleased_PreservesBracketedHeadingAsWritten()
    {
        PlanFor("# Changelog\n\n## [Unreleased]\n\n- hand\n", keep: true).Content
            .ShouldBe("# Changelog\n\n## [Unreleased]\n\n" + ReleaseHeading + "- hand\n\n" + Generated.TrimEnd('\n') + "\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plan_EmptyUnreleasedBody_BehavesLikePlainInsertAtThatPosition(bool keep)
    {
        var result = PlanFor("# Changelog\n\n## Unreleased\n\n## 1.0.0\n\n- old\n", keep: keep);

        result.Content.ShouldBe("# Changelog\n\n" + (keep ? "## Unreleased\n\n" : "") + ReleaseHeading + Generated +
                                "## 1.0.0\n\n- old\n");
    }

    [Fact]
    public void Plan_CustomUnreleasedHeading()
    {
        const string existing = "# Changelog\n\n## Unreleased\n\n- not mine\n\n## Next release\n\n- mine\n\n## 1.0.0\n";

        PlanFor(existing, heading: "next release").Content
            .ShouldBe("# Changelog\n\n## Unreleased\n\n- not mine\n\n" + ReleaseHeading + "- mine\n\n" + Generated + "## 1.0.0\n");
        // The default heading is not matched when a custom one is configured and a different one exists.
        PlanFor("# Changelog\n\n## Unreleased\n\n- x\n\n## 1.0.0\n", heading: "Next").Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + Generated + "## Unreleased\n\n- x\n\n## 1.0.0\n");
    }

    [Fact]
    public void Plan_EmptyLevel2Heading_DoesNotCrash()
    {
        PlanFor("# Changelog\n\n## \n\n- x\n\n## 1.0.0\n").Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + Generated + "## \n\n- x\n\n## 1.0.0\n");
    }

    [Fact]
    public void Plan_HeadingsInsideLongerFenceAreIgnored()
    {
        const string fence = "````\n```\n## Unreleased\n```\n## 9.9.9\n````\n\n";

        PlanFor("# Changelog\n\n" + fence + "## 1.0.0\n").Content
            .ShouldBe("# Changelog\n\n" + fence + ReleaseHeading + Generated + "## 1.0.0\n");
    }

    [Fact]
    public void Plan_ConfiguredBracketedHeadingMatchesEitherForm()
    {
        PlanFor("# Changelog\n\n## Unreleased\n\n- hand\n\n## 1.0.0\n", heading: "[Unreleased]").Content
            .ShouldBe("# Changelog\n\n" + ReleaseHeading + "- hand\n\n" + Generated + "## 1.0.0\n");
    }

    [Fact]
    public void Plan_PreservesCrlf()
    {
        var existing = "# Changelog\r\n\r\nIntro.\r\n\r\n## Unreleased\r\n\r\n- hand\r\n\r\n## 1.0.0\r\n";

        var result = PlanFor(existing, keep: true);

        result.Content.ShouldBe(("# Changelog\n\nIntro.\n\n## Unreleased\n\n" + ReleaseHeading + "- hand\n\n" + Generated +
                                 "## 1.0.0\n").Replace("\n", "\r\n"));
        result.Content.Replace("\r\n", "").ShouldNotContain("\n");
    }

    [Fact]
    public void Plan_FrontmatterTitleAndIntro()
    {
        const string fm = "---\ntitle: Changelog\n---\n\n# Changelog\n\nIntro.\n\n";

        PlanFor(fm + "## 1.0.0\n").Content.ShouldBe(fm + ReleaseHeading + Generated + "## 1.0.0\n");
        PlanFor(fm + "## Unreleased\n\n- hand\n\n## 1.0.0\n").Content
            .ShouldBe(fm + ReleaseHeading + "- hand\n\n" + Generated + "## 1.0.0\n");
        PlanFor(fm.TrimEnd('\n') + "\n").Content
            .ShouldBe(fm + ReleaseHeading + Generated.TrimEnd('\n') + "\n");
    }

    private const string ConsumerChangelog =
        "# Changelog\n\nAll notable changes.\n\n## Unreleased\n\n- hand written line\n\n## 2026.13.1 (2026-10-01)\n\n- old entry\n";

    private const string ConsumerConfig =
        """{"versionFiles":["Directory.Build.props"],"versionScheme":"year-minor-patch","changelog":"template","changelogPaths":["src",":!src/website"],"commitMessage":"chore(release): {version}","keepUnreleased":true}""";

    private void CreateConsumerRepo()
    {
        CreateRepo(config: ConsumerConfig);
        Write("CHANGELOG.md", ConsumerChangelog);
        Git("add -A");
        Git("commit -q -m \"docs: changelog\"");
        _shas.Clear();
    }

    [Fact]
    public async Task DryRun_PrintsExactlyTheSectionThatIsWritten()
    {
        CreateConsumerRepo();

        var output = "";
        var dry = CaptureConsole(async () =>
            (await Command().ExecuteAsync(BumpType.Minor, options: new BumpOptions { Changelog = "template", DryRun = true })).ShouldBe(0));
        output = dry;
        var section = output[(output.IndexOf("Changelog entry:", StringComparison.Ordinal) + "Changelog entry:".Length)..];
        File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md")).ShouldBe(ConsumerChangelog);

        (await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true))).ShouldBe(0);
        var written = File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md"));

        var printed = section.TrimStart('\r', '\n');
        printed = printed[..(printed.IndexOf("<!-- edit before committing -->", StringComparison.Ordinal) + ChangelogGenerator.ReviewMarker.Length + 2)];
        printed.ShouldStartWith("## Unreleased\n\n## 2026.14.0 (2026-11-10)\n\n- hand written line\n\n### Features\n");
        written.ShouldContain(printed);
        written.ShouldStartWith("# Changelog\n\nAll notable changes.\n\n" + printed + "## 2026.13.1 (2026-10-01)\n\n- old entry\n");
        AssertNoClaude();
    }

    [Fact]
    public async Task ChangelogPaths_ExcludePathspecIsPassedToGit()
    {
        CreateConsumerRepo();
        CommitFile("src/website/page.txt", "1", "feat(web): website only");
        CommitFile("src/app/app.txt", "1", "fix(app): app change");

        (await Command().ExecuteAsync(BumpType.Minor, options: Template(noCommit: true))).ShouldBe(0);

        var text = File.ReadAllText(Path.Combine(_dir, "CHANGELOG.md"));
        text.ShouldContain("fix(app): app change");
        text.ShouldNotContain("website only");
    }
}
