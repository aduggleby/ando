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

Generate API documentation from your .NET project. `Docfx.GenerateDocs` runs `docfx metadata` and `docfx build` with the given configuration file:

```csharp
var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");

// Build project (the project file enables GenerateDocumentationFile, see Tips)
Dotnet.Restore(project);
Dotnet.Build(project, o => o.Configuration = Configuration.Release);

// Generate API documentation (metadata + build)
Docfx.Install();
Docfx.GenerateDocs("./docfx.json");

// Copy the generated site to the host
Ando.CopyArtifactsToHost("_site", "./site");

Log.Info("Documentation generated in ./site");
```

## Generate and Copy in One Step

`Docfx.BuildAndCopy` generates the documentation, copies it to a target directory and removes intermediate files:

```csharp
Docfx.Install();
Docfx.BuildAndCopy("./docfx.json", "_site", "./docs/site");

Ando.CopyArtifactsToHost("docs/site", "./docs/site");
```

## Deploy the Documentation Site

ANDO has no GitHub Pages operation. Generate the site, copy it into the folder your hosting uses, and deploy that folder with the matching provider. For Cloudflare Pages:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");

Dotnet.Restore(project);
Dotnet.Build(project, o => o.Configuration = Configuration.Release);

Docfx.Install();
Docfx.GenerateDocs("./docfx.json");

if (deploy)
{
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(Directory("./_site"), "my-library-docs");

    Log.Info("Documentation deployed");
}
```

## Versioned Documentation

Keep each version in its own folder:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = project.Version; // read from the project file

Dotnet.Restore(project);
Dotnet.Build(project, o => o.Configuration = Configuration.Release);

Docfx.Install();

// Generate and copy into a folder named after the version
Docfx.BuildAndCopy("./docfx.json", "_site", $"./docs/site/{version}");

if (deploy)
{
    Ando.CopyArtifactsToHost($"docs/site/{version}", $"./docs/site/{version}");

    Log.Info($"Built docs for version {version}");
}
```

## Preview Locally

ANDO has no operation that serves documentation. Generate the site and copy it to the host, then serve the folder with any static file server:

```csharp
Docfx.Install();
Docfx.GenerateDocs("./docfx.json");
Ando.CopyArtifactsToHost("_site", "./site");
```

Usage:

```bash
ando
# then, for example: python3 -m http.server --directory site
```

## Multi-Project Documentation

Document multiple projects in a solution:

```csharp
var coreProject = Dotnet.Project("./src/Core/Core.csproj");
var apiProject = Dotnet.Project("./src/Api/Api.csproj");
var clientProject = Dotnet.Project("./src/Client/Client.csproj");

// Build all projects (each project file enables GenerateDocumentationFile)
Dotnet.Restore(coreProject);
Dotnet.Restore(apiProject);
Dotnet.Restore(clientProject);

Dotnet.Build(coreProject);
Dotnet.Build(apiProject);
Dotnet.Build(clientProject);

// Generate combined documentation
// docfx.json should reference all three projects
Docfx.Install();
Docfx.GenerateDocs("./docfx.json");

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
    "content": [{ "files": ["api/**.yml", "api/index.md"] }, { "files": ["articles/**.md", "toc.yml", "index.md"] }],
    "resource": [{ "files": ["images/**"] }],
    "dest": "_site",
    "template": ["default", "modern"]
  }
}
```

## Key Operations

| Operation                                                   | Purpose                                 |
| ----------------------------------------------------------- | --------------------------------------- |
| [Docfx.Install()](/providers/docfx#install)                 | Install DocFX as a dotnet global tool   |
| [Docfx.GenerateDocs()](/providers/docfx#generatedocs)       | Run `docfx metadata` and `docfx build`  |
| [Docfx.CopyToDirectory()](/providers/docfx#copytodirectory) | Copy the generated site                 |
| [Docfx.BuildAndCopy()](/providers/docfx#buildandcopy)       | Generate, copy and clean up in one step |
| [Docfx.Cleanup()](/providers/docfx#cleanup)                 | Remove intermediate files               |

## Tips

- **XML comments** - Enable `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in your `.csproj` (`Dotnet.Build` cannot set MSBuild properties)
- **Triple-slash comments** - Document all public APIs with `///` comments
- **Examples in docs** - Use `<example>` tags in XML comments
- **Custom templates** - DocFX supports custom themes and templates

## See Also

- [DocFX Provider](/providers/docfx) - Full API reference
- [GitHub Releases](/examples/github-releases) - Release documentation with code
- [Git Versioning](/examples/git-versioning) - Version your documentation
