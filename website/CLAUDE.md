# ANDO Website - Claude Code Instructions

## Overview

The ANDO website is a static documentation site built with Astro and Tailwind CSS v4. It serves as the landing page and reference documentation for the ANDO build system.

## Technology Stack

- **Astro** - Static site generator
- **Tailwind CSS v4** - Utility-first CSS framework (via Vite plugin)
- **Prettier** - Code formatter with Tailwind plugin
- **No JavaScript frameworks** - Pure Astro components

## Formatting

**IMPORTANT: Always run Prettier after editing files.**

```bash
# Format all files
npm run format

# Check formatting without writing
npm run format:check
```

VSCode is configured to format on save. When editing files programmatically, run `npm run format` after making changes.

## Styling Guidelines

**IMPORTANT: Use only Tailwind utility classes. Do not create custom CSS styles.**

All styling must be done inline using Tailwind classes. The project uses:
- `@tailwindcss/vite` plugin for Tailwind v4 integration
- `src/styles/app.css` with `@import "tailwindcss";` (no custom styles)
- Dark mode via Tailwind's `dark:` prefix with class-based toggle on `<html>`

### Common Patterns

```html
<!-- Section headings -->
<h2 class="text-xl font-bold text-zinc-900 dark:text-white mb-3">Title</h2>

<!-- Muted paragraphs -->
<p class="text-zinc-500 dark:text-zinc-400 mb-4">Description text.</p>

<!-- Code blocks -->
<div class="bg-zinc-900 rounded-xl border border-zinc-800 p-4 overflow-x-auto relative font-mono text-sm leading-relaxed">
  <span class="absolute top-3 right-4 text-xs text-zinc-400 uppercase tracking-wider">build.ando</span>
  <pre class="m-0 whitespace-pre"><code class="text-zinc-200">...</code></pre>
</div>

<!-- Inline code -->
<code class="text-sm bg-zinc-100 dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 px-1.5 rounded">code</code>

<!-- Tables -->
<table class="w-full text-sm">
  <thead>
    <tr class="border-b border-zinc-200 dark:border-zinc-700">
      <th class="text-left py-3 px-3 text-xs font-semibold text-zinc-400 uppercase tracking-wider">Header</th>
    </tr>
  </thead>
  <tbody>
    <tr class="border-b border-zinc-100 dark:border-zinc-800 hover:bg-zinc-50 dark:hover:bg-zinc-800/50">
      <td class="py-3 px-3">Content</td>
    </tr>
  </tbody>
</table>
```

### Syntax Highlighting Colors

For code examples, use these Tailwind text colors:
- Keywords (`var`, `using`): `text-sky-300`
- Types/variables: `text-amber-500`
- Strings: `text-green-300`
- Functions: `text-violet-300`
- Namespaces: `text-sky-200`
- Comments: `text-slate-400 italic`
- Properties: `text-blue-300`
- Operators: `text-slate-200`

## Directory Structure

```
website/
├── src/
│   ├── components/
│   │   └── OperationsTable.astro  # Reusable operations table
│   ├── data/
│   │   └── operations.js          # Operations data by provider
│   ├── layouts/
│   │   └── ProviderLayout.astro   # Layout for provider pages
│   ├── pages/
│   │   ├── index.astro            # Main landing page
│   │   ├── dotnet.astro           # Provider pages
│   │   ├── ef.astro
│   │   ├── npm.astro
│   │   ├── node.astro
│   │   ├── dotnetsdk.astro
│   │   ├── azure.astro
│   │   ├── bicep.astro
│   │   ├── cloudflare.astro
│   │   ├── functions.astro
│   │   └── appservice.astro
│   └── styles/
│       └── app.css                # Tailwind import only
├── public/                        # Static assets
├── astro.config.mjs               # Astro + Tailwind Vite config
└── package.json                   # Dependencies
```

## Development

```bash
# Install dependencies
npm install

# Start dev server (hot reload)
npm run dev

# Build for production
npm run build

# Preview production build
npm run preview
```

## Content Management

### Operations Data

Operations are defined in `src/data/operations.js` grouped by provider. Each operation has:
- `group`: Provider name (e.g., "Dotnet", "Npm")
- `name`: Method name (e.g., "Dotnet.Build")
- `desc`: HTML description
- `examples`: Array of example code strings

### Adding a New Provider

1. Add operations to `src/data/operations.js`
2. Create a new page in `src/pages/{provider}.astro` using `ProviderLayout`
3. Add the provider to `sortedProviders` array in:
   - `src/pages/index.astro` (navigation dropdown)
   - `src/layouts/ProviderLayout.astro` (navigation dropdown)
4. Keep providers sorted alphabetically

### Updating Documentation

When CLI commands or operations change in the main codebase:
1. Update the corresponding data in `src/data/operations.js`
2. Update `public/llms.txt` to reflect the same changes (this file provides LLM-friendly documentation)
3. Run `npm run build` to verify no errors

### LLM Documentation (llms.txt)

**IMPORTANT: Keep `public/llms.txt` in sync with the website.**

The `public/llms.txt` file follows the [llms.txt standard](https://llmstxt.org/) and provides a plain-text reference for LLMs. When updating operations, CLI commands, or examples on the website, ensure the same changes are reflected in `llms.txt`. This file includes:
- Quick start guide
- CLI commands
- Global variables and functions
- Common patterns and examples
- All operations by provider
