# ANDO

Build and deployment scripts in plain C# with full IDE support, type safety, and IntelliSense. Scripts run in isolated Docker containers for reproducible builds.

[![Website](https://img.shields.io/badge/docs-andobuild.com-blue)](https://andobuild.com)
[![GitHub](https://img.shields.io/github/stars/aduggleby/ando?style=social)](https://github.com/aduggleby/ando)

## Quick Start

```bash
dotnet tool install -g ando
```

Create `build.csando`:

```csharp
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(project);
```

Run it:

```bash
ando
```

## Documentation

- **Website:** [andobuild.com](https://andobuild.com)
- **CLI Reference:** [andobuild.com/cli](https://andobuild.com/cli)
- **Hooks:** [andobuild.com/hooks](https://andobuild.com/hooks)
- **Examples:** [andobuild.com/examples](https://andobuild.com/examples)
- **LLM Reference:** [andobuild.com/llms.txt](https://andobuild.com/llms.txt)

## Source Code

- **GitHub:** [github.com/aduggleby/ando](https://github.com/aduggleby/ando)

## License

[No'Saasy License](https://github.com/aduggleby/ando/blob/main/LICENSE)
