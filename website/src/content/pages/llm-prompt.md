---
title: LLM Prompt
description: Copy-paste this prompt to teach LLMs how to add ANDO to your project.
---

Copy the prompt below and paste it into your LLM conversation to teach it how to add ANDO build scripts to your project.

## The Prompt

```text
I want to add ANDO to this project. ANDO is a typed C# build system where build scripts are written in C# and executed in Docker containers.

Before writing any build.csando file, fetch the complete ANDO reference documentation from:
https://andobuild.com/llms.txt

This contains:
- All CLI commands and options
- Global variables (Root, Env, Dotnet, Npm, etc.)
- All operations by provider (Dotnet, Npm, Azure, Docker, GitHub, etc.)
- Options for each operation
- Common patterns and complete examples

After fetching llms.txt, analyze this project's structure to determine:
1. What type of project it is (.NET, Node, both, etc.)
2. What build steps are needed (restore, build, test, publish, deploy)
3. What deployment target to use (Azure, Cloudflare, Docker, NuGet, etc.)

Then create a build.csando file that:
- Uses the appropriate operations for this project type
- Follows the patterns from the documentation
- Includes comments explaining each step
- Uses profiles for conditional execution if needed (e.g., `var publish = DefineProfile("publish");`)

The build script should be placed in the project root as `build.csando`.
```

## What This Prompt Does

1. **Instructs the LLM to fetch documentation** - Points to `llms.txt` which contains the complete, up-to-date ANDO reference
2. **Guides project analysis** - Helps the LLM understand your project structure before writing code
3. **Ensures correct output** - Specifies the expected format and location for the build script

## Tips

- **Keep llms.txt URL handy** - If the LLM doesn't support fetching URLs, you can manually paste the contents from [llms.txt](/llms.txt)
- **Be specific about deployment** - After pasting the prompt, tell the LLM where you want to deploy (e.g., "Deploy to Cloudflare Pages" or "Publish to NuGet")
- **Mention special requirements** - If you need Docker-in-Docker, Entity Framework migrations, or multi-platform builds, mention it explicitly

## Example Conversation

```text
User: [pastes the prompt above]

User: This is a .NET 9 web API with a React frontend. I want to:
- Build and test the API
- Build the frontend with npm
- Deploy the API to Azure App Service
- Deploy the frontend to Cloudflare Pages

LLM: [fetches llms.txt, analyzes project, creates build.csando]
```

## Direct Link

You can also share this direct link to the LLM-friendly documentation:

**[https://andobuild.com/llms.txt](/llms.txt)**

This file follows the [llms.txt standard](https://llmstxt.org/) and contains everything an LLM needs to understand and use ANDO.

## See Also

- [CLI Reference](/cli) - Command-line options and configuration
- [Examples](/examples) - Real-world build scripts and recipes
