// =============================================================================
// [page].md.ts
//
// Summary: Dynamic endpoint serving standalone pages as markdown.
//
// Uses getStaticPaths to generate markdown endpoints for standalone pages.
// Supports two types:
// - Inline content (cli, server)
// - File-based content that reads from .md files (changelog, license, about)
// =============================================================================

import type { APIRoute, GetStaticPaths } from "astro";
import { markdownResponse, stripFrontmatter } from "../lib/markdown";
import fs from "node:fs";
import path from "node:path";

// Page definitions - either inline content or file path
type PageDef = { type: "inline"; title: string; content: string } | { type: "file"; title: string; file: string };

const pages: Record<string, PageDef> = {
  cli: {
    type: "inline",
    title: "ANDO CLI Reference",
    content: `Command-line interface reference for the ANDO build tool.

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
\`\`\``,
  },
  server: {
    type: "inline",
    title: "ANDO CI Server",
    content: `Self-hosted CI/CD server for ANDO build scripts with GitHub integration.

## Overview

The ANDO CI Server runs your \`build.csando\` scripts automatically when you push to GitHub. It provides a web interface for monitoring builds, viewing logs, and managing projects.

### Features

- **GitHub App Integration**: Receives webhooks on push events and reports build status back to GitHub.
- **Rootless Docker**: Runs under rootless Docker for enhanced security. Build containers are isolated.
- **Automatic HTTPS**: Caddy reverse proxy automatically obtains and renews Let's Encrypt certificates.
- **Automated Backups**: Daily backups with 7-day daily + 12-month monthly retention.

## Installation

Run the installer from your local machine:

\`\`\`bash
curl -fsSL https://andobuild.com/server-install.sh | bash -s user@your-server-ip
\`\`\`

### Install Options

\`\`\`bash
# Build image locally instead of pulling from ghcr.io
./server-install.sh --build-local user@your-server-ip

# Use an existing SQL Server instead of deploying a container
./server-install.sh --external-sql user@your-server-ip
\`\`\`

## GitHub App Configuration

Create a GitHub App with these settings:

| Setting | Value |
|---------|-------|
| Homepage URL | \`https://your-domain.com\` |
| Callback URL | \`https://your-domain.com/auth/github/callback\` |
| Webhook URL | \`https://your-domain.com/webhooks/github\` |

### Required Permissions

| Permission | Access |
|------------|--------|
| Contents | Read |
| Metadata | Read |
| Commit statuses | Read and write |

### Events

Subscribe to: Push, Pull request

## Server Management

Management scripts installed to \`/opt/ando/scripts/\`:

\`\`\`bash
sudo -u ando /opt/ando/scripts/status.sh   # View container status
sudo -u ando /opt/ando/scripts/logs.sh     # View logs
sudo -u ando /opt/ando/scripts/restart.sh  # Restart services
sudo -u ando /opt/ando/scripts/update.sh   # Pull latest and restart
sudo -u ando /opt/ando/scripts/backup.sh   # Run backup now
\`\`\`

## File Locations

| Path | Description |
|------|-------------|
| \`/opt/ando/docker-compose.yml\` | Docker Compose configuration |
| \`/opt/ando/config/.env\` | Environment configuration |
| \`/opt/ando/scripts/\` | Management scripts |
| \`/opt/ando/backups/\` | Automated backups |
| \`/opt/ando/data/sqldata/\` | SQL Server database |
| \`/opt/ando/data/artifacts/\` | Build artifacts |

## Full Documentation

https://github.com/aduggleby/ando/blob/main/src/Ando.Server/README.md`,
  },
  changelog: {
    type: "file",
    title: "ANDO Changelog",
    file: "src/pages/changelog.md",
  },
  license: {
    type: "file",
    title: "ANDO License",
    file: "src/pages/license.md",
  },
  about: {
    type: "file",
    title: "About ANDO",
    file: "src/pages/about.md",
  },
};

export const getStaticPaths: GetStaticPaths = () => {
  return Object.keys(pages).map((page) => ({
    params: { page },
  }));
};

export const GET: APIRoute = ({ params }) => {
  const page = params.page as string;
  const pageDef = pages[page];

  if (!pageDef) {
    return new Response("Not found", { status: 404 });
  }

  let content: string;

  if (pageDef.type === "inline") {
    content = pageDef.content;
  } else {
    const filePath = path.join(process.cwd(), pageDef.file);
    const fileContent = fs.readFileSync(filePath, "utf-8");
    content = stripFrontmatter(fileContent);
  }

  const md = `# ${pageDef.title}

${content}
`;

  return markdownResponse(md);
};
