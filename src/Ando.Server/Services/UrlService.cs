// =============================================================================
// UrlService.cs
//
// Summary: Provides URL generation for the application.
//
// Centralizes URL generation using the configured Server:BaseUrl setting.
// This is necessary because the application runs in a container behind a
// reverse proxy and cannot determine its external URL from HTTP headers.
//
// Design Decisions:
// - Uses configured BaseUrl for production environments
// - Falls back to request-based URL for development (convenience)
// - Removes trailing slashes from BaseUrl for consistent concatenation
// =============================================================================

using Ando.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Service for generating application URLs.
/// </summary>
public interface IUrlService
{
    /// <summary>
    /// Gets the base URL for the application (e.g., "https://ci.example.com").
    /// </summary>
    /// <param name="httpContext">Optional HTTP context for development fallback.</param>
    /// <returns>The base URL without trailing slash.</returns>
    string GetBaseUrl(HttpContext? httpContext = null);

    /// <summary>
    /// Builds a full URL from a relative path.
    /// </summary>
    /// <param name="relativePath">The relative path (e.g., "/auth/verify-email").</param>
    /// <param name="httpContext">Optional HTTP context for development fallback.</param>
    /// <returns>The full URL.</returns>
    string BuildUrl(string relativePath, HttpContext? httpContext = null);
}

/// <summary>
/// Default implementation of <see cref="IUrlService"/>.
/// </summary>
public class UrlService : IUrlService
{
    private readonly ServerSettings _serverSettings;
    private readonly IWebHostEnvironment _environment;

    public UrlService(
        IOptions<ServerSettings> serverSettings,
        IWebHostEnvironment environment)
    {
        _serverSettings = serverSettings.Value;
        _environment = environment;
    }

    /// <inheritdoc />
    public string GetBaseUrl(HttpContext? httpContext = null)
    {
        // Use configured BaseUrl if available
        if (!string.IsNullOrEmpty(_serverSettings.BaseUrl))
        {
            return _serverSettings.BaseUrl.TrimEnd('/');
        }

        // Fall back to request-based URL for non-production environments.
        // Production should always set Server:BaseUrl (validated on startup) to avoid
        // generating internal/container hostnames behind reverse proxies.
        if (httpContext != null && !_environment.IsProduction())
        {
            return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        }

        // This shouldn't happen in production (ConfigurationValidator ensures BaseUrl is set)
        throw new InvalidOperationException(
            "Server:BaseUrl is not configured. Set via environment variable: Server__BaseUrl");
    }

    /// <inheritdoc />
    public string BuildUrl(string relativePath, HttpContext? httpContext = null)
    {
        var baseUrl = GetBaseUrl(httpContext);
        var path = relativePath.StartsWith('/') ? relativePath : "/" + relativePath;
        return baseUrl + path;
    }
}
