// =============================================================================
// UtcDateTimeConverter.cs
//
// Summary: EF Core value converter that makes every DateTime the model stores
// and reads carry DateTimeKind.Utc.
//
// Registered model-wide in AndoDbContext.ConfigureConventions for DateTime and
// DateTime? on every provider. PostgreSQL maps DateTime to
// `timestamp with time zone` and Npgsql rejects writes whose Kind is not Utc;
// SQL Server stores `datetime2` without a Kind and returns Unspecified. The
// converter gives both providers the same contract: values are UTC on the way
// in and tagged UTC on the way out, so API responses serialize with `Z`.
//
// Design Decisions:
// - Kind == Utc is stored as is.
// - Kind == Local is converted with ToUniversalTime(), which changes the
//   instant representation correctly; SpecifyKind would only relabel it and
//   silently shift the stored instant by the server's offset.
// - Kind == Unspecified is tagged as Utc with SpecifyKind rather than rejected.
//   This preserves today's SQL Server semantics, where Unspecified values are
//   stored verbatim and the whole code base treats every stored value as UTC.
//   The only sources of Unspecified values are bare timestamps from
//   deserialized DTOs and webhook payloads; rejecting them would turn existing
//   working writes into runtime failures on both providers.
// - Values read from the provider are always tagged Utc.
// - One non-nullable converter serves DateTime? too: EF Core never passes null
//   into a value converter, it handles nulls around it.
// - No schema impact: the converter's provider type is DateTime, so column
//   types and migrations are unchanged.
// =============================================================================

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ando.Server.Data.Conventions;

/// <summary>
/// Converts <see cref="DateTime"/> values to UTC before they are written and tags
/// values read from the database as <see cref="DateTimeKind.Utc"/>.
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    /// <summary>
    /// Creates the converter. Parameterless so it can be registered with
    /// <c>HaveConversion&lt;UtcDateTimeConverter&gt;()</c>.
    /// </summary>
    public UtcDateTimeConverter()
        : base(v => ToProvider(v), v => FromProvider(v))
    {
    }

    /// <summary>
    /// Normalizes a value before it is written: Utc passes through, Local is
    /// converted to UTC, Unspecified is tagged as UTC without shifting.
    /// </summary>
    /// <param name="value">The value supplied by application code.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    public static DateTime ToProvider(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Tags a value read from the database as UTC.
    /// </summary>
    /// <param name="value">The value returned by the provider.</param>
    /// <returns>The value with <see cref="DateTimeKind.Utc"/>.</returns>
    public static DateTime FromProvider(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
