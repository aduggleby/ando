# ando bump - Implementation Plan

## Overview

`ando bump` is a CLI command that automatically detects projects from `build.csando`, bumps their versions, and commits the changes.

## Usage

```bash
ando bump         # Bump patch version (1.0.0 → 1.0.1)
ando bump minor   # Bump minor version (1.0.0 → 1.1.0)
ando bump major   # Bump major version (1.0.0 → 2.0.0)
```

## Command Flow

```
┌─────────────────────────────────┐
│ 1. Check for uncommitted changes │
└───────────────┬─────────────────┘
                │
        ┌───────▼───────┐
        │ Has changes?  │
        └───────┬───────┘
                │
       ┌────────┴────────┐
       │ Yes             │ No
       ▼                 │
┌──────────────────┐     │
│ Prompt:          │     │
│ Run ando commit? │     │
└────────┬─────────┘     │
         │               │
    [Y]  │  [n]          │
         │   │           │
         ▼   ▼           │
    Run   Exit           │
    ando                 │
    commit               │
         │               │
         └───────┬───────┘
                 │
                 ▼
┌─────────────────────────────────┐
│ 2. Parse build.csando           │
│    - Find Dotnet.Project() calls │
│    - Find Directory() + Npm ops  │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 3. Read current versions        │
│    - .csproj: <Version> tag     │
│    - package.json: version field │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 4. Validate versions            │
│    - Check all versions match   │
│    - If mismatch, prompt user   │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 5. Calculate new version        │
│    - Parse semver               │
│    - Apply bump type            │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 6. Update all project files     │
│    - Write new versions         │
│    - Display changes            │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 7. Update documentation         │
│    - Add changelog entry        │
│    - Update version badge       │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│ 8. Commit changes               │
│    - git add <files>            │
│    - git commit -m "Bump        │
│      version to X.Y.Z"          │
└─────────────────────────────────┘
```

## Output Examples

### Successful bump

```
$ ando bump
Detected projects:
  src/Ando/Ando.csproj                    0.9.23
  src/Ando.Server/Ando.Server.csproj      0.9.23
  website/package.json                    0.9.23

Bumping patch: 0.9.23 → 0.9.24

Updated project versions:
  ✓ src/Ando/Ando.csproj
  ✓ src/Ando.Server/Ando.Server.csproj
  ✓ website/package.json

Updated documentation:
  ✓ website/src/content/pages/changelog.md
  ✓ website/src/pages/index.astro

Committed: Bump version to 0.9.24
```

### With uncommitted changes

```
$ ando bump
Error: You have uncommitted changes.

  M src/Ando/Operations/GitHubOperations.cs
  M website/src/content/providers/github.md

? Run 'ando commit' to commit them first? [Y/n] y

[ando commit runs here...]

Detected projects:
  src/Ando/Ando.csproj                    0.9.23
  ...
```

### Version mismatch

```
$ ando bump
Detected projects:
  src/Ando/Ando.csproj                    0.9.23
  src/Ando.Server/Ando.Server.csproj      0.9.22  ← mismatch
  website/package.json                    0.9.23

Warning: Version mismatch detected.

? Which version should be used as the base?
  > 0.9.23 (src/Ando/Ando.csproj, website/package.json)
    0.9.22 (src/Ando.Server/Ando.Server.csproj)
```

### No projects found

```
$ ando bump
Error: No projects found in build.csando.

The bump command looks for:
  - Dotnet.Project("path/to/project.csproj")
  - Directory("path") used with Npm operations
```

### No build.csando

```
$ ando bump
Error: No build.csando found in current directory.
```

## Project Detection

### Parsing build.csando

The command parses `build.csando` to find project references. This uses simple pattern matching (not full Roslyn parsing) for speed and simplicity.

#### .NET Projects

Detect `Dotnet.Project()` calls:

```csharp
// Pattern: Dotnet.Project("path")
var app = Dotnet.Project("./src/App/App.csproj");
var tests = Dotnet.Project("./tests/App.Tests/App.Tests.csproj");
Dotnet.Build(Dotnet.Project("./src/Other/Other.csproj"));
```

