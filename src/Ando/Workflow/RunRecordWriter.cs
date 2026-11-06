// =============================================================================
// RunRecordWriter.cs
//
// Summary: Writes a JSON record of each `ando run` to .ando/runs/.
//
// The record lists every step that ran (including steps inside blocks, finally
// and on-failure steps) with start, end, result and error, plus the values the
// script added with Ando.Report. Deployment reports can be generated from it.
//
// Design Decisions:
// - Every string is passed through SecretValues.Redact
// - .ando/runs/ gets its own .gitignore so records never show up in git status
//   (and never trip Git.RequireClean)
// - Writing is best effort: a failure is logged, never fails the build
// =============================================================================

using System.Text.Json;
using Ando.Logging;

namespace Ando.Workflow;

/// <summary>
/// Writes the run record for one `ando run`.
/// </summary>
public static class RunRecordWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Writes .ando/runs/&lt;utc-time&gt;.json under <paramref name="invocationRoot"/>
    /// and returns its path, or null when writing failed.
    /// </summary>
    public static string? TryWrite(
        string invocationRoot,
        string scriptPath,
        IReadOnlyList<string> profiles,
        RunSession session,
        WorkflowResult result,
        DateTimeOffset startedAt,
        string andoVersion,
        IBuildLogger logger)
    {
        try
        {
            var directory = Path.Combine(invocationRoot, ".ando", "runs");
            Directory.CreateDirectory(directory);

            var gitignore = Path.Combine(directory, ".gitignore");
            if (!File.Exists(gitignore))
            {
                File.WriteAllText(gitignore, "*\n");
            }

            var record = Build(scriptPath, invocationRoot, profiles, session, result, startedAt, andoVersion);
            var json = JsonSerializer.Serialize(record, JsonOptions);
            var stamp = $"{startedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}";
            string path;
            for (var attempt = 0; ; attempt++)
            {
                path = Path.Combine(directory, attempt == 0 ? $"{stamp}.json" : $"{stamp}-{attempt}.json");
                try
                {
                    // CreateNew: two runs in the same second never overwrite each other.
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    using var writer = new StreamWriter(stream);
                    writer.Write(json);
                    break;
                }
                catch (IOException) when (File.Exists(path) && attempt < 100)
                {
                }
            }
            logger.Info($"Run record: {path}");
            return path;
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not write the run record: {ex.Message}");
            return null;
        }
    }

    internal static Dictionary<string, object?> Build(
        string scriptPath,
        string invocationRoot,
        IReadOnlyList<string> profiles,
        RunSession session,
        WorkflowResult result,
        DateTimeOffset startedAt,
        string andoVersion)
    {
        static string? R(string? value) => value == null ? null : SecretValues.Redact(value);

        return new Dictionary<string, object?>
        {
            ["andoVersion"] = andoVersion,
            ["script"] = R(Path.GetRelativePath(invocationRoot, scriptPath).Replace('\\', '/')),
            ["profiles"] = profiles,
            ["ref"] = session.Ref,
            ["startedAt"] = startedAt,
            ["endedAt"] = startedAt + result.Duration,
            ["durationSeconds"] = Math.Round(result.Duration.TotalSeconds, 1),
            ["success"] = result.Success,
            ["cancelled"] = result.WasCancelled,
            ["steps"] = result.StepResults.Select(step => new Dictionary<string, object?>
            {
                ["name"] = step.StepName,
                ["context"] = R(step.Context),
                ["depth"] = step.Depth,
                ["cleanup"] = step.Cleanup,
                ["startedAt"] = step.StartedAt,
                ["endedAt"] = step.EndedAt,
                ["durationSeconds"] = Math.Round(step.Duration.TotalSeconds, 1),
                ["success"] = step.Success,
                ["error"] = R(step.ErrorMessage)
            }).ToList(),
            ["reports"] = session.Reports
                .GroupBy(r => SecretValues.Redact(r.Key))
                .ToDictionary(g => g.Key, g => (object?)R(g.Last().Value))
        };
    }
}
