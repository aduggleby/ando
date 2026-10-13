// =============================================================================
// ModelFingerprint.cs
//
// Summary: Computes a provider-neutral SHA-256 fingerprint of the exported
// tables and columns, used to decide whether an export file can be imported.
//
// Design Decisions:
// - Includes, per table in export order: table name, and per column (sorted by
//   name) the column name, CLR type, nullability, max length, and key
//   membership. These are what the row data depends on
// - Excludes store types (nvarchar vs text), collations, index filters and
//   anything else that legitimately differs between the SQL Server and
//   PostgreSQL contexts, so both providers of the same build agree
// - Two builds with the same exported model produce the same fingerprint even
//   when their migration histories differ (squashed PostgreSQL InitialCreate)
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ando.Server.DataPortability;

/// <summary>
/// Fingerprint of the exported part of the EF model.
/// </summary>
public static class ModelFingerprint
{
    /// <summary>
    /// Builds the canonical text description that is hashed.
    /// </summary>
    /// <param name="tables">Resolved tables in export order.</param>
    /// <returns>One line per table and column.</returns>
    public static string Describe(IEnumerable<PortableTable> tables)
    {
        var sb = new StringBuilder();
        foreach (var table in tables)
        {
            sb.Append("table ").Append(table.Name).Append('\n');
            var keyNames = table.EntityType.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet() ?? [];
            foreach (var property in table.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                sb.Append("  ").Append(property.GetColumnName())
                    .Append(' ').Append(type.FullName)
                    .Append(property.IsNullable ? " null" : " notnull")
                    .Append(" max=").Append(property.GetMaxLength()?.ToString() ?? "-")
                    .Append(keyNames.Contains(property.Name) ? " key" : "")
                    .Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Computes the fingerprint (lowercase hex SHA-256 of <see cref="Describe"/>).
    /// </summary>
    /// <param name="tables">Resolved tables in export order.</param>
    /// <returns>The fingerprint.</returns>
    public static string Compute(IEnumerable<PortableTable> tables) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Describe(tables))));
}
