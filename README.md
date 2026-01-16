# ANDO

ANDO allows you to write build and deployment scripts in plain C# with full IDE support, type safety, and IntelliSense. Scripts are executed using Roslyn C# scripting in isolated Docker containers for reproducible builds.

> From the Latin "andō" - to set in motion.

[![Website](https://img.shields.io/badge/docs-andobuild.com-blue)](https://andobuild.com)
[![GitHub](https://img.shields.io/github/stars/aduggleby/ando?style=social)](https://github.com/aduggleby/ando)

- **Plain C#** - No custom syntax or DSLs. If you know C#, you know ANDO.
- **Type-safe** - Compile-time errors catch mistakes before they run.
- **Containerized** - Every build runs in Docker for isolation and reproducibility.
- **Composable** - Run nested builds with `Ando.Build()` for modular workflows.

## Projects

| Project | Description |
|---------|-------------|
| **Ando** (`src/Ando`) | CLI tool that parses and executes `build.ando` scripts. Installed as a dotnet tool. |
| **Ando.Server** (`src/Ando.Server`) | CI/CD server with GitHub webhook integration, real-time build logs via SignalR, and artifact management. |
| **Website** (`website/`) | Documentation site built with Astro. Deployed at [andobuild.com](https://andobuild.com). |

## Documentation

Full documentation is available at **[andobuild.com](https://andobuild.com)**.

## License

[O'Sassy License](https://osaasy.dev/) - see [LICENSE](./LICENSE)
