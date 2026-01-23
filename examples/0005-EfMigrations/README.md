# 0005-EfMigrations Example

This example demonstrates Entity Framework Core operations using ANDO.

## Prerequisites

- .NET 9.0 SDK
- EF Core tools: `dotnet tool install --global dotnet-ef`

## Features Demonstrated

- **Ef.DbContextFrom()**: Reference a DbContext in a project
- **Ef.AddMigration()**: Create a new migration
- **Ef.DatabaseUpdate()**: Apply pending migrations
- **Ef.Script()**: Generate idempotent SQL script
- **Ef.RemoveMigration()**: Remove the last migration

## EF Operations

```csharp
// Reference DbContext
var DbContext = Ef.DbContextFrom(DataProject, "AppDbContext");

// Create a migration
Ef.AddMigration(DbContext, "AddUserTable", outputDir: "Migrations");

// Apply migrations
Ef.DatabaseUpdate(DbContext);
// With connection string override:
Ef.DatabaseUpdate(DbContext, connectionString: "Server=prod;...");

// Generate SQL script
Ef.Script(DbContext, Root / "migration.sql", fromMigration: "InitialCreate");

// Remove last migration
Ef.RemoveMigration(DbContext, force: true);
```

## DotnetTool Support

ANDO can ensure the EF tools are installed:

```csharp
var efTool = Dotnet.Tool("dotnet-ef", "9.0.0");
// Tool is installed on first EF operation
```

## Note

This example has external dependencies (database) and is not included in automated E2E tests.
