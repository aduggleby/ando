---
title: Dotnet
description: Build, test, and publish .NET projects using the dotnet CLI.
provider: Dotnet
---

## Example

Build and publish a .NET application.

```csharp
// Define project references
var App = Dotnet.Project("./src/App/App.csproj");
var Tests = Dotnet.Project("./tests/App.Tests/App.Tests.csproj");

// Restore, build, and test
Dotnet.Restore(App);
Dotnet.Build(App, o => o.Configuration = Configuration.Release);
Dotnet.Test(Tests);

// Publish as self-contained single file
Dotnet.Publish(App, o => o
  .Output(Root / "dist")
  .WithConfiguration(Configuration.Release)
  .WithRuntime("linux-x64")
  .AsSelfContained()
  .AsSingleFile());
```
