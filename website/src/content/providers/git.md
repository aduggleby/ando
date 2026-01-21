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

// Add and commit changes
Git.Add(".");
Git.Commit("Release v1.0.0");
```

## Using with Profiles

Git operations are commonly used with profiles to run only during release workflows.

```csharp
// Use profiles to run Git operations conditionally
var release = DefineProfile("release");

Dotnet.Build(app);
Dotnet.Test(tests);

if (release) {
    Git.Add(".");
    Git.Commit("Release v1.0.0");
    Git.Tag("v1.0.0");
    Git.Push();
    Git.PushTags();
}

// CLI usage:
// ando -p release
```

## Notes

- Git operations run on the **host machine**, not inside the container, since git credentials are typically configured on the host.
- By default, `Git.Tag()` creates annotated tags. Use `.AsLightweight()` for lightweight tags.
