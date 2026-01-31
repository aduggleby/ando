---
title: Pre-Build Cleanup Hook
description: Clean up Syncthing conflict files and other artifacts before builds.
category: Hooks
tags:
  - hooks
  - cleanup
  - syncthing
  - automation
---

## Overview

Pre-hooks run before every ANDO command, making them ideal for cleanup tasks that prevent build errors. This recipe shows how to automatically remove Syncthing conflict files that can interfere with builds.

## The Problem

When using Syncthing or similar file synchronization tools, conflict files can be created when the same file is modified on multiple machines:

```
src/MyApp/Program.cs.sync-conflict-20260115-123456-ABCD.cs
```

These files can cause build errors because:
- The compiler sees extra `.cs` files
- File patterns may match them unexpectedly
- They clutter the project structure

## The Solution

Create a pre-hook that runs before every build and removes conflict files:

**scripts/ando-pre.csando:**
```csharp
Log.Info("Checking for Syncthing conflict files...");

// Find all Syncthing conflict files recursively
var conflictFiles = System.IO.Directory
    .EnumerateFiles(Root, "*.sync-conflict-*", System.IO.SearchOption.AllDirectories)
    .ToList();

if (conflictFiles.Count > 0)
{
    Log.Info($"Found {conflictFiles.Count} Syncthing conflict file(s):");
    foreach (var file in conflictFiles)
    {
        Log.Info($"  {file}");
    }

    Log.Info("Removing conflict files...");
    foreach (var file in conflictFiles)
    {
        try
        {
            System.IO.File.Delete(file);
        }
        catch (Exception ex)
        {
            Log.Warning($"  Failed to delete {file}: {ex.Message}");
        }
    }
    Log.Info("Done.");
}
else
{
    Log.Info("No conflict files found.");
}
```

## Hook File Location

ANDO looks for hook files in a `scripts/` directory in your project root:

```
my-project/
├── build.csando
├── scripts/
│   └── ando-pre.csando    # Runs before every command
└── src/
    └── ...
```

## Hook API

Pre-hooks have access to a subset of the build script API:

| Global | Description |
|--------|-------------|
| `Root` | Project root path (supports `/` operator) |
| `Log` | Logging operations (Info, Warning, Error, Debug) |
| `Shell` | Execute shell commands |
| `Env(name)` | Read environment variables |
| `Directory(path)` | Create directory references |

## Extended Example: Clean Multiple Patterns

Clean up various temporary and conflict files:

```csharp
// Patterns to clean
var patterns = new[]
{
    "*.sync-conflict-*",     // Syncthing conflicts
    "*.orig",                // Merge originals
    "*~",                    // Backup files
    ".DS_Store",             // macOS metadata
    "Thumbs.db"              // Windows thumbnails
};

var totalRemoved = 0;

foreach (var pattern in patterns)
{
    var files = System.IO.Directory
        .EnumerateFiles(Root, pattern, System.IO.SearchOption.AllDirectories)
        .Where(f => !f.Contains("node_modules") && !f.Contains(".git"))
        .ToList();

    foreach (var file in files)
    {
        try
        {
            System.IO.File.Delete(file);
            totalRemoved++;
            Log.Debug($"Removed: {file}");
        }
        catch
        {
            // Ignore deletion failures
        }
    }
}

if (totalRemoved > 0)
{
    Log.Info($"Cleaned up {totalRemoved} temporary file(s)");
}
```

## Conditional Cleanup

Only clean on specific commands or when explicitly requested:

```csharp
// Only clean before 'run' command, not 'verify' or 'clean'
var command = Env("ANDO_COMMAND", required: false) ?? "run";

if (command == "run")
{
    // Perform cleanup
    var conflictFiles = System.IO.Directory
        .EnumerateFiles(Root, "*.sync-conflict-*", System.IO.SearchOption.AllDirectories)
        .ToList();

    // ... delete files
}
```

## See Also

- [Post-Release Auto-Update Hook](/recipes/post-release-hook) - Auto-update tools after publishing
