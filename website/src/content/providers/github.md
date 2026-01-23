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

## Options Reference

### GitHub.CreateRelease Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Tag name for the release (e.g., "v1.0.0"). Creates the tag if it doesn't exist. |
| `WithTitle(string)` | Release title displayed on GitHub. Defaults to the tag name if not specified. |
| `WithNotes(string)` | Markdown-formatted release notes. Supports full GitHub markdown including headers, lists, and code blocks. |
| `WithGeneratedNotes()` | Auto-generate release notes from commits since the last release. GitHub creates a changelog from PR titles and contributor list. Useful for projects with descriptive PR titles. |
| `AsDraft()` | Create as a draft release. Draft releases are not visible to the public until published. |
| `AsPrerelease()` | Mark as a pre-release. Pre-releases are labeled as "Pre-release" on GitHub and excluded from "latest release" API queries. |
| `WithoutPrefix()` | Don't add 'v' prefix to tag. By default, "1.0.0" becomes "v1.0.0". Use this to keep tags like "1.0.0" as-is. |
| `WithFiles(params string[])` | Files to upload as release assets. Users can download these from the release page. Common uses: binaries, installers, checksums. |

### GitHub.CreatePr Options

| Option | Description |
|--------|-------------|
| `WithTitle(string)` | Pull request title. Should be concise and descriptive of the changes. |
| `WithBody(string)` | PR description in markdown. Include context, testing instructions, and any breaking changes. |
| `WithBase(string)` | Target branch to merge into (e.g., "main", "develop"). Defaults to the repository's default branch. |
| `WithHead(string)` | Source branch containing changes. Defaults to current branch. |
| `AsDraft()` | Create as a draft PR. Draft PRs indicate work-in-progress and can't be merged until marked ready. |

### GitHub.PushImage Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Image tag to push (e.g., "v1.0.0", "latest"). The full image name becomes `ghcr.io/{owner}/{name}:{tag}`. |
| `WithOwner(string)` | GitHub user or organization that owns the image. Auto-detected from git remote if not specified. |

## Notes

- GitHub operations require the `gh` CLI to be installed.
- Version tags are automatically prefixed with 'v' if not already present (e.g., "1.0.0" becomes "v1.0.0").
- For `PushImage`, the owner is auto-detected from the git remote if not specified.
