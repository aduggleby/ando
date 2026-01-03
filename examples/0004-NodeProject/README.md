# 0004-NodeProject Example

This example demonstrates npm operations using ANDO.

## Features Demonstrated

- **Npm.InDirectory()**: Set working directory for npm commands
- **Npm.Install()**: Install dependencies from package.json
- **Npm.Run()**: Execute npm scripts
- **Npm.Test()**: Run test script
- **Npm.Build()**: Run build script

## npm Operations

ANDO supports all common npm operations:

```csharp
// Set working directory
Npm.InDirectory("./frontend");

// Or use project reference
Npm.InProject(Project.From("./frontend/app.csproj"));

// Install dependencies
Npm.Install();  // npm install
Npm.Ci();       // npm ci (clean install)

// Run scripts
Npm.Run("lint");   // npm run lint
Npm.Run("dev");    // npm run dev
Npm.Test();        // npm test
Npm.Build();       // npm run build
```

## Running

```bash
# Local execution (requires Node.js)
ando --local

# Docker execution
ando
```