Regex pattern:
```
Dotnet\.Project\s*\(\s*["']([^"']+)["']\s*\)
```

#### npm Projects

Detect `Directory()` calls that are used with npm operations:

```csharp
// Pattern: Directory("path") followed by Npm.* usage
var frontend = Directory("./website");
Npm.Ci(frontend);
Npm.Build(frontend);

// Or inline
Npm.Install(Directory("./client"));
```

Strategy:
1. Find all `Directory("path")` calls and their variable names
2. Find all `Npm.*` calls
3. Match directories used with Npm operations
4. Look for `package.json` in those directories

### Version File Formats

#### .csproj (XML)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
```

Read/write the `<Version>` element. Handle:
- Version in root `<PropertyGroup>`
- Version in conditional `<PropertyGroup>`
- `<VersionPrefix>` + `<VersionSuffix>` (less common)

#### package.json (JSON)

```json
{
  "name": "my-app",
  "version": "1.2.3"
}
```

Read/write the `version` field. Preserve formatting (indentation, trailing newlines).

## Version Bumping Logic

### Semver Parsing

Parse version string into components:

```
1.2.3 → { major: 1, minor: 2, patch: 3 }
1.2.3-beta.1 → { major: 1, minor: 2, patch: 3, prerelease: "beta.1" }
```

### Bump Rules

| Current | Bump Type | Result |
|---------|-----------|--------|
| 1.2.3 | patch | 1.2.4 |
| 1.2.3 | minor | 1.3.0 |
| 1.2.3 | major | 2.0.0 |
| 1.2.3-beta.1 | patch | 1.2.3 (removes prerelease) |
| 1.2.3-beta.1 | minor | 1.3.0 |
| 1.2.3-beta.1 | major | 2.0.0 |

### Version Mismatch Resolution

If detected projects have different versions:

1. Group projects by version
2. Show grouped list to user
3. Prompt to select base version
4. Or use `--base <version>` flag to skip prompt

## Documentation Updates

After bumping project versions, the command updates documentation files.

### Changelog Entry

Adds a new entry to the changelog file (default: `website/src/content/pages/changelog.md`):

```markdown
## 0.9.24

**2024-01-23**

- Version bump
```

The entry is added at the top of the changelog, after the frontmatter.

### Version Badge

Updates the version badge on the website index page (default: `website/src/pages/index.astro`):

Finds and replaces patterns like:
```
v0.9.23 → v0.9.24
```

### Documentation File Detection

The command looks for documentation files in these locations:

| File Type | Search Paths |
|-----------|--------------|
| Changelog | `CHANGELOG.md`, `changelog.md`, `website/src/content/pages/changelog.md` |
| Version Badge | `website/src/pages/index.astro`, `README.md` |

If files are not found, the documentation update step is skipped with a warning.

### Configuration

Projects can customize documentation paths in `.ando/config.json`:

```json
{
  "bump": {
    "changelog": "docs/CHANGELOG.md",
    "versionBadge": "src/pages/index.astro",
    "versionBadgePattern": "v[0-9]+\\.[0-9]+\\.[0-9]+"
  }
}
```

## File Structure

```
src/Ando/
├── Cli/
│   ├── Commands/
│   │   ├── RunCommand.cs          # Existing
│   │   ├── VerifyCommand.cs       # Existing
│   │   ├── CleanCommand.cs        # Existing
│   │   └── BumpCommand.cs         # NEW
│   └── Program.cs                 # Add bump command registration
│
├── Versioning/                    # NEW directory
│   ├── ProjectDetector.cs         # Parse build.csando for projects
│   ├── VersionReader.cs           # Read versions from .csproj/package.json
│   ├── VersionWriter.cs           # Write versions to .csproj/package.json
│   ├── SemVer.cs                  # Semver parsing and bumping
│   ├── DocumentationUpdater.cs    # Update changelog and version badge
│   └── BumpOrchestrator.cs        # Coordinates the bump workflow
│
└── Git/                           # NEW or extend existing
    └── GitOperations.cs           # Check status, add, commit (host-side)
