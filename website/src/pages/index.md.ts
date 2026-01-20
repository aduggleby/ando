// =============================================================================
// index.md.ts
//
// Summary: Static endpoint serving the homepage as markdown.
//
// Returns the main ANDO documentation page in plain markdown format for LLM
// consumption. Content mirrors index.astro but in markdown.
// =============================================================================

import type { APIRoute } from "astro";
import { markdownResponse, operationsToMarkdown } from "../lib/markdown";
import { getOperationsGroupedByProvider, operations } from "../data/operations.js";

export const GET: APIRoute = () => {
  const groupedOperations = getOperationsGroupedByProvider();

  const gettingStartedCode = `# Install ANDO
dotnet tool install -g ando

# Create a hello world build script
echo 'Log.Info("Hello, World!");' > build.csando

# Run the build script
ando run`;

  const exampleBuildCode = `// Project and directory references
var WebApi = Dotnet.Project("./src/WebApi/WebApi.csproj");
var Frontend = Directory("./frontend");

// Backend build and publish
Dotnet.Restore(WebApi);
Dotnet.Build(WebApi);
Dotnet.Test(WebApi);
Dotnet.Publish(WebApi, o => o.Output(Root / "dist" / "api"));

// Frontend build
Node.Install();
Npm.Ci(Frontend);
Npm.Run(Frontend, "build");

// Deploy frontend to Cloudflare Pages
Cloudflare.EnsureAuthenticated();
Cloudflare.PagesDeploy(Frontend / "dist", "my-frontend");
Cloudflare.PurgeCache("example.com");`;

  const operationsSections = groupedOperations
    .map(([provider, ops]) => `### ${provider}\n\n${operationsToMarkdown(ops as any[])}`)
    .join("\n\n---\n\n");

  const md = `# ANDO - Build and deploy with a familiar syntax

> From the Latin "andō" - to set in motion.

ANDO is a continuous integration tool that runs your build and deployment workflow inside a Docker container. C# like syntax, support for .NET, Node, Npm, Cloudflare and more.

## Getting Started

Install as a dotnet tool.

\`\`\`bash
${gettingStartedCode}
\`\`\`

## ANDO Commands

| Command | Description |
|---------|-------------|
| \`ando\` | Run the build script (same as 'ando run'). |
| \`ando run\` | Run the build script in a Docker container. |
| \`ando verify\` | Check build script for errors without executing. |
| \`ando clean\` | Remove artifacts, temp files, and containers. |
| \`ando help\` | Show available commands and options. |

See the [CLI reference](/cli) for all options.

## Example build.csando

C# code defines projects, tools, workflow settings, and operations. Compose builds with nested scripts.

\`\`\`csharp
${exampleBuildCode}
\`\`\`

## All Operations

Each operation registers a deterministic step and runs inside the container.

${operationsSections}

---

${operations.length} operations total.
`;

  return markdownResponse(md);
};
