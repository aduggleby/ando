# ando commit - Implementation Plan

## Overview

`ando commit` is a CLI command that commits all staged and unstaged changes with an AI-generated commit message using Claude.

## Usage

```bash
ando commit    # Commit all changes with AI-generated message
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
       │ No              │ Yes
       ▼                 │
┌──────────────────┐     │
│ "Nothing to      │     │
│ commit"          │     │
│ Exit 0           │     │
└──────────────────┘     │
                         ▼
         ┌───────────────────────────────┐
         │ 2. Gather change information  │
         │    - git status --porcelain   │
         │    - git diff (unstaged)      │
         │    - git diff --cached        │
         └───────────────┬───────────────┘
                         │
                         ▼
         ┌───────────────────────────────┐
         │ 3. Generate commit message    │
         │    - Send diff to Claude      │
         │    - Parse response           │
         └───────────────┬───────────────┘
                         │
                         ▼
         ┌───────────────────────────────┐
         │ 4. Display message and confirm│
         │    - Show generated message   │
         │    - Ask for confirmation     │
         └───────────────┬───────────────┘
                         │
                ┌────────┴────────┐
                │ [Y]             │ [n]
                ▼                 ▼
         ┌─────────────┐   ┌─────────────┐
         │ 5. Commit   │   │ Exit        │
         │ git add -A  │   │ (no commit) │
         │ git commit  │   └─────────────┘
         └─────────────┘
```

## Output Examples

### Successful commit

```
$ ando commit
Analyzing changes...

  M src/Ando/Operations/GitHubOperations.cs
  M website/src/content/providers/github.md
  A website/src/content/recipes/github-releases.md

Generated commit message:
────────────────────────────────────────
docs: add options reference to GitHub provider and create releases recipe

Add detailed Options Reference section to GitHub provider documentation
explaining CreateRelease, CreatePr, and PushImage options. Create new
recipe for GitHub release management workflows.
────────────────────────────────────────

? Commit with this message? [Y/n] y

Committed: docs: add options reference to GitHub provider and create releases recipe
```

### Nothing to commit

```
$ ando commit
Nothing to commit. Working tree is clean.
```

### User declines message

```
$ ando commit
Analyzing changes...

  M src/Ando/Cli/Program.cs

Generated commit message:
────────────────────────────────────────
refactor: extract command registration into separate methods
────────────────────────────────────────

? Commit with this message? [Y/n] n

Commit cancelled.
```

## Claude Prompt Design

### Prompt Template

```
Generate a concise git commit message for the following changes.

Rules:
- Use conventional commit format: type(scope): description
- Types: feat, fix, docs, style, refactor, test, chore, build
- First line max 72 characters
- Be specific but concise
- Focus on WHAT changed and WHY, not HOW
- If multiple unrelated changes, focus on the primary change

Files changed:
{git_status_output}

Diff:
{git_diff_output}

Output ONLY the commit message. No markdown, no explanations, no quotes.
```

### Prompt Size Management

Git diffs can be large. To manage prompt size:

1. **Truncate large diffs**: If diff exceeds 8000 characters, truncate with note
2. **Prioritize staged changes**: Show `git diff --cached` first
3. **Summarize binary files**: Don't include binary content
4. **Exclude lock files**: Skip package-lock.json, yarn.lock, etc.

```csharp
private string GetTruncatedDiff(int maxLength = 8000)
{
    var diff = GetFullDiff();

    if (diff.Length <= maxLength)
        return diff;

    return diff.Substring(0, maxLength) +
           "\n\n[Diff truncated - showing first 8000 characters]";
}
```

### Files to Exclude from Diff

```csharp
private static readonly string[] ExcludedFiles = new[]
{
    "package-lock.json",
    "yarn.lock",
    "pnpm-lock.yaml",
    "*.lock",
    "*.min.js",
    "*.min.css"
};
```

## File Structure

```
src/Ando/
├── Cli/
│   ├── Commands/
│   │   ├── RunCommand.cs          # Existing
│   │   ├── BumpCommand.cs         # From ando-bump
│   │   └── CommitCommand.cs       # NEW
│   └── Program.cs                 # Add commit command registration
│
├── Git/                           # Shared with BumpCommand
│   └── GitOperations.cs           # Status, diff, add, commit
│
└── AI/                            # NEW directory
    └── CommitMessageGenerator.cs  # Claude integration for commit messages
```

## Implementation Details

### CommitCommand.cs

