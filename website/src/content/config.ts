// =============================================================================
// config.ts
//
// Summary: Content collection schemas for Astro.
//
// Defines the structure and validation for content collections:
// - providers: Provider documentation pages (e.g., dotnet, cloudflare)
// - pages: General pages (e.g., cli, server, about)
// - examples: Example build scripts (e.g., astro, dotnet-tool)
// =============================================================================

import { defineCollection, z } from "astro:content";

const providers = defineCollection({
  type: "content",
  schema: z.object({
    title: z.string(),
    description: z.string(),
    provider: z.string(),
  }),
});

const pages = defineCollection({
  type: "content",
  schema: z.object({
    title: z.string(),
    description: z.string(),
    toc: z.boolean().optional(),
  }),
});

const examples = defineCollection({
  type: "content",
  schema: z.object({
    title: z.string(),
    description: z.string(),
    tags: z.array(z.string()).optional(),
  }),
});

export const collections = { providers, pages, examples };
