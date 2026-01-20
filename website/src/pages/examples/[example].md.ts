// =============================================================================
// examples/[example].md.ts
//
// Summary: Dynamic endpoint serving example pages as markdown.
//
// Uses getStaticPaths to generate markdown endpoints for all examples.
// =============================================================================

import type { APIRoute, GetStaticPaths } from "astro";
import { markdownResponse } from "../../lib/markdown";

// Example definitions with their content
const exampleData: Record<
  string,
  {
    title: string;
    description: string;
    overview: string;
    steps: string[];
    buildCode: string;
    prerequisites: string[];
    operations: Array<{ name: string; href: string; purpose: string }>;
  }
> = {
  astro: {
    title: "Astro + Cloudflare Pages",
    description: "Build an Astro site and deploy it to Cloudflare Pages with cache purging.",
    overview:
      "This example demonstrates a complete workflow for building and deploying a static Astro website to Cloudflare Pages. The build runs in an Ubuntu container with Node.js installed automatically.",
    steps: [
      "Installs Node.js in the container",
      "Installs npm dependencies",
      "Builds the Astro site",
      "Authenticates with Cloudflare",
      "Deploys to Cloudflare Pages",
      "Purges the Cloudflare cache so visitors see the latest content",
    ],
    buildCode: `// Create a directory reference for the website project.
var website = Directory(".");

// Install Node.js (includes npm) in the Ubuntu container.
Node.Install();

// Install dependencies.
Npm.Install(website);

// Build the Astro site (outputs to ./dist).
Npm.Build(website);

// Verify Cloudflare authentication.
Cloudflare.EnsureAuthenticated();

// Deploy to Cloudflare Pages.
Cloudflare.PagesDeploy(website / "dist", "my-site");

// Purge the Cloudflare cache to ensure visitors see the latest content.
Cloudflare.PurgeCache("example.com");`,
    prerequisites: [
      "`CLOUDFLARE_API_TOKEN` environment variable (or enter when prompted)",
      "`CLOUDFLARE_ACCOUNT_ID` environment variable (or enter when prompted)",
      "A Cloudflare Pages project already created",
    ],
    operations: [
      {
        name: "Node.Install()",
        href: "/providers/node",
        purpose: "Installs Node.js v22 in the container",
      },
      {
        name: "Npm.Install()",
        href: "/providers/npm#install",
        purpose: "Runs `npm install` to install dependencies",
      },
      {
        name: "Npm.Build()",
        href: "/providers/npm#build",
        purpose: "Runs `npm run build` to build the Astro site",
      },
      {
        name: "Cloudflare.EnsureAuthenticated()",
        href: "/providers/cloudflare#ensureauthenticated",
        purpose: "Verifies Cloudflare credentials are available",
      },
      {
        name: "Cloudflare.PagesDeploy()",
        href: "/providers/cloudflare#pagesdeploy",
        purpose: "Deploys the `dist` folder to Cloudflare Pages",
      },
      {
        name: "Cloudflare.PurgeCache()",
        href: "/providers/cloudflare#purgecache",
        purpose: "Clears the CDN cache so visitors see the latest content",
      },
    ],
  },
};

export const getStaticPaths: GetStaticPaths = () => {
  return Object.keys(exampleData).map((example) => ({
    params: { example },
  }));
};

export const GET: APIRoute = ({ params }) => {
  const example = exampleData[params.example as string];
  if (!example) {
    return new Response("Not found", { status: 404 });
  }

  const stepsList = example.steps.map((s, i) => `${i + 1}. ${s}`).join("\n");
  const prereqsList = example.prerequisites.map((p) => `- ${p}`).join("\n");
  const opsTable = example.operations.map((op) => `| [${op.name}](${op.href}) | ${op.purpose} |`).join("\n");

  const md = `# ${example.title}

${example.description}

[Back to Examples](/examples)

## Overview

${example.overview}

The workflow performs these steps:

${stepsList}

## Build Script

\`\`\`csharp
${example.buildCode}
\`\`\`

## Prerequisites

${prereqsList}

## Key Operations

| Operation | Purpose |
|-----------|---------|
${opsTable}

## Running the Build

\`\`\`bash
ando
\`\`\`

The build runs inside a Docker container. On first run, ANDO will prompt for Cloudflare credentials if the environment variables aren't set.
`;

  return markdownResponse(md);
};
