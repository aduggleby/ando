// =============================================================================
// [provider].md.ts
//
// Summary: Dynamic endpoint serving provider pages as markdown.
//
// Uses getStaticPaths to generate markdown endpoints for all providers.
// Each endpoint returns the provider's operations in markdown format.
// =============================================================================

import type { APIRoute, GetStaticPaths } from "astro";
import { markdownResponse, operationsToMarkdown } from "../../lib/markdown";
import { getOperationsForProvider } from "../../data/operations.js";
import { providers } from "../../data/providers.js";

// Provider descriptions (matching the .astro pages)
const providerDescriptions: Record<string, string> = {
  ando: "Core operations for logging, artifacts, nested builds, and build configuration.",
  appservice: "Deploy to Azure App Service with zip deploy, slot swapping, and zero-downtime deployments.",
  azure: "Azure CLI authentication and subscription management for cloud deployments.",
  bicep: "Deploy Azure infrastructure using Bicep templates with typed outputs.",
  cloudflare: "Deploy to Cloudflare Pages and manage CDN cache.",
  docker: "Build Docker container images using the Docker CLI.",
  dotnet: "Build, test, and publish .NET projects using the dotnet CLI.",
  ef: "Entity Framework Core migrations and database operations.",
  functions: "Deploy to Azure Functions with zip deploy, slot swapping, and zero-downtime deployments.",
  git: "Version control operations using the Git CLI.",
  github: "GitHub integration for releases, pull requests, and container registry.",
  node: "Install Node.js in build containers.",
  npm: "npm operations for installing dependencies and running scripts.",
  nuget: "Pack and push NuGet packages to feeds.",
};

export const getStaticPaths: GetStaticPaths = () => {
  return providers.map((p) => ({
    params: { provider: p.id.toLowerCase() },
    props: { providerId: p.id, providerLabel: p.label },
  }));
};

export const GET: APIRoute = ({ params, props }) => {
  const { provider } = params;
  const { providerId, providerLabel } = props as {
    providerId: string;
    providerLabel: string;
  };

  const operations = getOperationsForProvider(providerId);
  const description = providerDescriptions[provider as string] || `${providerLabel} operations.`;

  const md = `# ${providerLabel} Operations

${description}

## Operations

${operationsToMarkdown(operations)}
`;

  return markdownResponse(md);
};
