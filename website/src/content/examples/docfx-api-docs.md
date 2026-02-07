---
title: DocFX API Documentation
description: Generate and publish API documentation for your .NET libraries using DocFX.
category: .NET
tags:
  - dotnet
  - docfx
  - documentation
  - api
---

## Overview

DocFX is Microsoft's documentation generator for .NET projects. It creates API reference documentation from XML comments and can combine it with conceptual documentation. This example shows how to integrate DocFX into your ANDO build pipeline.

## Project Structure

```
my-library/
├── src/
│   └── MyLibrary/
│       ├── MyLibrary.csproj
│       └── MyClass.cs           # With XML doc comments
├── docs/
│   ├── articles/
│   │   ├── intro.md             # Conceptual docs
│   │   └── getting-started.md
│   ├── api/
│   │   └── .gitignore           # Generated API docs
│   └── toc.yml                  # Table of contents
├── docfx.json                   # DocFX configuration
└── build.csando
```

## Basic Documentation Build

Generate API documentation from your .NET project:

```csharp
var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");

// Build project (generates XML documentation)
Dotnet.Restore(project);
Dotnet.Build(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithProperty("GenerateDocumentationFile", "true"));

// Generate API documentation
Docfx.Metadata("./docfx.json");
Docfx.Build("./docfx.json");

Log.Info("Documentation generated in ./docs/_site");
```

## With Conceptual Documentation

Combine API docs with markdown articles:

```csharp
var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");

Dotnet.Restore(project);
Dotnet.Build(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithProperty("GenerateDocumentationFile", "true"));

// Generate metadata from assemblies
Docfx.Metadata("./docfx.json");

// Build complete documentation site
Docfx.Build("./docfx.json", o => o
    .WithOutput(Root / "docs" / "_site"));

// Copy additional assets if needed
Ando.CopyArtifactsToHost("docs/_site", "./site");

Log.Info("Full documentation site ready");
```

## Deploy to GitHub Pages

Generate and deploy documentation to GitHub Pages:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");

// Build library
Dotnet.Restore(project);
Dotnet.Build(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithProperty("GenerateDocumentationFile", "true"));

// Generate documentation
Docfx.Metadata("./docfx.json");
Docfx.Build("./docfx.json");

if (deploy)
{
    // Deploy to GitHub Pages
    GitHub.PagesPublish(Root / "docs" / "_site");

    Log.Info("Documentation deployed to GitHub Pages");
}
```

## Versioned Documentation

Maintain documentation for multiple versions:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.GetVersion();

Dotnet.Restore(project);
Dotnet.Build(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithProperty("GenerateDocumentationFile", "true")
    .WithProperty("Version", version));

// Generate docs with version in output path
Docfx.Metadata("./docfx.json");
Docfx.Build("./docfx.json", o => o
    .WithOutput(Root / "docs" / "_site" / version));

if (deploy)
{
    // Also update "latest" symlink/copy
    Docfx.Build("./docfx.json", o => o
        .WithOutput(Root / "docs" / "_site" / "latest"));

    GitHub.PagesPublish(Root / "docs" / "_site");

    Log.Info($"Deployed docs for version {version}");
}
```

## Serve Locally for Preview

Serve documentation locally during development:

```csharp
var serve = DefineProfile("serve");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");

Dotnet.Restore(project);
Dotnet.Build(project, o => o
    .WithProperty("GenerateDocumentationFile", "true"));

Docfx.Metadata("./docfx.json");

if (serve)
{
    // Build and serve with live reload
    Docfx.Serve("./docfx.json");
}
else
{
    // Just build
    Docfx.Build("./docfx.json");
}
```

Usage:
```bash
# Build docs only
ando

# Build and serve locally
ando -p serve
```

## Multi-Project Documentation

Document multiple projects in a solution:

```csharp
var coreProject = Dotnet.Project("./src/Core/Core.csproj");
var apiProject = Dotnet.Project("./src/Api/Api.csproj");
var clientProject = Dotnet.Project("./src/Client/Client.csproj");

// Build all projects with XML docs
Dotnet.Restore(coreProject);
Dotnet.Restore(apiProject);
Dotnet.Restore(clientProject);

Dotnet.Build(coreProject, o => o
    .WithProperty("GenerateDocumentationFile", "true"));
Dotnet.Build(apiProject, o => o
    .WithProperty("GenerateDocumentationFile", "true"));
Dotnet.Build(clientProject, o => o
    .WithProperty("GenerateDocumentationFile", "true"));

// Generate combined documentation
// docfx.json should reference all three projects
Docfx.Metadata("./docfx.json");
Docfx.Build("./docfx.json");

Log.Info("Multi-project documentation generated");
```

## Example docfx.json

```json
{
  "metadata": [
    {
      "src": [
        {
          "src": "./src",
          "files": ["**/*.csproj"]
        }
      ],
      "dest": "api"
    }
  ],
  "build": {
    "content": [
      { "files": ["api/**.yml", "api/index.md"] },
      { "files": ["articles/**.md", "toc.yml", "index.md"] }
    ],
    "resource": [
      { "files": ["images/**"] }
    ],
    "dest": "_site",
    "template": ["default", "modern"]
  }
}
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Docfx.Metadata()](/providers/docfx#metadata) | Generate API metadata from source |
| [Docfx.Build()](/providers/docfx#build) | Build documentation site |
| [Docfx.Serve()](/providers/docfx#serve) | Serve docs locally with live reload |

## Tips

- **XML comments** - Enable `GenerateDocumentationFile` in your `.csproj`
- **Triple-slash comments** - Document all public APIs with `///` comments
- **Examples in docs** - Use `<example>` tags in XML comments
- **Custom templates** - DocFX supports custom themes and templates

## See Also

- [DocFX Provider](/providers/docfx) - Full API reference
- [GitHub Releases](/examples/github-releases) - Release documentation with code
- [Git Versioning](/examples/git-versioning) - Version your documentation
