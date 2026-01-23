// =============================================================================
// VersionResolver.cs
//
// Summary: Fetches latest SDK/runtime versions from official APIs.
//
// This class dynamically resolves the latest versions of .NET SDK, Node.js LTS,
// and npm from their official release APIs. Results are cached per-build instance
// to avoid repeated API calls.
//
// API Sources:
// - .NET: https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json
// - Node: https://nodejs.org/dist/index.json (filtered by lts field)
// - npm: https://registry.npmjs.org/npm (dist-tags.latest)
//
// Design Decisions:
// - Uses caching to avoid repeated API calls within a single build
// - Provides fallback versions if API calls fail (network issues, etc.)
// - Logs debug messages for API calls and info for fallback usage
// - Uses async/await for non-blocking HTTP calls
// =============================================================================

using System.Text.Json;
using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Resolves latest SDK/runtime versions from official release APIs.
/// Caches results to avoid repeated API calls within a build session.
/// </summary>
public class VersionResolver
{
    private readonly HttpClient _httpClient;
    private readonly IMessageLogger _logger;

    // Cached versions (per-build instance).
    private string? _dotnetVersion;
    private string? _nodeVersion;
    private string? _npmVersion;

    // Fallback versions if API fails.
    private const string FallbackDotnet = "9.0";
    private const string FallbackNode = "22";
    private const string FallbackNpm = "latest";

    // API endpoints.
    private const string DotnetReleasesUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";
    private const string NodeReleasesUrl = "https://nodejs.org/dist/index.json";
    private const string NpmRegistryUrl = "https://registry.npmjs.org/npm";

    public VersionResolver(HttpClient httpClient, IMessageLogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the latest .NET SDK version from Microsoft's release metadata.
    /// Returns the channel version (e.g., "10.0") for the latest stable release.
    /// </summary>
    public async Task<string> GetLatestDotnetSdkVersionAsync()
    {
        if (_dotnetVersion != null)
        {
            return _dotnetVersion;
        }

        try
        {
            _logger.Debug("Fetching latest .NET SDK version from release metadata...");
            var response = await _httpClient.GetStringAsync(DotnetReleasesUrl);
            var json = JsonDocument.Parse(response);

            // Find the latest stable release (highest support-phase = "active" or "maintenance").
            // The releases-index.json contains an array of release channels.
            var releases = json.RootElement.GetProperty("releases-index");
            string? latestVersion = null;

            foreach (var release in releases.EnumerateArray())
            {
                var supportPhase = release.GetProperty("support-phase").GetString();
                // Look for active or go-live (preview before stable) releases.
                if (supportPhase == "active" || supportPhase == "go-live")
                {
                    var channelVersion = release.GetProperty("channel-version").GetString();
                    if (channelVersion != null)
                    {
                        // Take the highest version number.
                        if (latestVersion == null || CompareVersions(channelVersion, latestVersion) > 0)
                        {
                            latestVersion = channelVersion;
                        }
                    }
                }
            }

            if (latestVersion != null)
            {
                _dotnetVersion = latestVersion;
                _logger.Debug($"Latest .NET SDK version: {_dotnetVersion}");
                return _dotnetVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to fetch .NET SDK version: {ex.Message}");
        }

        _logger.Info($"Using fallback .NET SDK version: {FallbackDotnet}");
        _dotnetVersion = FallbackDotnet;
        return _dotnetVersion;
    }

    /// <summary>
    /// Gets the latest Node.js LTS version from nodejs.org.
    /// Returns the major version number (e.g., "22").
    /// </summary>
    public async Task<string> GetLatestNodeLtsVersionAsync()
    {
        if (_nodeVersion != null)
        {
            return _nodeVersion;
        }

        try
        {
            _logger.Debug("Fetching latest Node.js LTS version...");
            var response = await _httpClient.GetStringAsync(NodeReleasesUrl);
            var json = JsonDocument.Parse(response);

            // Node releases are sorted by date descending. Find first with lts field set.
            foreach (var release in json.RootElement.EnumerateArray())
            {
                var lts = release.GetProperty("lts");
                // lts is either false or a string like "Jod" (the codename).
                if (lts.ValueKind == JsonValueKind.String)
                {
                    var version = release.GetProperty("version").GetString();
                    if (version != null && version.StartsWith("v"))
                    {
                        // Extract major version from "v22.12.0" -> "22".
                        var majorVersion = version.Substring(1).Split('.')[0];
                        _nodeVersion = majorVersion;
                        _logger.Debug($"Latest Node.js LTS version: {_nodeVersion}");
                        return _nodeVersion;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to fetch Node.js LTS version: {ex.Message}");
        }

        _logger.Info($"Using fallback Node.js version: {FallbackNode}");
        _nodeVersion = FallbackNode;
        return _nodeVersion;
    }

    /// <summary>
    /// Gets the latest npm version from the npm registry.
    /// </summary>
    public async Task<string> GetLatestNpmVersionAsync()
    {
        if (_npmVersion != null)
        {
            return _npmVersion;
        }

        try
        {
            _logger.Debug("Fetching latest npm version...");
            var response = await _httpClient.GetStringAsync(NpmRegistryUrl);
            var json = JsonDocument.Parse(response);

            // Get the latest version from dist-tags.
            var distTags = json.RootElement.GetProperty("dist-tags");
            var latest = distTags.GetProperty("latest").GetString();

            if (latest != null)
            {
                _npmVersion = latest;
                _logger.Debug($"Latest npm version: {_npmVersion}");
                return _npmVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to fetch npm version: {ex.Message}");
        }

        _logger.Info($"Using fallback npm version: {FallbackNpm}");
        _npmVersion = FallbackNpm;
        return _npmVersion;
    }

    /// <summary>
    /// Compares two version strings (e.g., "10.0" vs "9.0").
    /// Returns positive if v1 > v2, negative if v1 < v2, zero if equal.
    /// </summary>
    private static int CompareVersions(string v1, string v2)
    {
        var parts1 = v1.Split('.').Select(int.Parse).ToArray();
        var parts2 = v2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length ? parts1[i] : 0;
            var p2 = i < parts2.Length ? parts2[i] : 0;
            if (p1 != p2)
            {
                return p1 - p2;
            }
        }

        return 0;
    }

    // For testing: allow clearing the cache.
    internal void ClearCache()
    {
        _dotnetVersion = null;
        _nodeVersion = null;
        _npmVersion = null;
    }
}
