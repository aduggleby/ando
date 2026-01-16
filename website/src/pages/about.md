---
layout: ../layouts/ContentLayout.astro
title: About
description: About ANDO, its philosophy, and design principles.
---

## Philosophy

I wasn't happy with the syntax used by the available build and deploy tools. In the age of AI the answer to that is to build your own tool and here we are.

This won't cover all your needs probably, you're free to use an AI or your own skills to add providers and operators into the source code and feel free to PR them back into the project to add them to the monolith. That's the goal, one simple .NET executable that is cross-platform and just does what it says on the tin (and fast).

## Design Principles

1. **Fluent Style** - Simple to read, write and undestand. If you know C# or Java, you can read ANDO.
2. **Typed operations** - Compile-time errors catch mistakes before they run.
3. **Containerized** - Every build runs in Docker for isolation and reproducibility.

### A note on type safety

When you run ando, the CLI:

  1. Loads your build.csando file as C# source
  2. Uses Microsoft.CodeAnalysis.CSharp.Scripting to compile it
  3. If compilation fails (type errors, missing methods, etc.), you get errors before anything executes
  4. Only after successful compilation does it run the workflow

  Each operation class (e.g., DotnetOperations.cs) defines methods with specific signatures. So if you call an operation with wrong arguments, Roslyn catches it during compilation - not at runtime when your CI is halfway through a deployment.
  
  **This is the main advantage over some other build systems IMHO.**

## Links

- [Documentation](https://andobuild.com)
- [GitHub Repository](https://github.com/aduggleby/ando)
- [NuGet Package](https://www.nuget.org/packages/ando)
