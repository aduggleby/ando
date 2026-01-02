# 0002-Library: Multi-Project Example

Demonstrates building a console application that depends on a class library.

## Structure

```
0002-Library/
├── build.ando
├── src/
│   ├── Greeter.Lib/           # Class library
│   │   ├── Greeter.Lib.csproj
│   │   └── GreetingService.cs
│   └── Greeter.Console/       # Console app (references library)
│       ├── Greeter.Console.csproj
│       └── Program.cs
└── dist/                      # Output (created by build)
```

## Build Script

```csharp
var Library = Project.From("./src/Greeter.Lib/Greeter.Lib.csproj");
var Console = Project.From("./src/Greeter.Console/Greeter.Console.csproj");

var output = Context.Paths.Root / "dist";

// Build library first
Dotnet.Restore(Library);
Dotnet.Build(Library);

// Build and publish console app
Dotnet.Restore(Console);
Dotnet.Build(Console);
Dotnet.Publish(Console, o => o.Output(output));
```

## Running

```bash
ando run build
```

## Expected Output

After running, `dist/` contains:
- `Greeter.Console` (executable)
- `Greeter.Lib.dll` (library)

Running the executable prints:
```
Hello, World! Welcome to ANDO.
```
