---
title: EF Core
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
