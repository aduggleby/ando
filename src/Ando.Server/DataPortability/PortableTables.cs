// =============================================================================
// PortableTables.cs
//
// Summary: The ordered list of tables that the data export/import moves, with
// per-table metadata and typed query/delete helpers resolved from the EF model.
//
// The order is foreign-key order: every table appears after the tables it
// references, so import can insert in file order and delete in reverse order.
// Hangfire tables are not part of the EF model and are never exported.
//
// Design Decisions:
// - Columns come from IEntityType.GetProperties(), so only mapped properties
//   are exported (computed getters such as Build.ShortCommitSha are not) and
//   value conversions stay the ones EF applies
// - JSON row keys are EF property names, which equal the column names in this
//   model (no column renames are configured)
// - Shadow properties are rejected: they would have no CLR property to read or
//   write, so a future shadow property must be handled explicitly
// - Identity (auto-increment) tables are derived from the model: a single
//   int/long key with ValueGenerated.OnAdd. SystemSettings (ValueGeneratedNever)
//   and the composite-key Identity tables are not identity tables
// - Rows are read with AsNoTracking and ordered by every key column
// =============================================================================

using System.Linq.Expressions;
using System.Reflection;
using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ando.Server.DataPortability;

/// <summary>
/// Metadata and typed operations for one exported table.
/// </summary>
public sealed class PortableTable
{
    private readonly Func<AndoDbContext, CancellationToken, IAsyncEnumerable<object>> _queryOrdered;
    private readonly Func<AndoDbContext, CancellationToken, Task<int>> _deleteAll;
    private readonly Func<AndoDbContext, CancellationToken, Task<long>> _count;

    private PortableTable(
        string name,
        IEntityType entityType,
        IReadOnlyList<IProperty> properties,
        IProperty? identityKey,
        Func<AndoDbContext, CancellationToken, IAsyncEnumerable<object>> queryOrdered,
        Func<AndoDbContext, CancellationToken, Task<int>> deleteAll,
        Func<AndoDbContext, CancellationToken, Task<long>> count)
    {
        Name = name;
        EntityType = entityType;
        Properties = properties;
        IdentityKey = identityKey;
        _queryOrdered = queryOrdered;
        _deleteAll = deleteAll;
        _count = count;
    }

    /// <summary>
    /// Database table name (also the "table" value in the export file).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// EF entity type mapped to the table.
    /// </summary>
    public IEntityType EntityType { get; }

    /// <summary>
    /// CLR type of the entity.
    /// </summary>
    public Type ClrType => EntityType.ClrType;

    /// <summary>
    /// Mapped properties (columns) in model order.
    /// </summary>
    public IReadOnlyList<IProperty> Properties { get; }

    /// <summary>
    /// The auto-increment key column, or null when the table has no identity column.
    /// </summary>
    public IProperty? IdentityKey { get; }

    /// <summary>
    /// Streams all rows ordered by key without tracking.
    /// </summary>
    /// <param name="db">The context to read from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entities in key order.</returns>
    public IAsyncEnumerable<object> QueryOrdered(AndoDbContext db, CancellationToken cancellationToken) =>
        _queryOrdered(db, cancellationToken);

    /// <summary>
    /// Deletes every row of the table in the database.
    /// </summary>
    /// <param name="db">The context to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of deleted rows.</returns>
    public Task<int> DeleteAllAsync(AndoDbContext db, CancellationToken cancellationToken) =>
        _deleteAll(db, cancellationToken);

    /// <summary>
    /// Counts the rows of the table in the database.
    /// </summary>
    /// <param name="db">The context to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The row count.</returns>
    public Task<long> CountAsync(AndoDbContext db, CancellationToken cancellationToken) =>
        _count(db, cancellationToken);

    /// <summary>
    /// Creates the table descriptor for entity type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Entity CLR type.</typeparam>
    /// <param name="model">The EF model.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the entity cannot be exported safely.</exception>
    internal static PortableTable Create<T>(IModel model) where T : class
    {
        var entityType = model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not part of the model.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(T).Name} is not mapped to a table.");

        var properties = entityType.GetProperties().ToList();
        var shadow = properties.FirstOrDefault(p => p.IsShadowProperty() || p.PropertyInfo == null);
        if (shadow != null)
        {
            throw new InvalidOperationException(
                $"{tableName}.{shadow.Name} is a shadow property; the data export does not support it.");
        }

