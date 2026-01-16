import { createHighlighter, type Highlighter } from "shiki";

// Cache the highlighter instance to avoid creating multiple instances
let highlighter: Highlighter | null = null;

export async function getHighlighter(): Promise<Highlighter> {
  if (!highlighter) {
    highlighter = await createHighlighter({
      themes: ["github-light", "github-dark"],
      langs: ["csharp", "shell", "bash"],
    });
  }
  return highlighter;
}