```

## Implementation Details

### BumpCommand.cs

```csharp
// =============================================================================
// BumpCommand.cs
//
// CLI command handler for 'ando bump'. Orchestrates version bumping across
// all projects detected in build.csando.
// =============================================================================

public class BumpCommand
{
    public enum BumpType { Patch, Minor, Major }

    public async Task<int> ExecuteAsync(BumpType type = BumpType.Patch)
    {
        // 1. Check git status
        // 2. If dirty, prompt for ando commit
        // 3. Detect projects from build.csando
        // 4. Read versions from all project files
        // 5. Validate/resolve version mismatches
        // 6. Calculate new version
        // 7. Write new versions to project files
        // 8. Update documentation (changelog, version badge)
        // 9. Commit all changes
    }
}
```

### ProjectDetector.cs

```csharp
// =============================================================================
// ProjectDetector.cs
//
// Parses build.csando to find project references. Uses regex pattern matching
// to extract Dotnet.Project() and Directory() calls.
// =============================================================================

public class ProjectDetector
{
    public record DetectedProject(
        string Path,           // Relative path to project file
        ProjectType Type       // Dotnet or Npm
    );

    public enum ProjectType { Dotnet, Npm }

    public List<DetectedProject> DetectProjects(string buildScriptPath)
    {
        var content = File.ReadAllText(buildScriptPath);
        var projects = new List<DetectedProject>();

        // Find Dotnet.Project() calls
        projects.AddRange(FindDotnetProjects(content));

        // Find Directory() calls used with Npm
        projects.AddRange(FindNpmProjects(content));

        return projects;
    }

    private IEnumerable<DetectedProject> FindDotnetProjects(string content)
    {
        // Regex: Dotnet\.Project\s*\(\s*["']([^"']+)["']\s*\)
        // Extract path, resolve relative to build script location
        // Verify .csproj file exists
    }

    private IEnumerable<DetectedProject> FindNpmProjects(string content)
    {
        // 1. Find all Directory("path") calls with variable assignments
        // 2. Find all Npm.* calls
        // 3. Match which directories are used with Npm
        // 4. Check for package.json in those directories
    }
}
```

### SemVer.cs

```csharp
// =============================================================================
// SemVer.cs
//
// Semantic version parsing and manipulation. Handles standard semver format
// with optional prerelease suffix.
// =============================================================================

public record SemVer(int Major, int Minor, int Patch, string? Prerelease = null)
{
    public static SemVer Parse(string version)
    {
        // Parse "1.2.3" or "1.2.3-beta.1"
    }

    public SemVer Bump(BumpType type)
    {
        return type switch
        {
            BumpType.Patch => this with { Patch = Patch + 1, Prerelease = null },
            BumpType.Minor => this with { Minor = Minor + 1, Patch = 0, Prerelease = null },
            BumpType.Major => this with { Major = Major + 1, Minor = 0, Patch = 0, Prerelease = null },
        };
    }

    public override string ToString()
        => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
```

### VersionReader.cs / VersionWriter.cs

```csharp
// =============================================================================
// VersionReader.cs
//
// Reads version information from project files (.csproj, package.json).
// =============================================================================

public class VersionReader
{
    public string? ReadVersion(string projectPath, ProjectType type)
    {
        return type switch
        {
            ProjectType.Dotnet => ReadCsprojVersion(projectPath),
            ProjectType.Npm => ReadPackageJsonVersion(projectPath),
        };
    }

    private string? ReadCsprojVersion(string path)
    {
        // Parse XML, find <Version> element
        var doc = XDocument.Load(path);
        return doc.Descendants("Version").FirstOrDefault()?.Value;
    }

    private string? ReadPackageJsonVersion(string path)
    {
        // Parse JSON, find "version" field
        var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("version").GetString();
    }
}
```

```csharp
// =============================================================================
// VersionWriter.cs
//
// Writes version information to project files. Preserves file formatting.
// =============================================================================

public class VersionWriter
{
    public void WriteVersion(string projectPath, ProjectType type, string newVersion)
    {
        switch (type)
        {
            case ProjectType.Dotnet:
                WriteCsprojVersion(projectPath, newVersion);
                break;
            case ProjectType.Npm:
                WritePackageJsonVersion(projectPath, newVersion);
                break;
        }
    }

