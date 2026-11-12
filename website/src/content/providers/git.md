---
title: Git
description: Version control operations using the Git CLI.
provider: Git
---

## Example

Tag and push a release with version tracking.

```csharp
// Tag and push a release
Git.Tag("v1.0.0");
Git.Push();
Git.PushTags();

// Custom tag with message
Git.Tag("v1.0.0", o => o.WithMessage("Release notes here"));

// Lightweight tag (not annotated)
Git.Tag("my-tag", o => o.AsLightweight());
```

## Using with Profiles

Git operations are commonly used with profiles to run only during release workflows.

```csharp
// Use profiles to run Git operations conditionally
var release = DefineProfile("release");

Dotnet.Build(app);
Dotnet.Test(tests);

if (release) {
    Git.Tag("v1.0.0");
    Git.Push();
    Git.PushTags();
}

// CLI usage:
// ando -p release
```

## Options Reference

### Git.Tag Options

| Option                | Description                                                                                                                                               |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithMessage(string)` | Set the tag message for annotated tags. Appears when viewing the tag with `git show`.                                                                     |
| `AsLightweight()`     | Create a lightweight tag instead of an annotated tag. Lightweight tags are just pointers to commits without metadata. Use for temporary or local markers. |
| `WithSkipIfExists()`  | Skip tag creation if the tag already exists. Shows a warning instead of failing. Useful in CI where builds may be re-run.                                 |

### Git.Push Options

| Option             | Description                                                                                                                 |
| ------------------ | --------------------------------------------------------------------------------------------------------------------------- |
| `ToRemote(string)` | Remote to push to (e.g., "origin", "upstream"). Defaults to "origin".                                                       |
| `ToBranch(string)` | Branch to push. Defaults to the current branch.                                                                             |
| `WithUpstream()`   | Set upstream tracking reference (`-u` flag). The local branch will track the remote branch for future push/pull operations. |

## Push and PushTags

Both are separate steps and neither does the other's job:

| Operation        | Pushes                                                                                              | Use it when                                                          |
| ---------------- | --------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
| `Git.Push()`     | The current branch (`ToRemote`, `ToBranch`, `WithUpstream` options). Tags are not pushed.           | You committed or bumped a version and want the commit on the remote. |
| `Git.PushTags()` | Tags only, one at a time, skipping tags that already exist on the remote. The branch is not pushed. | You created a tag with `Git.Tag()` and want it on the remote.        |

A release usually calls `Git.Tag(...)`, `Git.Push()` and then `Git.PushTags()`.

## Evaluated when the script loads (not steps)

`Git.Commit`, `Git.ShortCommit`, `Git.CommitTime`, `Git.ChangedSince()` and `Git.LastTag()` are **not steps**. They run git on the host while the script is evaluated, before any container exists and before any step runs. They do not appear in `--dry-run`, and they see the repository as it is when the script loads, not as earlier steps leave it (a tag created by `Git.Tag()` in the same script is not visible to `Git.LastTag()`). `Git.RequireClean()` is also checked at evaluation time. Their values can be used directly in C# `if` statements and string interpolation.

For a step that runs in order with the other steps, see [Step variants](#step-variants-revparse-latesttag-changedfiles). These refer to the commit being built: `HEAD`, or the commit given with `ando run --ref`.

| Member                                              | Description                                                                                                                                                                                                                                                                                                                  |
| --------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Git.Commit`                                        | Full SHA of the commit being built.                                                                                                                                                                                                                                                                                          |
| `Git.ShortCommit`                                   | Short SHA of the commit being built.                                                                                                                                                                                                                                                                                         |
| `Git.CommitTime`                                    | Commit time of the commit being built, as a `long` of unix seconds. Cached. Use it as a reproducible timestamp: `Archive.Zip(.., o => o.WithTimestamp(Git.CommitTime))`.                                                                                                                                                     |
| `Git.RequireClean(params string[] paths)`           | Fails the build before any step runs if tracked files are modified, staged or deleted, or untracked files that are not ignored exist, under the paths (the whole repository when none are given). ANDO's own `build.csando.log` and `.ando/` are ignored. With `--ref` the build uses a clean checkout, so it always passes. |
| `Git.ChangedSince(sinceRef, params string[] paths)` | True if any file under the paths differs between `sinceRef` and the commit being built. Committed changes only. Checks the whole repository when no paths are given.                                                                                                                                                         |
| `Git.LastTag(pattern = "*")`                        | The most recent tag reachable from the commit being built that matches the glob (such as `"v*"`), or `null`.                                                                                                                                                                                                                 |

