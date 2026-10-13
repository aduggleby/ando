// =============================================================================
// DatabaseTestContainerMode.cs
//
// Summary: Selects how the container-backed fixtures own their database
// containers, from the ANDO_TEST_DB_CONTAINERS environment variable.
//
// - shared (default on developer machines): fixed container names and host ports
//   (ando-server-tests-sqlserver on 11433, ando-server-tests-postgres on
//   17135); an existing container with that name is replaced at start and the
//   container is left running afterwards for data inspection. Only one test
//   run per Docker daemon at a time.
// - isolated (default when CI or ANDO_HOST_ROOT is set, or inside a container): a unique container name per run, a random free host port, and
//   automatic removal when the run ends. Use it in CI or wherever several runs
//   share a Docker daemon, so runs never remove each other's containers.
//
// Design Decisions:
// - Parsing is lazy and unknown values throw, so a typo fails loudly instead
//   of silently falling back to shared mode
// =============================================================================

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// How a database fixture owns its Docker container.
/// </summary>
public enum DatabaseContainerMode
{
    /// <summary>
    /// Fixed name and port, replaced at start, left running afterwards.
    /// </summary>
    Shared,

    /// <summary>
    /// Unique name, random host port, removed when the run ends.
    /// </summary>
    Isolated
}

/// <summary>
/// Reads the container mode for the container-backed integration tests.
/// </summary>
public static class DatabaseTestContainerMode
{
    /// <summary>
    /// Environment variable holding the mode (<c>shared</c> or <c>isolated</c>).
    /// </summary>
    public const string EnvironmentVariable = "ANDO_TEST_DB_CONTAINERS";

    /// <summary>
    /// Returns the mode for the current process environment.
    /// </summary>
    /// <returns>The selected mode.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the variable holds an unknown value.</exception>
    public static DatabaseContainerMode GetCurrent()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Parse(configured);
        }

        // Unset: shared mode for developer machines, isolated mode on CI runners. A CI runner is
        // recognised by the conventional CI variable or by ANDO_HOST_ROOT, which Ando.Server sets
        // for builds it executes. Shared fixed names and ports would otherwise let concurrent
        // builds on one Docker daemon remove each other's database containers.
        return IsCiEnvironment() ? DatabaseContainerMode.Isolated : DatabaseContainerMode.Shared;
    }

    /// <summary>
    /// True when the process looks like a CI build (<c>CI</c> or <c>ANDO_HOST_ROOT</c> is set, or the process runs inside a container).
    /// </summary>
    /// <returns>Whether the isolated default applies.</returns>
    public static bool IsCiEnvironment() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANDO_HOST_ROOT"))
        // The ANDO build container does not forward host variables, so a test process that
        // runs inside any container (shared Docker daemon) also defaults to isolated mode.
        || File.Exists("/.dockerenv");

    /// <summary>
    /// Parses a mode value. Null or blank means <see cref="DatabaseContainerMode.Shared"/>.
    /// </summary>
    /// <param name="value">The value (case-insensitive).</param>
    /// <returns>The parsed mode.</returns>
    /// <exception cref="InvalidOperationException">Thrown for an unknown value.</exception>
    public static DatabaseContainerMode Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "shared" => DatabaseContainerMode.Shared,
            "isolated" => DatabaseContainerMode.Isolated,
            _ => throw new InvalidOperationException(
                $"{EnvironmentVariable} has unknown value '{value}'. Valid values: shared (default), isolated.")
        };
    }
}
