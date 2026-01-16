# 0001-Simple: Hello World

A minimal example demonstrating ANDO basics.

## Structure

```
0001-Simple/
├── build.csando      # Build script
├── src/
│   ├── HelloWorld.csproj
│   └── Program.cs
└── dist/           # Output (created by build)
```

## Build Script

```csharp
var App = Dotnet.Project("./src/HelloWorld.csproj");

var outputDir = Env("ANDO_OUTPUT_DIR", required: false) ?? "dist";
var output = Root / outputDir;

Dotnet.Restore(App);
Dotnet.Build(App);
Dotnet.Publish(App, o => o.Output(output));
```

## Running

```bash
# Default output to dist/
ando run build

# Custom output directory
ANDO_OUTPUT_DIR=out ando run build
```

## Expected Output

After running, `dist/` contains the published executable:
- `HelloWorld` (or `HelloWorld.exe` on Windows)

Running the executable prints:
```
Hello, World!
```
