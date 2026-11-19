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

using System.Runtime.CompilerServices;
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
    /// Registers a step to generate an idempotent SQL script from migrations.
    /// Idempotent scripts can be safely re-run without causing errors.
    /// </summary>
    /// <param name="context">DbContext reference.</param>
    /// <param name="outputFile">Path for the output SQL file.</param>
    /// <param name="fromMigration">Optional starting migration (for partial scripts).</param>
    /// <param name="toMigration">Optional last migration to include. Without a from migration, "0" (the empty database) is used as the start.</param>
    [OverloadResolutionPriority(1)] // keeps Script(ctx, path, null) unambiguous against the options form
    public void Script(EfContextRef context, BuildPath outputFile, string? fromMigration = null, string? toMigration = null) =>
        Script(context, outputFile, o => o.From(fromMigration).To(toMigration));

    /// <summary>
    /// Same as the string overload, but the migrations can be resolved when the step runs, so they can use
    /// the Value of an earlier step. A null or empty value means "not set".
    /// Usage: Ef.Script(ctx, out, o => o.From(() => applied.Value).To(() => latest.Value));
    /// </summary>
    public void Script(EfContextRef context, BuildPath outputFile, Action<EfScriptOptions> configure)
    {
        var options = new EfScriptOptions();
        configure(options);
        RegisterCommand("Ef.Script", "dotnet",
            () =>
            {
                var from = options.FromMigration();
                var to = options.ToMigration();
                var hasTo = !string.IsNullOrEmpty(to);
                // `dotnet ef migrations script [from] [to]` is positional: "0" means the empty database.
                var args = new ArgumentBuilder()
                    .Add("ef", "migrations", "script", "--project", context.Project.Path, "-o", outputFile.Value, "--idempotent")
                    .AddIf(context.ContextName != "default", "--context", context.ContextName);
                if (!string.IsNullOrEmpty(from))
                {
                    args.Add(from);
                }
                else if (hasTo)
                {
                    args.Add("0");
                }
                if (hasTo)
                {
                    args.Add(to!);
                }
                return args;
            },
            context.ToString());
    }
}

/// <summary>Start and end migration of Ef.Script, each fixed or resolved when the step runs.</summary>
public sealed class EfScriptOptions
{
    internal Func<string?> FromMigration { get; private set; } = () => null;
    internal Func<string?> ToMigration { get; private set; } = () => null;

    /// <summary>Starts the script after this migration.</summary>
    [OverloadResolutionPriority(1)]
    public EfScriptOptions From(string? migration) { FromMigration = () => migration; return this; }

    /// <summary>Starts the script after the migration returned when the step runs.</summary>
    public EfScriptOptions From(Func<string?> migration) { FromMigration = migration; return this; }

    /// <summary>Ends the script at this migration.</summary>
    [OverloadResolutionPriority(1)]
    public EfScriptOptions To(string? migration) { ToMigration = () => migration; return this; }

    /// <summary>Ends the script at the migration returned when the step runs.</summary>
    public EfScriptOptions To(Func<string?> migration) { ToMigration = migration; return this; }
}
