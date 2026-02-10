using Ando.Scripting;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Docs;

[Trait("Category", "Unit")]
public class DocsSnippetCompilationTests
{
    private const string Marker = "ando-doc-snippet-test";

    [Fact]
    public async Task WebsiteDocs_MarkedCSharpSnippets_CompileAsBuildScripts()
    {
        var repoRoot = FindRepoRoot();
        var files = new[]
        {
            Path.Combine(repoRoot, "website", "public", "llms.txt"),
            Path.Combine(repoRoot, "website", "src", "content", "providers", "ando.md"),
        };

        var snippets = new List<(string File, int FenceStartLine, string Code)>();
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            snippets.AddRange(ExtractMarkedCSharpFences(file, content));
        }

        // Guard against the test silently doing nothing.
        snippets.Count.ShouldBeGreaterThan(0);

        var logger = new TestLogger();
        var host = new ScriptHost(logger);

        foreach (var (file, fenceStartLine, code) in snippets)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ando-doc-snippets", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "build.csando");

            try
            {
                await File.WriteAllTextAsync(scriptPath, code);
                var errors = await host.VerifyScriptAsync(scriptPath);

                if (errors.Count > 0)
                {
                    throw new Xunit.Sdk.XunitException(
                        $"Doc snippet failed to compile: {file}:{fenceStartLine}\n" +
                        string.Join("\n", errors));
                }
            }
            finally
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static IEnumerable<(string File, int FenceStartLine, string Code)> ExtractMarkedCSharpFences(string file, string content)
    {
        using var reader = new StringReader(content);

        var lineNo = 0;
        var inFence = false;
        var fenceLang = string.Empty;
        var fenceStartLine = 0;
        var codeLines = new List<string>();

        while (true)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            lineNo++;

            var trimmed = line.Trim();

            if (!inFence)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    fenceLang = trimmed[3..].Trim();
                    inFence = true;
                    fenceStartLine = lineNo;
                    codeLines.Clear();
                }

                continue;
            }

            // End fence.
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = false;

                var isCSharp =
                    fenceLang.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
                    fenceLang.Equals("cs", StringComparison.OrdinalIgnoreCase);

                if (isCSharp)
                {
                    var code = string.Join("\n", codeLines);
                    if (code.Contains(Marker, StringComparison.Ordinal))
                    {
                        yield return (file, fenceStartLine, code);
                    }
                }

                continue;
            }

            codeLines.Add(line);
        }
    }

    private static string FindRepoRoot()
    {
        // tests/Ando.Tests/bin/... -> walk up until we find Ando.sln
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "Ando.sln");
            if (File.Exists(candidate))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find repo root (Ando.sln).");
    }
}

