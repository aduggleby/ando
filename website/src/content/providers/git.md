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

| Option | Description |
|--------|-------------|
| `WithMessage(string)` | Set the tag message for annotated tags. Appears when viewing the tag with `git show`. |
| `AsLightweight()` | Create a lightweight tag instead of an annotated tag. Lightweight tags are just pointers to commits without metadata. Use for temporary or local markers. |
| `WithSkipIfExists()` | Skip tag creation if the tag already exists. Shows a warning instead of failing. Useful in CI where builds may be re-run. |

### Git.Push Options

| Option | Description |
|--------|-------------|
| `ToRemote(string)` | Remote to push to (e.g., "origin", "upstream"). Defaults to "origin". |
| `ToBranch(string)` | Branch to push. Defaults to the current branch. |
| `WithUpstream()` | Set upstream tracking reference (`-u` flag). The local branch will track the remote branch for future push/pull operations. |

## Notes

- Git operations run on the **host machine**, not inside the container, since git credentials are typically configured on the host.
- By default, `Git.Tag()` creates annotated tags. Use `.AsLightweight()` for lightweight tags.
- **CI Server**: The ANDO CI Server automatically configures a git committer identity (`user.name` / `user.email`) in build containers, so annotated tags work out of the box. The default identity is `Ando Server <ando-server@localhost>`. Override it with `GIT_COMMITTER_NAME` / `GIT_COMMITTER_EMAIL` environment variables in your `.env.ando` file.
