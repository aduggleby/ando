# ANDO LLM Intro

Use this as a quick orientation only. For the latest details, always prefer:

- https://andobuild.com/llms.txt
- https://andobuild.com/cli

## What ANDO Is

ANDO is a C#-based build and deployment tool that runs build scripts in Docker for reproducible automation.

- Overview: https://andobuild.com
- CLI docs: https://andobuild.com/cli
- LLM canonical reference: https://andobuild.com/llms.txt

## Install / Update

Install ANDO as a global .NET tool:

```bash
dotnet tool install -g ando
```

Update to the newest published version:

```bash
dotnet tool update -g ando
```

## Commands (Brief)

Treat these as short hints. For current behavior, flags, and examples, follow the linked docs.

- `ando` / `ando run`: Run the build script.
  Docs: https://andobuild.com/cli#run-options
- `ando verify`: Validate the build script without executing it.
  Docs: https://andobuild.com/cli#run-options
- `ando commit`: Create a commit with an AI-generated message.
  Docs: https://andobuild.com/cli#commit-command
- `ando bump`: Bump project version(s).
  Docs: https://andobuild.com/cli#bump-command
- `ando docs`: Update docs based on changes.
  Docs: https://andobuild.com/cli#docs-command
- `ando release`: Interactive release workflow (includes publish step).
  Docs: https://andobuild.com/cli#release-command
- `ando ship`: Release-like workflow without publish.
  Docs: https://andobuild.com/cli#ship-command
- `ando clean`: Remove artifacts/temp files/containers.
  Docs: https://andobuild.com/cli#clean-options
- `ando help`: Show current command list and usage.
  Docs: https://andobuild.com/cli
- `ando --version` / `ando -v`: Show installed ANDO version.
  Docs: https://andobuild.com/cli

## Freshness Rule

If command behavior, flags, or workflow details are needed, check `https://andobuild.com/llms.txt` first, then `https://andobuild.com/cli`.
