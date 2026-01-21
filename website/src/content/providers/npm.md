---
title: Npm
description: Run npm commands for JavaScript/TypeScript projects.
provider: Npm
---

## Example

Build a frontend application.

```csharp
// Create a directory reference
var frontend = Directory("./frontend");

// Install dependencies (prefer ci for reproducible builds)
Npm.Ci(frontend);

// Run linting
Npm.Run(frontend, "lint");

// Run tests
Npm.Test(frontend);

// Build for production
Npm.Build(frontend);
```