        var unsupported = properties.FirstOrDefault(p => !PortableValueSerializer.IsSupported(p.ClrType));
        if (unsupported != null)
        {
            throw new InvalidOperationException(
                $"{tableName}.{unsupported.Name} has type {unsupported.ClrType.Name}, which the data export does not support.");
        }

        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"{tableName} has no primary key.");
        var keyNames = key.Properties.Select(p => p.Name).ToArray();

        IProperty? identityKey = null;
        if (key.Properties.Count == 1
            && key.Properties[0].ValueGenerated == ValueGenerated.OnAdd
            && key.Properties[0].ClrType is var keyType
            && (keyType == typeof(int) || keyType == typeof(long)))
        {
            identityKey = key.Properties[0];
        }

        return new PortableTable(
            tableName,
            entityType,
            properties,
            identityKey,
            (db, ct) => QueryOrderedCore<T>(db, keyNames, ct),
            (db, ct) => db.Set<T>().ExecuteDeleteAsync(ct),
            (db, ct) => db.Set<T>().LongCountAsync(ct));
    }

    private static async IAsyncEnumerable<object> QueryOrderedCore<T>(
        AndoDbContext db,
        string[] keyNames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) where T : class
    {
        IQueryable<T> query = db.Set<T>().AsNoTracking();
        for (var i = 0; i < keyNames.Length; i++)
        {
            query = ApplyOrder(query, keyNames[i], first: i == 0);
        }

        await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return entity;
        }
    }

    private static readonly MethodInfo OrderByMethod = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2);

    private static readonly MethodInfo ThenByMethod = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.ThenBy) && m.GetParameters().Length == 2);

    private static IQueryable<T> ApplyOrder<T>(IQueryable<T> source, string propertyName, bool first)
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var body = Expression.Property(parameter, propertyName);
        var lambda = Expression.Lambda(body, parameter);
        var method = (first ? OrderByMethod : ThenByMethod).MakeGenericMethod(typeof(T), body.Type);
        return (IQueryable<T>)method.Invoke(null, [source, lambda])!;
    }
}

/// <summary>
/// The tables moved by the data export/import, in foreign-key order.
/// </summary>
public static class PortableTables
{
    /// <summary>
    /// Expected table names in export order. Checked against the model by <see cref="Resolve"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> TableNames =
    [
        "AspNetRoles",
        "AspNetUsers",
        "AspNetUserRoles",
        "AspNetUserClaims",
        "AspNetRoleClaims",
        "AspNetUserLogins",
        "AspNetUserTokens",
        "Projects",
        "ProjectSecrets",
        "Builds",
        "BuildLogEntries",
        "BuildArtifacts",
        "ApiTokens",
        "SystemSettings"
    ];

    /// <summary>
    /// Resolves the table descriptors from the context's model, in export order.
    /// </summary>
    /// <param name="model">The EF model of an <see cref="AndoDbContext"/>.</param>
    /// <returns>The descriptors in foreign-key order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the model's table names differ from <see cref="TableNames"/>.</exception>
    public static IReadOnlyList<PortableTable> Resolve(IModel model)
    {
        IReadOnlyList<PortableTable> tables =
        [
            PortableTable.Create<ApplicationRole>(model),
            PortableTable.Create<ApplicationUser>(model),
            PortableTable.Create<IdentityUserRole<int>>(model),
            PortableTable.Create<IdentityUserClaim<int>>(model),
            PortableTable.Create<IdentityRoleClaim<int>>(model),
            PortableTable.Create<IdentityUserLogin<int>>(model),
            PortableTable.Create<IdentityUserToken<int>>(model),
            PortableTable.Create<Project>(model),
            PortableTable.Create<ProjectSecret>(model),
            PortableTable.Create<Build>(model),
            PortableTable.Create<BuildLogEntry>(model),
            PortableTable.Create<BuildArtifact>(model),
            PortableTable.Create<ApiToken>(model),
            PortableTable.Create<SystemSettings>(model)
        ];

        var actual = tables.Select(t => t.Name).ToList();
        if (!actual.SequenceEqual(TableNames))
        {
            throw new InvalidOperationException(
                $"Exported table list does not match the model: expected {string.Join(", ", TableNames)}; " +
                $"model has {string.Join(", ", actual)}.");
        }

        // Every entity type mapped to a table must be exported, so a new table
        // cannot be silently left behind by a migration.
        var missing = model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n != null && !TableNames.Contains(n))
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Tables in the model are not covered by the data export: {string.Join(", ", missing)}.");
        }

        return tables;
    }
}