```csharp
// =============================================================================
// CommitCommand.cs
//
// CLI command handler for 'ando commit'. Commits all changes with an
// AI-generated commit message using Claude.
// =============================================================================

public class CommitCommand
{
    private readonly GitOperations _git;
    private readonly CommitMessageGenerator _messageGenerator;
    private readonly IConsole _console;

    public async Task<int> ExecuteAsync()
    {
        // 1. Check for changes
        if (!_git.HasUncommittedChanges())
        {
            _console.WriteLine("Nothing to commit. Working tree is clean.");
            return 0;
        }

        // 2. Analyze changes
        _console.WriteLine("Analyzing changes...");
        _console.WriteLine();

        // Show changed files
        foreach (var file in _git.GetChangedFiles())
        {
            _console.WriteLine($"  {file}");
        }
        _console.WriteLine();

        // 3. Generate commit message with Claude
        var message = await _messageGenerator.GenerateAsync(
            _git.GetStatus(),
            _git.GetDiff()
        );

        // 4. Show and confirm
        _console.WriteLine("Generated commit message:");
        _console.WriteLine("────────────────────────────────────────");
        _console.WriteLine(message);
        _console.WriteLine("────────────────────────────────────────");
        _console.WriteLine();

        if (!_console.Confirm("Commit with this message?", defaultValue: true))
        {
            _console.WriteLine("Commit cancelled.");
            return 0;
        }

        // 5. Commit
        _git.StageAll();
        _git.Commit(message);

        _console.WriteLine();
        _console.WriteLine($"Committed: {message.Split('\n')[0]}");

        return 0;
    }
}
```

### CommitMessageGenerator.cs

```csharp
// =============================================================================
// CommitMessageGenerator.cs
//
// Generates commit messages using Claude. Sends git diff and status to Claude
// and parses the response into a clean commit message.
// =============================================================================

public class CommitMessageGenerator
{
    private const int MaxDiffLength = 8000;

    private static readonly string[] ExcludedPatterns = new[]
    {
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "*.min.js",
        "*.min.css"
    };

    public async Task<string> GenerateAsync(string gitStatus, string gitDiff)
    {
        var prompt = BuildPrompt(gitStatus, gitDiff);
        var response = await CallClaudeAsync(prompt);
        return CleanResponse(response);
    }

    private string BuildPrompt(string status, string diff)
    {
        var truncatedDiff = TruncateDiff(diff, MaxDiffLength);

        return $"""
            Generate a concise git commit message for the following changes.

            Rules:
            - Use conventional commit format: type(scope): description
            - Types: feat, fix, docs, style, refactor, test, chore, build
            - First line max 72 characters
            - Be specific but concise
            - Focus on WHAT changed and WHY, not HOW
            - If multiple unrelated changes, focus on the primary change

            Files changed:
            {status}

            Diff:
            {truncatedDiff}

            Output ONLY the commit message. No markdown, no explanations, no quotes.
            """;
    }

    private async Task<string> CallClaudeAsync(string prompt)
    {
        // Option 1: Use Claude CLI
        // Run: echo "{prompt}" | claude -p --allowedTools ''

        // Option 2: Use Anthropic API directly
        // POST to https://api.anthropic.com/v1/messages

        // For simplicity, use Claude CLI which handles auth
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "-p --allowedTools ''",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output;
    }

    private string TruncateDiff(string diff, int maxLength)
    {
        // Filter out excluded files
        var filteredDiff = FilterExcludedFiles(diff);

        if (filteredDiff.Length <= maxLength)
            return filteredDiff;

        return filteredDiff.Substring(0, maxLength) +
               "\n\n[Diff truncated - showing first 8000 characters]";
    }

    private string FilterExcludedFiles(string diff)
    {
        // Remove diff sections for excluded files
        // Split by "diff --git" and filter
        var sections = Regex.Split(diff, @"(?=diff --git)");
        var filtered = sections.Where(s => !IsExcludedFile(s));
        return string.Join("", filtered);
    }

    private bool IsExcludedFile(string diffSection)
    {
        foreach (var pattern in ExcludedPatterns)
        {
            if (pattern.Contains('*'))
            {
                var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(diffSection, regex))
                    return true;
            }
            else if (diffSection.Contains(pattern))
            {
                return true;
            }
        }
        return false;
    }

    private string CleanResponse(string response)
    {
        // Remove any markdown formatting Claude might add
        var clean = response.Trim();

        // Remove quotes if wrapped
        if (clean.StartsWith('"') && clean.EndsWith('"'))
            clean = clean[1..^1];

        // Remove markdown code blocks
        if (clean.StartsWith("```"))
        {
            var lines = clean.Split('\n');
            clean = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        return clean.Trim();
    }
}
```

### GitOperations.cs (Extended)

```csharp
// =============================================================================
// GitOperations.cs
//
// Git operations for checking status, staging files, and committing.
// Runs on host machine (not in container).
// =============================================================================