```csharp
Git.RequireClean("src", "web", "infra", "Directory.Build.props");

var last = Git.LastTag("v*");
if (last != null && Git.ChangedSince(last, "src/Worker", "services/signing"))
{
    Log.Info($"Worker changed since {last}; building {Git.ShortCommit}");
}
```

`.ando/runs/` has its own `.gitignore`, so run records never make the tree dirty.

## Step variants: RevParse, LatestTag, ChangedFiles

These three are **steps**. They run on the host in the source root (the `--ref` worktree when set), in order with the other steps like `Git.Tag` and `Git.Push`, so they appear in `--dry-run` and see what earlier steps did. Each returns a `CommandStep` whose `Value` is the trimmed stdout; read it at execution time (`Ando.When`, `Ando.Defer`, `Ando.Report` or a `Func<>` overload), not at the top level of the script.

| Step                                                       | Runs                                                                                                                                               | Eager counterpart               |
| ---------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------- |
| `Git.RevParse(string rev = "HEAD", bool shortSha = false)` | `git rev-parse [--short] <rev>`                                                                                                                    | `Git.Commit`, `Git.ShortCommit` |
| `Git.LatestTag(string pattern = "*")`                      | `git describe --tags --abbrev=0 --match <pattern> HEAD`. If no tag matches, `Value` is empty and the build continues.                              | `Git.LastTag()`                 |
| `Git.ChangedFiles(string sinceRef, params string[] paths)` | `git diff --name-only <sinceRef> HEAD -- <paths>`. `Value` is a newline-separated list, empty when nothing changed. An unknown ref fails the step. | `Git.ChangedSince()`            |

Of the `CommandStep` fluent methods, `Git.RevParse` and `Git.ChangedFiles` honor `WithTimeout`, `WithEnv`, `Quiet` and `AllowFailure`. Do not use `InDirectory` on them: it resolves to a container path, but git runs on the host. `Git.LatestTag` ignores all the fluent methods.

Use the eager members when you need the value while the script is evaluated (to decide which steps to register). Use the step variants when the value must reflect the state at that point of the run, or must show up as a step.

```csharp
var sha = Git.RevParse(shortSha: true);
var t = Git.LatestTag("v*");
var c = Git.ChangedFiles(t.Value, "src/Worker");

Ando.When(() => c.Value != "", () => Log.Info($"Worker changed since {t.Value}, building {sha.Value}"));
```

## Installing git in the build container

`Git.Install()` is different from every other `Git.*` operation: it runs **in the build container**, not on the host. Use it when commands inside the container need git, for example tests that create real git repositories. The default image has no git.

- If `git` is already on `PATH` the step logs that and does nothing.
- Otherwise it installs with apt (`apt-get update && apt-get install -y --no-install-recommends git ca-certificates`) or with apk (`apk add --no-cache git`). An image with neither fails with an "unsupported image" message.
- It logs `git --version` when done.

```csharp
Git.Install();
```

## Notes

- Git operations run on the **host machine**, not inside the container, since git credentials are typically configured on the host. The members in "Evaluated when the script loads" additionally run when the script loads, not as steps.
- By default, `Git.Tag()` creates annotated tags. Use `.AsLightweight()` for lightweight tags.
- `Git.PushTags()` only pushes tags that don't already exist on the remote. Tags are pushed individually rather than with `--tags`, so existing remote tags are never overwritten. If all tags already exist, the step succeeds with a skip message.
- **CI Server**: The ANDO CI Server automatically configures a git committer identity (`user.name` / `user.email`) in build containers, so annotated tags work out of the box. The default identity is `Ando Server <ando-server@localhost>`. Override it with `GIT_COMMITTER_NAME` / `GIT_COMMITTER_EMAIL` environment variables in your `.env.ando` file.
