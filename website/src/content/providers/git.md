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

## Commit and change queries

These run on the host while the script is evaluated, so their values can be used directly in C# `if` statements and string interpolation. They refer to the commit being built: `HEAD`, or the commit given with `ando run --ref`.

| Member                                              | Description                                                                                                                                                                                                                                                          |
| --------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Git.Commit`                                        | Full SHA of the commit being built.                                                                                                                                                                                                                                  |
| `Git.ShortCommit`                                   | Short SHA of the commit being built.                                                                                                                                                                                                                                 |
| `Git.RequireClean(params string[] paths)`           | Fails the build before any step runs if tracked files are modified, staged or deleted, or untracked files that are not ignored exist, under the paths (the whole repository when none are given). ANDO's own `build.csando.log` and `.ando/` are ignored. With `--ref` the build uses a clean checkout, so it always passes. |
| `Git.ChangedSince(sinceRef, params string[] paths)` | True if any file under the paths differs between `sinceRef` and the commit being built. Committed changes only. Checks the whole repository when no paths are given.                                                                                                 |
| `Git.LastTag(pattern = "*")`                        | The most recent tag reachable from the commit being built that matches the glob (such as `"v*"`), or `null`.                                                                                                                                                         |

```csharp
Git.RequireClean("src", "web", "infra", "Directory.Build.props");

var last = Git.LastTag("v*");
if (last != null && Git.ChangedSince(last, "src/Worker", "services/signing"))
{
    Log.Info($"Worker changed since {last}; building {Git.ShortCommit}");
}
```

`.ando/runs/` has its own `.gitignore`, so run records never make the tree dirty.

## Notes

- Git operations run on the **host machine**, not inside the container, since git credentials are typically configured on the host.
- By default, `Git.Tag()` creates annotated tags. Use `.AsLightweight()` for lightweight tags.
- `Git.PushTags()` only pushes tags that don't already exist on the remote. Tags are pushed individually rather than with `--tags`, so existing remote tags are never overwritten. If all tags already exist, the step succeeds with a skip message.
- **CI Server**: The ANDO CI Server automatically configures a git committer identity (`user.name` / `user.email`) in build containers, so annotated tags work out of the box. The default identity is `Ando Server <ando-server@localhost>`. Override it with `GIT_COMMITTER_NAME` / `GIT_COMMITTER_EMAIL` environment variables in your `.env.ando` file.
