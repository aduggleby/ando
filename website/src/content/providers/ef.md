---
title: Entity Framework Core (Ef)
description: Manage Entity Framework Core migrations and database updates.
provider: Ef
---

## Setup

EF Core operations require the `dotnet-ef` tool. ANDO can install it automatically.

```csharp
// Define the EF tool (installs if needed)
var EfTool = Dotnet.Tool("dotnet-ef", "9.0.0");
```

## Example

Run migrations and generate SQL scripts.

```csharp
// Define the DbContext reference
var DataProject = Dotnet.Project("./src/Data/Data.csproj");
var db = Ef.DbContextFrom(DataProject, "AppDbContext");

// Apply migrations to the database
Ef.DatabaseUpdate(db);

// Or generate an idempotent SQL script
Ef.Script(db, Root / "artifacts" / "migration.sql");
```

## Operations Reference

### Ef.DbContextFrom

Creates a reference to an EF Core DbContext for use in other operations.

| Parameter | Description |
|-----------|-------------|
| `project` | ProjectRef pointing to the project containing the DbContext. |
| `contextName` | Name of the DbContext class (e.g., "AppDbContext"). If the project has only one DbContext, this can be omitted. |

### Ef.DatabaseUpdate

Applies all pending migrations to the database.

| Parameter | Description |
|-----------|-------------|
| `context` | DbContext reference from `Ef.DbContextFrom()`. |
| `connectionString` | Optional connection string. If not provided, uses the connection string from the DbContext's configuration. |

### Ef.Script

Generates an idempotent SQL script for all migrations.

| Parameter | Description |
|-----------|-------------|
| `context` | DbContext reference from `Ef.DbContextFrom()`. |
| `outputFile` | Path where the SQL script will be written. |
| `fromMigration` | Optional starting migration. Only generates SQL for migrations after this point. |

