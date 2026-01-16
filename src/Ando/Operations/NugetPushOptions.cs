// =============================================================================
// NugetPushOptions.cs
//
// Summary: Fluent builder for configuring 'dotnet nuget push' command options.
//
// NugetPushOptions uses a fluent builder pattern for readable configuration
// of push options. Each method returns 'this' to allow chaining.
//
// Example usage:
//   Nuget.Push("./packages/MyPackage.1.0.0.nupkg", o => o
//       .ToNuGetOrg()
//       .WithApiKey(Context.Vars["NUGET_API_KEY"])
//       .SkipDuplicates());
//
// Design Decisions:
// - Fluent builder pattern for readable, chainable configuration
// - Default source is nuget.org (most common use case)
// - API key should typically come from environment variables or secrets
// - SkipDuplicate defaults to true to prevent failures when re-pushing same version
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Fluent builder for configuring 'dotnet nuget push' command options.
/// Methods return 'this' for chaining.
/// </summary>
public class NugetPushOptions
{
    /// <summary>Default NuGet.org API endpoint.</summary>
    public const string NuGetOrgSource = "https://api.nuget.org/v3/index.json";

    /// <summary>NuGet feed source URL.</summary>
    public string? Source { get; private set; }

    /// <summary>API key for authentication with the NuGet feed.</summary>
    public string? ApiKey { get; private set; }

    /// <summary>Skip pushing if package version already exists. Defaults to true.</summary>
    public bool SkipDuplicate { get; private set; } = true;

    /// <summary>Don't push symbol packages (.snupkg).</summary>
    public bool NoSymbols { get; private set; }

    /// <summary>Sets the NuGet feed source URL.</summary>
    public NugetPushOptions ToSource(string source)
    {
        Source = source;
        return this;
    }

    /// <summary>Sets nuget.org as the target feed.</summary>
    public NugetPushOptions ToNuGetOrg()
    {
        Source = NuGetOrgSource;
        return this;
    }

    /// <summary>Sets the API key for authentication.</summary>
    public NugetPushOptions WithApiKey(string apiKey)
    {
        ApiKey = apiKey;
        return this;
    }

    /// <summary>
    /// Skips pushing if the package version already exists on the feed.
    /// Prevents failures when re-running builds that may have already pushed.
    /// </summary>
    public NugetPushOptions SkipDuplicates(bool skip = true)
    {
        SkipDuplicate = skip;
        return this;
    }

    /// <summary>
    /// Don't push symbol packages (.snupkg).
    /// Use when symbols are not needed or handled separately.
    /// </summary>
    public NugetPushOptions WithoutSymbols(bool noSymbols = true)
    {
        NoSymbols = noSymbols;
        return this;
    }
}
