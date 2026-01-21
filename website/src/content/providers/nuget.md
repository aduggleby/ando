---
title: NuGet
description: Create and publish NuGet packages to nuget.org or private feeds.
provider: Nuget
---

## Authentication

NuGet.org requires an API key for publishing packages. You can generate one at [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys).

For CI/CD, set the `NUGET_API_KEY` environment variable and access it via `Vars["NUGET_API_KEY"]`.

## Example

Build a library and publish to NuGet.org.

```csharp
// Define project reference
var App = Dotnet.Project("./src/MyLib/MyLib.csproj");

// Build the project
Dotnet.Build(App);

// Create NuGet package (outputs to bin/Release)
Nuget.Pack(App);

// Authenticate and push to NuGet.org
// Defaults: nuget.org, skips if version already exists
Nuget.EnsureAuthenticated();
Nuget.Push(App);
```
