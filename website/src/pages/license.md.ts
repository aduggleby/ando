// =============================================================================
// license.md.ts
//
// Summary: Static endpoint serving the license page as raw markdown.
//
// Reads license.md, strips frontmatter, and returns plain markdown.
// =============================================================================

import type { APIRoute } from "astro";
import { markdownResponse, stripFrontmatter } from "../lib/markdown";
import fs from "node:fs";
import path from "node:path";

export const GET: APIRoute = () => {
  const filePath = path.join(process.cwd(), "src/pages/license.md");
  const content = fs.readFileSync(filePath, "utf-8");
  const md = `# ANDO License\n\n${stripFrontmatter(content)}`;
  return markdownResponse(md);
};
