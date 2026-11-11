// =============================================================================
// ArchiveOptions.cs
//
// Summary: Fluent options for Archive.Zip and Archive.TarGz.
//
// Design Decisions:
// - The timestamp is explicit and defaults to a fixed value, never "now", so the
//   same tree always produces the same bytes.
// - Compression level is fixed (default 6) because the level changes the bytes.
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Options for <see cref="ArchiveOperations.Zip"/> and <see cref="ArchiveOperations.TarGz"/>.
/// </summary>
public class ArchiveOptions
{
    /// <summary>1980-01-01T00:00:00Z, the earliest time a zip file can store.</summary>
    public const long DefaultTimestamp = 315532800;

    /// <summary>Modification time (Unix seconds) set on every entry.</summary>
    public long Timestamp { get; private set; } = DefaultTimestamp;

    /// <summary>Deflate level 1-9 (default 6).</summary>
    public int CompressionLevel { get; private set; } = 6;

    /// <summary>
    /// Sets the modification time of every entry, in Unix seconds (default
    /// 315532800 = 1980-01-01T00:00:00Z). Zip stores times with 2 second resolution
    /// and cannot represent times before 1980. A good reproducible choice is
    /// <c>Git.CommitTime</c>.
    /// </summary>
    public ArchiveOptions WithTimestamp(long unixSeconds)
    {
        if (unixSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unixSeconds), "Timestamp must not be negative.");
        }
        Timestamp = unixSeconds;
        return this;
    }

    /// <summary>Sets the deflate level 1-9 (default 6). Different levels produce different bytes.</summary>
    public ArchiveOptions WithCompressionLevel(int level)
    {
        if (level is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Compression level must be between 1 and 9.");
        }
        CompressionLevel = level;
        return this;
    }
}