    private void WriteCsprojVersion(string path, string version)
    {
        // Use regex replacement to preserve formatting
        // Pattern: <Version>...</Version>
        var content = File.ReadAllText(path);
        var updated = Regex.Replace(
            content,
            @"<Version>[^<]+</Version>",
            $"<Version>{version}</Version>");
        File.WriteAllText(path, updated);
    }

    private void WritePackageJsonVersion(string path, string version)
    {
        // Use regex to preserve formatting (indentation, etc.)
        var content = File.ReadAllText(path);
        var updated = Regex.Replace(
            content,
            @"""version""\s*:\s*""[^""]+""",
            $@"""version"": ""{version}""");
        File.WriteAllText(path, updated);
    }
}
```

### DocumentationUpdater.cs

```csharp
// =============================================================================
// DocumentationUpdater.cs
//
// Updates documentation files with new version information.
// Handles changelog entries and version badges.
// =============================================================================

public class DocumentationUpdater
{
    private readonly string _repoRoot;

    public DocumentationUpdater(string repoRoot)
    {
        _repoRoot = repoRoot;
    }

    public record UpdateResult(string FilePath, bool Success, string? Error = null);

    public List<UpdateResult> UpdateDocumentation(string oldVersion, string newVersion)
    {
        var results = new List<UpdateResult>();

        // Update changelog
        var changelogPath = FindChangelog();
        if (changelogPath != null)
        {
            results.Add(UpdateChangelog(changelogPath, newVersion));
        }

        // Update version badge
        var badgePath = FindVersionBadge();
        if (badgePath != null)
        {
            results.Add(UpdateVersionBadge(badgePath, oldVersion, newVersion));
        }

        return results;
    }

    private string? FindChangelog()
    {
        var candidates = new[]
        {
            "CHANGELOG.md",
            "changelog.md",
            "website/src/content/pages/changelog.md"
        };

        return candidates
            .Select(c => Path.Combine(_repoRoot, c))
            .FirstOrDefault(File.Exists);
    }

    private string? FindVersionBadge()
    {
        var candidates = new[]
        {
            "website/src/pages/index.astro",
            "README.md"
        };

        return candidates
            .Select(c => Path.Combine(_repoRoot, c))
            .FirstOrDefault(File.Exists);
    }

    private UpdateResult UpdateChangelog(string path, string newVersion)
    {
        try
        {
            var content = File.ReadAllText(path);
            var date = DateTime.Now.ToString("yyyy-MM-dd");

            // Find insertion point (after frontmatter if present)
            var insertIndex = 0;
            if (content.StartsWith("---"))
            {
                var endFrontmatter = content.IndexOf("---", 3);
                if (endFrontmatter > 0)
                {
                    insertIndex = content.IndexOf('\n', endFrontmatter) + 1;
                }
            }

            var entry = $"\n## {newVersion}\n\n**{date}**\n\n- Version bump\n";
            var updated = content.Insert(insertIndex, entry);

            File.WriteAllText(path, updated);
            return new UpdateResult(path, true);
        }
        catch (Exception ex)
        {
            return new UpdateResult(path, false, ex.Message);
        }
    }

    private UpdateResult UpdateVersionBadge(string path, string oldVersion, string newVersion)
    {
        try
        {
            var content = File.ReadAllText(path);

            // Replace version patterns like "v0.9.23" or "0.9.23"
            var updated = content
                .Replace($"v{oldVersion}", $"v{newVersion}")
                .Replace($"\"{oldVersion}\"", $"\"{newVersion}\"");

            if (updated == content)
            {
                return new UpdateResult(path, false, "No version pattern found to update");
            }

            File.WriteAllText(path, updated);
            return new UpdateResult(path, true);
        }
        catch (Exception ex)
        {
            return new UpdateResult(path, false, ex.Message);
        }
    }
}
```

### Git Integration

```csharp
// =============================================================================
// GitStatusChecker.cs
//
// Checks git repository status for uncommitted changes.
// Runs on host (not in container).
// =============================================================================

public class GitStatusChecker
{
    public bool HasUncommittedChanges()
    {
        // Run: git status --porcelain
        // Return true if output is not empty
    }

