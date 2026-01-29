---
title: DocFX
description: Generate API documentation from C# XML documentation comments.
provider: Docfx
---

## Overview

DocFX is Microsoft's documentation generator for .NET projects. It reads XML documentation comments (`///` comments) from C# source code and generates static HTML documentation sites.

## Installation

DocFX is installed as a dotnet global tool. The `Docfx.Install()` operation handles installation automatically.

```csharp
Docfx.Install();
```

## Generating Documentation

The typical workflow is:

1. **Install DocFX** if not already installed
2. **Generate docs** from your docfx.json configuration
3. **Copy** the generated docs to your website's public folder

```csharp
// Install DocFX (skips if already installed)
Docfx.Install();

// Generate and copy docs in one step
Docfx.BuildAndCopy("./docfx.json", "_apidocs", "./website/public/apidocs");
```

## Configuration (docfx.json)

Create a `docfx.json` in your project root:

```json
{
  "metadata": [
    {
      "src": [
        {
          "src": "src",
          "files": ["**/*.csproj"],
          "exclude": ["**/bin/**", "**/obj/**"]
        }
      ],
      "dest": "api"
    }
  ],
  "build": {
    "content": [
      {
        "files": ["api/**.yml", "api/index.md"]
      }
    ],
    "output": "_apidocs",
    "template": ["default", "modern"],
    "globalMetadata": {
      "_appTitle": "My API Documentation",
      "_enableSearch": true
    }
  }
}
```

## Operations

### Docfx.Install

Installs DocFX as a dotnet global tool. Safe to call multiple times - skips if already installed.

### Docfx.BuildAndCopy

The recommended all-in-one operation. Generates documentation and copies it to your target directory:

1. Runs `docfx metadata` to extract API metadata from C# XML comments
2. Runs `docfx build` to generate HTML documentation
3. Creates a redirect `index.html` at the root
4. Copies output to target directory
5. Cleans up intermediate files (`api/` and output directory)

### Docfx.GenerateDocs

Runs only the metadata extraction and build steps. Use when you need more control over the process.

### Docfx.CopyToDirectory

Copies generated documentation to a target directory. Use after `GenerateDocs()` for manual workflows.

### Docfx.Cleanup

Removes intermediate DocFX files (the `api/` metadata folder and output directory).

## Example

Complete example for a project with a documentation website:

```csharp
var publish = DefineProfile("publish");

if (publish)
{
    // Install DocFX
    Docfx.Install();

    // Generate API docs and copy to website
    Docfx.BuildAndCopy("./docfx.json", "_apidocs", "./website/public/apidocs");

    // Build and deploy the website
    Ando.Build(Directory("./website"));
}
```

## C# XML Documentation

DocFX reads standard C# XML documentation comments:

```csharp
/// <summary>
/// Builds a .NET project.
/// </summary>
/// <param name="project">The project reference to build.</param>
/// <param name="options">Optional build options.</param>
public void Build(ProjectRef project, Action<BuildOptions>? options = null)
{
    // ...
}
```

Key XML documentation tags:

| Tag | Description |
|-----|-------------|
| `<summary>` | Brief description of the member |
| `<param>` | Parameter description |
| `<returns>` | Return value description |
| `<remarks>` | Additional notes |
| `<example>` | Usage example |
| `<see cref=""/>` | Cross-reference to another member |
| `<inheritdoc/>` | Inherit documentation from base class |

## Tips

- Add `api/`, `_apidocs/`, and your output directory to `.gitignore`
- Generated docs can be ~30-50MB for large projects
- Regenerate docs during your publish/deploy workflow, not for every build
- Use `<inheritdoc/>` on interface implementations to avoid duplication