public class GitOperations
{
    public bool HasUncommittedChanges()
    {
        var result = RunGit("status", "--porcelain");
        return !string.IsNullOrWhiteSpace(result);
    }

    public string GetStatus()
    {
        return RunGit("status", "--porcelain");
    }

    public List<string> GetChangedFiles()
    {
        var status = GetStatus();
        return status
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Substring(3)) // Remove status prefix "M  " etc
            .ToList();
    }

    public string GetDiff()
    {
        // Get both staged and unstaged changes
        var staged = RunGit("diff", "--cached");
        var unstaged = RunGit("diff");

        var result = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(staged))
        {
            result.AppendLine("=== Staged changes ===");
            result.AppendLine(staged);
        }

        if (!string.IsNullOrWhiteSpace(unstaged))
        {
            result.AppendLine("=== Unstaged changes ===");
            result.AppendLine(unstaged);
        }

        return result.ToString();
    }

    public void StageAll()
    {
        RunGit("add", "-A");
    }

    public void Commit(string message)
    {
        // Use stdin for message to handle special characters
        RunGitWithInput("commit", "-F", "-", input: message);
    }

    private string RunGit(params string[] args)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = string.Join(" ", args),
            RedirectStandardOutput = true,
            UseShellExecute = false
        });

        var output = process!.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private void RunGitWithInput(string command, params string[] args, string input)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"{command} {string.Join(" ", args)}",
                RedirectStandardInput = true,
                UseShellExecute = false
            }
        };

        process.Start();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new Exception($"Git {command} failed with exit code {process.ExitCode}");
    }
}
```

## CLI Registration

Update `Program.cs` to register the commit command:

```csharp
var commitCommand = new Command("commit", "Commit all changes with AI-generated message");

commitCommand.SetHandler(async () =>
{
    var command = new CommitCommand();
    return await command.ExecuteAsync();
});

rootCommand.AddCommand(commitCommand);
```

## Testing Strategy

### Unit Tests

| Test | Description |
|------|-------------|
| `CommitMessageGenerator_BuildsPrompt` | Prompt includes status and diff |
| `CommitMessageGenerator_TruncatesLargeDiff` | Diffs over 8000 chars are truncated |
| `CommitMessageGenerator_FiltersLockFiles` | package-lock.json excluded from diff |
| `CommitMessageGenerator_CleansResponse` | Removes markdown formatting |
| `GitOperations_DetectsChanges` | HasUncommittedChanges returns true |
| `GitOperations_DetectsClean` | HasUncommittedChanges returns false |
| `GitOperations_ParsesStatus` | GetChangedFiles parses status output |

### Integration Tests

| Test | Description |
|------|-------------|
| `Commit_WithChanges_GeneratesMessage` | Full flow with Claude |
| `Commit_NoChanges_ExitsClean` | Clean repo exits with 0 |
| `Commit_UserDeclines_NoCommit` | Answering 'n' cancels commit |

## Error Handling

| Error | Message | Exit Code |
|-------|---------|-----------|
| Not a git repo | "Error: Not a git repository." | 1 |
| Claude not available | "Error: Claude CLI not found. Install with: npm install -g @anthropic-ai/claude-code" | 1 |
| Claude API error | "Error: Failed to generate commit message: {details}" | 1 |
| Git commit failed | "Error: Git commit failed: {message}" | 1 |
| No changes | "Nothing to commit. Working tree is clean." | 0 |
| User cancelled | "Commit cancelled." | 0 |

## Integration with ando bump

The `ando bump` command will call `ando commit` (or use the same underlying code) when it detects uncommitted changes:

```csharp
// In BumpCommand.ExecuteAsync
if (_git.HasUncommittedChanges())
{
    _console.WriteLine("Error: You have uncommitted changes.");
    _console.WriteLine();

    if (_console.Confirm("Run 'ando commit' to commit them first?", defaultValue: true))
    {
        var commitCommand = new CommitCommand();
        var result = await commitCommand.ExecuteAsync();

        if (result != 0)
            return result;

        _console.WriteLine();
    }
    else
    {
        return 1;
    }
}
```

## Future Enhancements

Out of scope for initial implementation:

- `--amend` flag to amend the previous commit
- `--scope` flag to suggest a scope for the commit type
- `--no-verify` flag to skip pre-commit hooks
- Interactive message editing before commit
- Learning from repository's commit history style
- Support for co-authors
