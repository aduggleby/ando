// =============================================================================
// ArtifactManifest.cs
//
// Summary: Build once, deploy the same bytes.
//
// A build run writes manifest.json into an artifacts directory with the
// version, commit and SHA-256 of every file (Ando.WriteManifest). A later
// deploy run imports that directory (Ando.ImportArtifacts): ANDO verifies every
// hash on the host while the script is evaluated, copies the directory into the
// build container and verifies again there, so a deploy refuses artifacts that
// changed after the build.
//
// Design Decisions:
// - Extra files that are not in the manifest also fail verification
// - The manifest's own file is excluded from the hashes
// - Paths use forward slashes, relative to the artifacts directory
// =============================================================================

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ando.Operations;

/// <summary>One file in an artifact manifest.</summary>
public record ArtifactFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>
/// The manifest of an artifacts directory: version, commit and every file's SHA-256.
/// </summary>
public class ArtifactManifest
{
    /// <summary>Manifest file name inside the artifacts directory.</summary>
    public const string FileName = "manifest.json";

    /// <summary>Version recorded by the build.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    /// <summary>Commit recorded by the build.</summary>
    [JsonPropertyName("commit")]
    public string Commit { get; init; } = "";

    /// <summary>When the manifest was written (UTC).</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Every file in the directory except manifest.json.</summary>
    [JsonPropertyName("files")]
    public List<ArtifactFile> Files { get; init; } = [];

    /// <summary>
    /// Container path of the imported directory (set by Ando.ImportArtifacts).
    /// </summary>
    [JsonIgnore]
    public string ContainerPath { get; internal set; } = "";

    /// <summary>
    /// Finds a file by its relative path ("documents/worker.tar.gz") or, when
    /// that is unique, by its file name ("worker.tar.gz"). Throws when not found.
    /// </summary>
    public ArtifactFile File(string pathOrName)
    {
        var normalized = pathOrName.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        var exact = Files.FirstOrDefault(f => f.Path == normalized);
        if (exact != null) return exact;

        var byName = Files.Where(f => System.IO.Path.GetFileName(f.Path) == normalized).ToList();
        return byName.Count switch
        {
            1 => byName[0],
            0 => throw new KeyNotFoundException($"No file '{pathOrName}' in the artifact manifest."),
            _ => throw new InvalidOperationException(
                $"'{pathOrName}' matches {byName.Count} files in the artifact manifest; use the relative path.")
        };
    }

    /// <summary>Container path of a file in the imported directory.</summary>
    public string PathOf(string pathOrName) => $"{ContainerPath.TrimEnd('/')}/{File(pathOrName).Path}";

    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    internal static ArtifactManifest Parse(string json) =>
        JsonSerializer.Deserialize<ArtifactManifest>(json, JsonOptions)
        ?? throw new InvalidOperationException("The artifact manifest is empty.");

    /// <summary>
    /// Parses `sha256sum` output ("hash  ./path") and `stat -c '%s %n'` output
    /// ("size ./path") from the container into manifest entries.
    /// </summary>
    internal static List<ArtifactFile> ParseListings(string sha256Output, string sizeOutput)
    {
        var sizes = new Dictionary<string, long>();
        foreach (var line in sizeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var space = line.IndexOf(' ');
            if (space > 0 && long.TryParse(line[..space], out var size))
            {
                sizes[Normalize(line[(space + 1)..])] = size;
            }
        }

        var files = new List<ArtifactFile>();
        foreach (var line in sha256Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // sha256sum prints "<hash>  <path>" (two spaces, or " *" in binary mode).
            if (line.Length < 66) continue;
            var hash = line[..64];
            var path = Normalize(line[66..]);
            files.Add(new ArtifactFile(path, sizes.GetValueOrDefault(path), hash));
        }

        return [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)];
    }

    private static string Normalize(string path)
    {
        var normalized = path.TrimEnd('\r').Replace('\\', '/');
        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    /// <summary>
    /// Verifies a host directory against its manifest. Returns the problems
    /// found (empty when every file matches and no file is missing or extra).
    /// </summary>
    internal List<string> VerifyDirectory(string directory)
    {
        var problems = new List<string>();
        var expected = Files.ToDictionary(f => f.Path, StringComparer.Ordinal);

        var actualPaths = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(p => System.IO.Path.GetRelativePath(directory, p).Replace('\\', '/'))
            .Where(p => p != FileName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in Files)
        {
            if (!actualPaths.Contains(file.Path))
            {
                problems.Add($"missing: {file.Path}");
                continue;
            }

            using var stream = System.IO.File.OpenRead(System.IO.Path.Combine(directory, file.Path));
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (hash != file.Sha256.ToLowerInvariant())
            {
                problems.Add($"changed: {file.Path}");
            }
        }

        problems.AddRange(actualPaths.Where(p => !expected.ContainsKey(p)).Select(p => $"not in manifest: {p}"));
        return problems;
    }
}
