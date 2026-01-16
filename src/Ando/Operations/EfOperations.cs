// =============================================================================
// EfOperations.cs
//
// Summary: Provides Entity Framework Core CLI operations for database management.
//
// EfOperations exposes the 'dotnet ef' commands for database migrations and
// schema management. Like DotnetOperations, methods register steps rather
// than executing immediately.
//
// Architecture:
// - Uses EfContextRef to reference specific DbContext classes in projects
// - Supports multiple DbContexts per project via contextName parameter
// - All operations use the 'dotnet ef' tool (requires EF Core tools package)
//
// Design Decisions:
// - DbContextFrom creates a reference without registering a step
// - Connection strings can be provided at runtime for different environments
// - --idempotent flag used for migration scripts to support re-running
// =============================================================================

using Ando.Context;
using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides Entity Framework Core CLI operations for database management.
/// Uses 'dotnet ef' tool for migrations and database updates.
/// </summary>
public class EfOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Creates a reference to a DbContext in a project.
    /// Does not register a step - just creates a reference for use with other methods.
    /// </summary>
    /// <param name="project">Project containing the DbContext.</param>
    /// <param name="contextName">DbContext class name, or "default" for single-context projects.</param>
    public EfContextRef DbContextFrom(ProjectRef project, string contextName = "default")
    {
        return new EfContextRef(project, contextName);
    }

    /// <summary>
    /// Registers a step to apply pending migrations to the database.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="connectionString">Optional connection string override.</param>
    public void DatabaseUpdate(EfContextRef context, string? connectionString = null)
    {
        RegisterCommand("Ef.DatabaseUpdate", "dotnet",
            () => new ArgumentBuilder()
                .Add("ef", "database", "update", "--project", context.Project.Path)
                .AddIf(context.ContextName != "default", "--context", context.ContextName)
                .AddIfNotNull("--connection", connectionString),
            context.ToString());
    }

    /// <summary>
    /// Registers a step to apply pending migrations to the database.
    /// Uses a connection string from a Bicep deployment output.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="connectionString">Connection string output from a Bicep deployment.</param>
    public void DatabaseUpdate(EfContextRef context, OutputRef connectionString)
    {
        RegisterCommand("Ef.DatabaseUpdate", "dotnet",
            () => new ArgumentBuilder()
                .Add("ef", "database", "update", "--project", context.Project.Path)
                .AddIf(context.ContextName != "default", "--context", context.ContextName)
                .AddIfNotNull("--connection", connectionString.Resolve()),
            context.ToString());
    }

    /// <summary>
    /// Registers a step to create a new migration.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="migrationName">Name for the new migration.</param>
    /// <param name="outputDir">Optional output directory for migration files.</param>
    public void AddMigration(EfContextRef context, string migrationName, string? outputDir = null)
    {
        RegisterCommand("Ef.AddMigration", "dotnet",
            () => new ArgumentBuilder()
                .Add("ef", "migrations", "add", migrationName, "--project", context.Project.Path)
                .AddIf(context.ContextName != "default", "--context", context.ContextName)
                .AddIfNotNull("-o", outputDir),
            context.ToString());
    }

    /// <summary>
    /// Registers a step to generate an idempotent SQL script from migrations.
    /// Idempotent scripts can be safely re-run without causing errors.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="outputFile">Path for the output SQL file.</param>
    /// <param name="fromMigration">Optional starting migration (for partial scripts).</param>
    public void Script(EfContextRef context, BuildPath outputFile, string? fromMigration = null)
    {
        RegisterCommand("Ef.Script", "dotnet",
            () => new ArgumentBuilder()
                .Add("ef", "migrations", "script", "--project", context.Project.Path, "-o", outputFile.Value, "--idempotent")
                .AddIf(context.ContextName != "default", "--context", context.ContextName)
                .AddIfNotNull(fromMigration),
            context.ToString());
    }

    /// <summary>
    /// Registers a step to remove the last migration.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="force">Force removal even if migration has been applied.</param>
    public void RemoveMigration(EfContextRef context, bool force = false)
    {
        RegisterCommand("Ef.RemoveMigration", "dotnet",
            () => new ArgumentBuilder()
                .Add("ef", "migrations", "remove", "--project", context.Project.Path)
                .AddIf(context.ContextName != "default", "--context", context.ContextName)
                .AddFlag(force, "--force"),
            context.ToString());
    }
}
