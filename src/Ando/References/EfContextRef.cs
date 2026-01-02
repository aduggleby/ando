// =============================================================================
// EfContextRef.cs
//
// Summary: Represents a reference to an Entity Framework DbContext.
//
// EfContextRef combines a project reference with a DbContext class name,
// enabling EF operations to target specific contexts in multi-context projects.
//
// Example usage:
//   var db = Ef.DbContextFrom(project, "ApplicationDbContext");
//   Ef.DatabaseUpdate(db);
//   Ef.Script(db, Context.Paths.Artifacts / "migration.sql");
//
// Design Decisions:
// - Created via EfOperations.DbContextFrom() factory method (internal constructor)
// - "default" context name means single-context project (no --context flag needed)
// - ToString() provides readable logging: "MyProject" or "MyProject:MyContext"
// =============================================================================

namespace Ando.References;

/// <summary>
/// Represents a reference to an Entity Framework DbContext within a project.
/// Used with EfOperations for database migration and schema management.
/// </summary>
public class EfContextRef
{
    /// <summary>The project containing the DbContext.</summary>
    public ProjectRef Project { get; }

    /// <summary>
    /// The DbContext class name.
    /// "default" indicates a single-context project where the class name is inferred.
    /// </summary>
    public string ContextName { get; }

    // Internal constructor - created via EfOperations.DbContextFrom().
    internal EfContextRef(ProjectRef project, string contextName = "default")
    {
        Project = project;
        ContextName = contextName;
    }

    // Provides readable string for logging and step names.
    // Single context: "MyProject", Multiple contexts: "MyProject:MyContext"
    public override string ToString() =>
        ContextName == "default" ? Project.Name : $"{Project.Name}:{ContextName}";
}
