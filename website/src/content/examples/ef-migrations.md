---
title: EF Core Migrations
description: Run Entity Framework Core database migrations as part of your build pipeline.
category: .NET
tags:
  - dotnet
  - ef-core
  - database
  - migrations
---

## Overview

Entity Framework Core migrations allow you to evolve your database schema alongside your code. This example shows how to run migrations as part of your ANDO build pipeline.

## Basic Migration

Apply pending migrations to a database:

```csharp
var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

// Build the project first (required for EF tools)
Dotnet.Restore(project);
Dotnet.Build(project);

// Reference the DbContext
var dbContext = Ef.DbContextFrom(project, "AppDbContext");

// Apply migrations
Ef.DatabaseUpdate(dbContext, Env("CONNECTION_STRING"));
```

## Migration with Bicep Deployment

Get the connection string from a Bicep deployment output:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy infrastructure
    var deployment = Bicep.DeployToResourceGroup("my-rg", "./infra/main.bicep");

    // Get connection string from deployment output
    var dbContext = Ef.DbContextFrom(project, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, deployment.Output("sqlConnectionString"));

    Log.Info("Migrations applied successfully");
}
```

## Generate SQL Script

For production deployments, generate a SQL script instead of running migrations directly:

```csharp
var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);

var dbContext = Ef.DbContextFrom(project, "AppDbContext");

// Generate idempotent migration script
Ef.Script(dbContext, Root / "migrations.sql");

// Copy script to host for manual review/execution
Ando.CopyArtifactsToHost("migrations.sql", "./artifacts/migrations.sql");

Log.Info("Migration script generated at ./artifacts/migrations.sql");
```

## Multiple DbContexts

Handle multiple database contexts in the same project:

```csharp
var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);

// Reference multiple contexts
var appContext = Ef.DbContextFrom(project, "AppDbContext");
var auditContext = Ef.DbContextFrom(project, "AuditDbContext");

// Apply migrations to each
Ef.DatabaseUpdate(appContext, Env("APP_CONNECTION_STRING"));
Ef.DatabaseUpdate(auditContext, Env("AUDIT_CONNECTION_STRING"));

Log.Info("All database migrations applied");
```

## Conditional Migrations

Only run migrations in specific profiles:

```csharp
var deploy = DefineProfile("deploy");
var migrateOnly = DefineProfile("migrate");

var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);

// Migrations run in both deploy and migrate profiles
if (deploy || migrateOnly)
{
    var dbContext = Ef.DbContextFrom(project, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, Env("CONNECTION_STRING"));
    Log.Info("Migrations applied");
}

// Full deployment only in deploy profile
if (deploy)
{
    Dotnet.Publish(project, o => o.Output(Root / "publish"));
    Azure.EnsureAuthenticated();
    AppService.DeployZip("my-webapp", Root / "publish");
}
```

Usage:
```bash
# Run migrations only
ando -p migrate

# Full deployment with migrations
ando -p deploy
```

## Full Example: Web App with Migrations

Complete deployment including database migrations:

```csharp
var deploy = DefineProfile("deploy");

var webProject = Dotnet.Project("./src/WebApp/WebApp.csproj");
var testProject = Dotnet.Project("./tests/WebApp.Tests/WebApp.Tests.csproj");

// Build and test
Dotnet.SdkInstall();
Dotnet.Restore(webProject);
Dotnet.Build(webProject);
Dotnet.Test(testProject);

if (deploy)
{
    Azure.EnsureAuthenticated();

    // 1. Run migrations BEFORE deployment
    var dbContext = Ef.DbContextFrom(webProject, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, Env("CONNECTION_STRING"));
    Log.Info("Database migrations applied");

    // 2. Publish application
    Dotnet.Publish(webProject, o => o
        .WithConfiguration(Configuration.Release)
        .Output(Root / "publish"));

    // 3. Deploy with zero-downtime swap
    AppService.DeployWithSwap("my-webapp", Root / "publish", "staging");

    Log.Info("Deployment complete!");
}
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Ef.DbContextFrom()](/providers/ef#dbcontextfrom) | Reference a DbContext in a project |
| [Ef.DatabaseUpdate()](/providers/ef#databaseupdate) | Apply pending migrations |
| [Ef.Script()](/providers/ef#script) | Generate SQL migration script |

## Tips

- **Run migrations before deployment** - Ensures the database schema is ready before new code starts
- **Use scripts for production** - Generate and review SQL scripts instead of running migrations directly
- **Test with a staging database** - Run migrations against a staging database first
- **Handle rollbacks** - Keep migration scripts to enable manual rollbacks if needed

## See Also

- [EF Provider](/providers/ef) - Full API reference
- [Azure App Service](/examples/azure-app-service) - Web app deployment
- [Bicep Provider](/providers/bicep) - Infrastructure deployment
