// =============================================================================
// config.ts
//
// Summary: Content collection schemas for Astro.
//
// Defines the structure and validation for content collections:
// - providers: Provider documentation pages (e.g., dotnet, cloudflare)
// - pages: General pages (e.g., cli, server, about)
// - examples: Example build scripts (e.g., astro, dotnet-tool)
// - recipes: Advanced patterns and techniques (e.g., playwright-e2e-tests)
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

const recipes = defineCollection({
  type: "content",
  schema: z.object({
    title: z.string(),
    description: z.string(),
    difficulty: z.enum(["beginner", "intermediate", "advanced"]).optional(),
    tags: z.array(z.string()).optional(),
  }),
});

export const collections = { providers, pages, examples, recipes };
