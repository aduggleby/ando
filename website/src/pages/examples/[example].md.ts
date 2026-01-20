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
  "dotnet-tool": {
    title: ".NET CLI Tool + NuGet",
    description: "Build a .NET CLI tool for multiple platforms, create a NuGet package, and publish with profiles.",
    overview:
      "This example demonstrates building a .NET CLI tool installable via `dotnet tool install`. It publishes self-contained executables for multiple platforms and uses profiles for conditional NuGet publishing.",
    steps: [
      "Installs .NET SDK in the container",
      "Restores, builds, and tests the project",
      "Publishes self-contained executables for multiple platforms",
      "Creates a NuGet package for the dotnet tool",
      "**(with -p push)** Publishes to NuGet.org and deploys docs",
      "Copies artifacts to the host machine",
    ],
    buildCode: `// Define profiles
var push = DefineProfile("push");

var project = Dotnet.Project("./src/Ando/Ando.csproj");
var testProject = Dotnet.Project("./tests/Ando.Tests/Ando.Tests.csproj");
var distPath = Root / "dist";

// Build workflow
Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(testProject);

// Publish for multiple platforms
var runtimes = new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" };
foreach (var runtime in runtimes)
{
    Dotnet.Publish(project, o => o
        .WithRuntime(runtime)
        .Output(distPath / runtime)
        .AsSelfContained()
        .AsSingleFile());
}

// Create NuGet package
Nuget.Pack(project);

// Push to NuGet.org (only with -p push)
if (push)
{
    Nuget.EnsureAuthenticated();
    Nuget.Push(project);
    Ando.Build(Directory("./website"));
}

// Copy artifacts to host
Ando.CopyArtifactsToHost("dist", "./dist");
Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");`,
    prerequisites: [
      "`NUGET_API_KEY` environment variable (or enter when prompted) - only needed for push profile",
      "A .NET project configured as a tool (with PackAsTool in .csproj)",
    ],
    operations: [
      {
        name: "DefineProfile()",
        href: "/providers/ando#defineprofile",
        purpose: "Creates a profile for conditional execution",
      },
      {
        name: "Dotnet.Publish()",
        href: "/providers/dotnet#publish",
        purpose: "Creates self-contained executables",
      },
      {
        name: "Nuget.Pack()",
        href: "/providers/nuget#pack",
        purpose: "Creates a NuGet package",
      },
      {
        name: "Nuget.Push()",
        href: "/providers/nuget#push",
        purpose: "Publishes to NuGet.org",
      },
      {
        name: "Ando.Build()",
        href: "/providers/ando#build",
        purpose: "Runs a nested build script",
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
