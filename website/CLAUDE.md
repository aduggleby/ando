# ANDO Website - Claude Code Instructions

## Overview

The ANDO website is a static documentation site built with Astro. It serves as the landing page and reference documentation for the ANDO build system.

## Technology Stack

- **Astro** - Static site generator
- **CSS** - Custom styles in `src/styles/global.css`
- **No JavaScript frameworks** - Pure Astro components

## Directory Structure

```
website/
├── src/
│   ├── pages/
│   │   └── index.astro    # Main (only) page
│   └── styles/
│       └── global.css     # All styling
├── public/                # Static assets
├── astro.config.mjs       # Astro configuration
└── package.json           # Dependencies
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

## Content Structure

The `index.astro` page contains:

1. **Data arrays** (lines 4-170) - JavaScript arrays defining:
   - `commands` - CLI commands
   - `runOptions` - Flags for `ando run`
   - `cleanOptions` - Flags for `ando clean`
   - `operations` - All operation methods with examples

2. **HTML template** (lines 172+) - Renders the data arrays into tables

## Updating Documentation

When CLI commands or operations change in the main codebase:

1. Update the corresponding data array in `index.astro`
2. Ensure `group` values in `operations` match: `"Dotnet"`, `"Npm"`, `"Ef"`
3. Run `npm run build` to verify no errors

## Syntax Highlighting

Code examples use custom CSS classes for syntax highlighting:
- `.tok-keyword` - Keywords (var, using)
- `.tok-type` - Type names
- `.tok-func` - Function/method names
- `.tok-string` - String literals
- `.tok-comment` - Comments
- `.tok-op` - Operators
- `.tok-namespace` - Namespace names
- `.tok-prop` - Property names
