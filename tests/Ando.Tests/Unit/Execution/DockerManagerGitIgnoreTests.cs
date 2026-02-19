using System.Diagnostics;
using Ando.Execution;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Execution;

public class DockerManagerGitIgnoreTests : IDisposable
{
    private readonly TestLogger _logger = new();
    private readonly DockerManager _dockerManager;
    private readonly string _testDir;

    public DockerManagerGitIgnoreTests()
    {
        _dockerManager = new DockerManager(_logger);
        _testDir = Path.Combine(Path.GetTempPath(), $"ando-gitignore-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            foreach (var file in Directory.GetFiles(_testDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_testDir, recursive: true);
        }
    }

    private static bool IsGitAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                ArgumentList = { "--version" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RunGit(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _testDir,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start git process");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Git command failed: {string.Join(' ', args)}");
        }
    }

    [SkippableFact]
    public async Task TryGetGitIncludedPathsAsync_NonGitDirectory_ReturnsNull()
    {
        Skip.IfNot(IsGitAvailable(), "Git is not available in this environment");

        var paths = await _dockerManager.TryGetGitIncludedPathsAsync(_testDir);

        Assert.Null(paths);
    }

    [SkippableFact]
    public async Task TryGetGitIncludedPathsAsync_RespectsGitIgnore()
    {
        Skip.IfNot(IsGitAvailable(), "Git is not available in this environment");

        RunGit("init");
        File.WriteAllText(Path.Combine(_testDir, ".gitignore"), "ignored.log\nlogs/\n");
        File.WriteAllText(Path.Combine(_testDir, "tracked.txt"), "tracked");
        File.WriteAllText(Path.Combine(_testDir, "untracked.txt"), "untracked");
        File.WriteAllText(Path.Combine(_testDir, "ignored.log"), "ignored");
        Directory.CreateDirectory(Path.Combine(_testDir, "logs"));
        File.WriteAllText(Path.Combine(_testDir, "logs", "nested.log"), "ignored");

        RunGit("add", ".gitignore", "tracked.txt");

        var paths = await _dockerManager.TryGetGitIncludedPathsAsync(_testDir);

        Assert.NotNull(paths);
        Assert.Contains(".gitignore", paths);
        Assert.Contains("tracked.txt", paths);
        Assert.Contains("untracked.txt", paths);
        Assert.DoesNotContain("ignored.log", paths);
        Assert.DoesNotContain("logs/nested.log", paths);
    }

    [Fact]
    public void TarHelpIndicatesNullSupport_WithBusyBoxOutput_ReturnsFalse()
    {
        var help = """
            BusyBox v1.37.0 (2025-12-16 14:19:28 UTC) multi-call binary.
            Usage: tar c|x|t [-ZzJjahmvokO] [-f TARFILE] [LONGOPT]... [FILE]...
            """;

        var supportsNull = DockerManager.TarHelpIndicatesNullSupport(help);

        Assert.False(supportsNull);
    }

    [Fact]
    public void TarHelpIndicatesNullSupport_WithGnuTarOutput_ReturnsTrue()
    {
        var help = """
            Usage: tar [OPTION...] [FILE]...
              --null                 -T reads null-terminated names
            """;

        var supportsNull = DockerManager.TarHelpIndicatesNullSupport(help);

        Assert.True(supportsNull);
    }
}
