---
title: GitHub
description: GitHub integration using the gh CLI for releases, pull requests, and container registry.
provider: GitHub
---

## Authentication

GitHub operations authenticate using one of these methods (checked in order):

| Method | Description |
|--------|-------------|
| `GITHUB_TOKEN` | Environment variable (preferred for CI/CD) |
| `GH_TOKEN` | Alternative environment variable |
| `gh auth login` | Uses token from ~/.config/gh/hosts.yml |

## Creating Releases

Create GitHub releases with version tags, release notes, and pre-release flags.

```csharp
// Create a GitHub release with auto-generated notes
Git.Tag("v1.0.0");
Git.Push();
Git.PushTags();

GitHub.CreateRelease(o => o
    .WithTag("v1.0.0")
    .WithGeneratedNotes());

// Create release with custom notes
GitHub.CreateRelease(o => o
    .WithTag("v1.0.0")
    .WithTitle("v1.0.0 - Initial Release")
    .WithNotes("## What's New\n- Feature A\n- Feature B"));

// Create a pre-release
GitHub.CreateRelease(o => o
    .WithTag("v1.0.0")
    .AsPrerelease());
```

## Container Registry

Push Docker images to GitHub Container Registry (ghcr.io). The image must be built locally first.

```csharp
// Build and push Docker image to GitHub Container Registry
Docker.Build("Dockerfile", o => o.WithTag("myapp:v1.0.0"));

GitHub.PushImage("myapp", o => o
    .WithTag("v1.0.0")
    .WithOwner("my-org"));

// Push with latest tag
GitHub.PushImage("myapp", o => o.WithTag("latest"));
```

## Pull Requests

Create pull requests programmatically.

```csharp
// Create a pull request
GitHub.CreatePr(o => o
    .WithTitle("Add new feature")
    .WithBody("## Summary\nThis PR adds...")
    .WithBase("main"));

// Create a draft PR
GitHub.CreatePr(o => o
    .WithTitle("WIP: New feature")
    .AsDraft());
```

## Notes

- GitHub operations require the `gh` CLI to be installed.
- Version tags are automatically prefixed with 'v' if not already present (e.g., "1.0.0" becomes "v1.0.0").
- For `PushImage`, the owner is auto-detected from the git remote if not specified.
