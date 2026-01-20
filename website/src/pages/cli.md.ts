// =============================================================================
// cli.md.ts
//
// Summary: Static endpoint serving the CLI reference page as markdown.
//
// Returns CLI documentation in plain markdown format for LLM consumption.
// =============================================================================

import type { APIRoute } from "astro";
import { markdownResponse } from "../lib/markdown";

export const GET: APIRoute = () => {
  const md = `# ANDO CLI Reference

Command-line interface reference for the ANDO build tool.

## Commands

The CLI provides a small, focused surface area.

| Command | Description |
|---------|-------------|
| \`ando\` | Run the build script (same as 'ando run'). |
| \`ando run\` | Run the build script in a Docker container. |
| \`ando verify\` | Check build script for errors without executing. |
| \`ando clean\` | Remove artifacts, temp files, and containers. |
| \`ando help\` | Show available commands and options. |

## Run Options

Options for the \`ando run\` command.

| Flag | Description |
|------|-------------|
| \`-f, --file <file>\` | Use a specific build file instead of build.csando. |
| \`--read-env\` | Load .env file without prompting (also applies to sub-builds). |
| \`--verbosity <level>\` | Set output verbosity (quiet\\|minimal\\|normal\\|detailed). |
| \`--no-color\` | Disable colored output. |
| \`--cold\` | Always create a fresh container (ignore warm cache). |
| \`--image <image>\` | Use a custom Docker image. |
| \`--dind\` | Mount Docker socket for Docker-in-Docker builds. |

## Clean Options

Options for the \`ando clean\` command.

| Flag | Description |
|------|-------------|
| \`--artifacts\` | Remove the artifacts directory. |
| \`--temp\` | Remove temp directory. |
| \`--cache\` | Remove NuGet and npm caches. |
| \`--container\` | Remove the project's warm container. |
| \`--all\` | Remove all of the above. |

## Examples

Common usage patterns.

\`\`\`bash
# Run the build with default settings
ando

# Verify build script without executing
ando verify

# Run with detailed output
ando run --verbosity detailed

# Force a fresh container (cold start)
ando run --cold

# Use a specific Docker image
ando run --image mcr.microsoft.com/dotnet/sdk:9.0

# Clean everything
ando clean --all

# Only remove the warm container
ando clean --container
\`\`\`
`;

  return markdownResponse(md);
};
