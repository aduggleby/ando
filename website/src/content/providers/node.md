---
title: Node
description: Install Node.js in Ubuntu-based containers.
provider: Node
---

## Overview

The Node operations allow you to install Node.js globally in Ubuntu-based containers. This is useful when using the default `ubuntu:22.04` image, which doesn't include Node.js pre-installed.

For warm containers, the installation is skipped if Node.js is already installed at the correct major version, making subsequent builds fast.

## Example

Build a static website with Node.js.

```csharp
// Install Node.js (includes npm)
Node.Install();

// Install dependencies
Npm.Install();

// Build the site
Npm.Build();
```

## Version Support

You can specify the Node.js major version to install:

```csharp
// Install Node.js 20 (previous LTS)
Node.Install("20");

// Install Node.js 22 (current LTS, default)
Node.Install("22");

// Or use default (currently v22)
Node.Install();
```
