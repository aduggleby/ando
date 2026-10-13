// =============================================================================
// DatabaseTestProviders.cs
//
// Summary: Selects which database providers the container-backed integration
// tests run against, from the ANDO_TEST_DB_PROVIDERS environment variable.
//
// The container-backed suites exist once per provider (SqlServer and
// PostgreSql collections over shared abstract bases). This class decides,
// per provider, whether the fixture starts its container and whether the
// tests run or are reported as skipped.
//
// Design Decisions:
// - Default (unset or empty) is BOTH providers, so CI and a plain
//   `dotnet test` cover the full matrix; set one provider to shorten local runs
// - Parsing is lazy and never happens in a static initializer, so a bad value
//   fails the affected tests with a clear message instead of a
//   TypeInitializationException
// - Unknown provider names throw rather than being ignored, so a typo cannot
//   silently skip a whole provider
// =============================================================================

using Ando.Server.Configuration;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// Reads the provider selection for container-backed integration tests.
/// </summary>
public static class DatabaseTestProviders
{
    /// <summary>
    /// Environment variable holding a comma-separated provider list (e.g. <c>SqlServer,PostgreSql</c>).
    /// </summary>
    public const string EnvironmentVariable = "ANDO_TEST_DB_PROVIDERS";

    /// <summary>
    /// Returns the selected providers for the current process environment.
    /// </summary>
    /// <returns>The distinct selected providers.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the variable names an unknown provider.</exception>
    public static IReadOnlySet<DatabaseProvider> GetSelected()
    {
        return Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));
    }

    /// <summary>
    /// Returns whether tests for <paramref name="provider"/> should run.
    /// </summary>
    /// <param name="provider">The provider to check.</param>
    /// <returns>True when the provider is selected.</returns>
    public static bool IsSelected(DatabaseProvider provider) => GetSelected().Contains(provider);

    /// <summary>
    /// Builds the skip reason shown for tests of a deselected provider.
    /// </summary>
    /// <param name="provider">The deselected provider.</param>
    /// <returns>A message naming the variable and how to enable the provider.</returns>
    public static string SkipReason(DatabaseProvider provider) =>
        $"{provider} is not selected by {EnvironmentVariable} " +
        $"(current: '{Environment.GetEnvironmentVariable(EnvironmentVariable)}'; default: SqlServer,PostgreSql).";

    /// <summary>
    /// Parses a provider list. Null or blank means all providers.
    /// </summary>
    /// <param name="value">Comma-separated provider names (case-insensitive).</param>
    /// <returns>The distinct providers named in <paramref name="value"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown for an unknown provider name.</exception>
    public static IReadOnlySet<DatabaseProvider> Parse(string? value)
    {
        var all = new HashSet<DatabaseProvider> { DatabaseProvider.SqlServer, DatabaseProvider.PostgreSql };
        if (string.IsNullOrWhiteSpace(value))
        {
            return all;
        }

        var selected = new HashSet<DatabaseProvider>();
        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            // Enum.TryParse also accepts numeric strings; reject those explicitly.
            if (!Enum.TryParse<DatabaseProvider>(entry, ignoreCase: true, out var provider)
                || !Enum.IsDefined(provider)
                || char.IsDigit(entry[0]))
            {
                throw new InvalidOperationException(
                    $"{EnvironmentVariable} contains unknown database provider '{entry}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<DatabaseProvider>())} (comma-separated).");
            }

            selected.Add(provider);
        }

        // A value with only separators (e.g. ",") selects nothing; treat it as unset.
        return selected.Count == 0 ? all : selected;
    }
}
