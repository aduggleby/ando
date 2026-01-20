// =============================================================================
// markdown.ts
//
// Summary: Utility functions for generating markdown versions of pages.
//
// This module provides helpers for:
// - Reading and stripping frontmatter from markdown files
// - Converting page content to plain markdown for LLM consumption
// - Generating consistent markdown responses for static endpoints
// =============================================================================

import type { APIContext } from "astro";

/**
 * Creates a markdown Response with proper headers.
 */
export function markdownResponse(content: string): Response {
  return new Response(content, {
    headers: {
      "Content-Type": "text/markdown; charset=utf-8",
    },
  });
}

/**
 * Strips YAML frontmatter from markdown content.
 * Frontmatter is delimited by --- at the start of the file.
 */
export function stripFrontmatter(content: string): string {
  const lines = content.split("\n");
  if (lines[0]?.trim() !== "---") {
    return content;
  }

  let endIndex = -1;
  for (let i = 1; i < lines.length; i++) {
    if (lines[i]?.trim() === "---") {
      endIndex = i;
      break;
    }
  }

  if (endIndex === -1) {
    return content;
  }

  return lines
    .slice(endIndex + 1)
    .join("\n")
    .trim();
}

/**
 * Converts HTML to simple markdown.
 * Handles common patterns from the ANDO website.
 */
export function htmlToMarkdown(html: string): string {
  return (
    html
      // Convert <code> to backticks
      .replace(/<code[^>]*>(.*?)<\/code>/gi, "`$1`")
      // Convert <em> to italics
      .replace(/<em>(.*?)<\/em>/gi, "*$1*")
      // Convert <strong> and <b> to bold
      .replace(/<strong>(.*?)<\/strong>/gi, "**$1**")
      .replace(/<b>(.*?)<\/b>/gi, "**$1**")
      // Convert <a> to markdown links
      .replace(/<a[^>]*href="([^"]*)"[^>]*>(.*?)<\/a>/gi, "[$2]($1)")
      // Remove other HTML tags
      .replace(/<[^>]+>/g, "")
      // Decode HTML entities
      .replace(/&lt;/g, "<")
      .replace(/&gt;/g, ">")
      .replace(/&amp;/g, "&")
      .replace(/&quot;/g, '"')
      .replace(/&#39;/g, "'")
  );
}

/**
 * Formats operations data as markdown.
 */
export function operationsToMarkdown(
  operations: Array<{
    name: string;
    desc: string;
    examples?: string[];
  }>
): string {
  return operations
    .map((op) => {
      let md = `### ${op.name}\n\n${htmlToMarkdown(op.desc)}`;
      if (op.examples && op.examples.length > 0) {
        md += "\n\n```csharp\n" + op.examples.join("\n") + "\n```";
      }
      return md;
    })
    .join("\n\n");
}
