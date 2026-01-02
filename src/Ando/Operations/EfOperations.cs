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
public class EfOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<ICommandExecutor> _executorFactory;

    public EfOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    {
        _registry = registry;
        _logger = logger;
        _executorFactory = executorFactory;
    }

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
        _registry.Register("Ef.DatabaseUpdate", async () =>
        {
            var args = new List<string>
            {
                "ef", "database", "update",
                "--project", context.Project.Path
            };

            // Specify context when project has multiple DbContexts.
            if (context.ContextName != "default")
            {
                args.AddRange(new[] { "--context", context.ContextName });
            }

            // Override connection string for different environments.
            if (connectionString != null)
            {
                args.AddRange(new[] { "--connection", connectionString });
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, context.ToString());
    }

    /// <summary>
    /// Registers a step to create a new migration.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="migrationName">Name for the new migration.</param>
    /// <param name="outputDir">Optional output directory for migration files.</param>
    public void AddMigration(EfContextRef context, string migrationName, string? outputDir = null)
    {
        _registry.Register("Ef.AddMigration", async () =>
        {
            var args = new List<string>
            {
                "ef", "migrations", "add", migrationName,
                "--project", context.Project.Path
            };

            if (context.ContextName != "default")
            {
                args.AddRange(new[] { "--context", context.ContextName });
            }

            if (outputDir != null)
            {
                args.AddRange(new[] { "-o", outputDir });
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, context.ToString());
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
        _registry.Register("Ef.Script", async () =>
        {
            var args = new List<string>
            {
                "ef", "migrations", "script",
                "--project", context.Project.Path,
                "-o", outputFile.Value,
                "--idempotent"  // Safe to re-run
            };

            if (context.ContextName != "default")
            {
                args.AddRange(new[] { "--context", context.ContextName });
            }

            if (fromMigration != null)
            {
                args.Add(fromMigration);
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, context.ToString());
    }

    /// <summary>
    /// Registers a step to remove the last migration.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="force">Force removal even if migration has been applied.</param>
    public void RemoveMigration(EfContextRef context, bool force = false)
    {
        _registry.Register("Ef.RemoveMigration", async () =>
        {
            var args = new List<string>
            {
                "ef", "migrations", "remove",
                "--project", context.Project.Path
            };

            if (context.ContextName != "default")
            {
                args.AddRange(new[] { "--context", context.ContextName });
            }

            // Force removes even if migration was applied to database.
            if (force)
            {
                args.Add("--force");
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, context.ToString());
    }
}