    public List<string> GetChangedFiles()
    {
        // Run: git status --porcelain
        // Parse output into list of changed files
    }

    public void CommitFiles(IEnumerable<string> files, string message)
    {
        // Run: git add <files>
        // Run: git commit -m "<message>"
    }
}
```

## CLI Registration

Update `Program.cs` to register the bump command:

```csharp
var bumpCommand = new Command("bump", "Bump version in all detected projects")
{
    new Argument<string?>("type", () => null, "Bump type: minor or major (default: patch)")
};

bumpCommand.SetHandler(async (string? type) =>
{
    var bumpType = type?.ToLower() switch
    {
        null => BumpType.Patch,
        "minor" => BumpType.Minor,
        "major" => BumpType.Major,
        _ => throw new ArgumentException($"Invalid bump type: {type}. Use 'minor' or 'major'.")
    };

    var command = new BumpCommand();
    return await command.ExecuteAsync(bumpType);
}, bumpTypeArg);

rootCommand.AddCommand(bumpCommand);
```

## Testing Strategy

### Unit Tests

| Test | Description |
|------|-------------|
| `SemVer_Parse_ValidVersion` | Parse "1.2.3" correctly |
| `SemVer_Parse_WithPrerelease` | Parse "1.2.3-beta.1" correctly |
| `SemVer_Bump_Patch` | 1.2.3 → 1.2.4 |
| `SemVer_Bump_Minor` | 1.2.3 → 1.3.0 |
| `SemVer_Bump_Major` | 1.2.3 → 2.0.0 |
| `SemVer_Bump_RemovesPrerelease` | 1.2.3-beta → 1.2.3 (patch) |
| `ProjectDetector_FindsDotnetProjects` | Extracts paths from Dotnet.Project() |
| `ProjectDetector_FindsNpmProjects` | Extracts paths from Directory() + Npm |
| `VersionReader_ReadsCsproj` | Reads version from .csproj |
| `VersionReader_ReadsPackageJson` | Reads version from package.json |
| `VersionWriter_WritesCsproj` | Updates .csproj preserving format |
| `VersionWriter_WritesPackageJson` | Updates package.json preserving format |
| `DocUpdater_FindsChangelog` | Finds changelog in standard locations |
| `DocUpdater_UpdatesChangelog` | Adds entry after frontmatter |
| `DocUpdater_UpdatesVersionBadge` | Replaces version in index.astro |
| `DocUpdater_SkipsMissingFiles` | Gracefully handles missing docs |

### Integration Tests

| Test | Description |
|------|-------------|
| `Bump_SingleDotnetProject` | Full flow with one .csproj |
| `Bump_MultipleDotnetProjects` | Bumps all .csproj files |
| `Bump_MixedProjects` | Bumps .csproj and package.json |
| `Bump_WithUncommittedChanges` | Prompts for ando commit |
| `Bump_VersionMismatch` | Prompts to select base version |
| `Bump_UpdatesDocumentation` | Updates changelog and version badge |
| `Bump_CommitsAllChanges` | Commits both project files and docs |

## Error Handling

| Error | Message | Exit Code |
|-------|---------|-----------|
| No build.csando | "Error: No build.csando found in current directory." | 1 |
| No projects found | "Error: No projects found in build.csando." | 1 |
| Invalid bump type | "Error: Invalid bump type 'foo'. Use 'minor' or 'major'." | 1 |
| Version parse error | "Error: Could not parse version 'abc' in {file}" | 1 |
| Git commit failed | "Error: Git commit failed: {message}" | 1 |
| User cancelled | (silent exit) | 0 |

## Future Enhancements

These are out of scope for initial implementation but could be added later:

- `--dry-run` flag to preview changes without writing
- `--no-commit` flag to update files but not commit
- `--tag` flag to also create a git tag
- `--push` flag to push after commit
- `--pre <label>` flag for prerelease versions (e.g., `--pre beta`)
- `--skip-docs` flag to skip documentation updates
- Interactive changelog entry editing (prompt for release notes)
