// =============================================================================
// examples/index.md.ts
//
// Summary: Static endpoint serving the examples list page as markdown.
//
// Returns a list of all available examples in markdown format.
// =============================================================================

import type { APIRoute } from "astro";
import { markdownResponse } from "../../lib/markdown";

const examples = [
  {
    title: "Astro + Cloudflare Pages",
    href: "/examples/astro",
    description: "Build an Astro site and deploy to Cloudflare Pages with cache purging.",
    tags: ["Node.js", "Cloudflare"],
  },
];

export const GET: APIRoute = () => {
  const examplesList = examples
    .map((ex) => `### [${ex.title}](${ex.href})\n\n${ex.description}\n\nTags: ${ex.tags.join(", ")}`)
    .join("\n\n---\n\n");

  const md = `# ANDO Examples

Real-world build scripts you can use as starting points.

${examplesList}
`;

  return markdownResponse(md);
};
