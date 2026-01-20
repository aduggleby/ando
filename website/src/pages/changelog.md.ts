// =============================================================================
// changelog.md.ts
//
// Summary: Static endpoint serving the changelog as raw markdown.
//
// Reads changelog.md, strips frontmatter, and returns plain markdown.
// =============================================================================

import type { APIRoute } from "astro";
import { markdownResponse, stripFrontmatter } from "../lib/markdown";
import fs from "node:fs";
import path from "node:path";

export const GET: APIRoute = () => {
  const filePath = path.join(process.cwd(), "src/pages/changelog.md");
  const content = fs.readFileSync(filePath, "utf-8");
  const md = `# ANDO Changelog\n\n${stripFrontmatter(content)}`;
  return markdownResponse(md);
};
