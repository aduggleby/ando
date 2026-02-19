// =============================================================================
// RateLimitSettings.cs
//
// Summary: Configuration settings for rate limiting.
//
// Controls rate limiting for various endpoints to prevent abuse.
// Uses ASP.NET Core's built-in rate limiting middleware.
//
// Design Decisions:
// - Configurable per-endpoint limits
// - Webhook endpoint has separate limits (public, unauthenticated)
// - API endpoints have higher limits for authenticated users
// - Uses sliding window algorithm for smooth rate limiting
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public class RateLimitSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Whether rate limiting is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Rate limit settings for the webhook endpoint (unauthenticated).
    /// </summary>
    public EndpointRateLimit Webhook { get; set; } = new()
    {
        PermitLimit = 100,
        WindowSeconds = 60,
        QueueLimit = 10
    };

    /// <summary>
    /// Rate limit settings for authenticated API endpoints.
    /// </summary>
    public EndpointRateLimit Api { get; set; } = new()
    {
        PermitLimit = 1000,
        WindowSeconds = 60,
        QueueLimit = 50
    };

    /// <summary>
    /// Legacy/default auth rate limit settings.
    /// Used as fallback for newer auth-specific policies.
    /// </summary>
    public EndpointRateLimit Auth { get; set; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60,
        QueueLimit = 2
    };

    /// <summary>
    /// Stricter limits for sensitive auth operations (login/register/password reset).
    /// </summary>
    public EndpointRateLimit AuthSensitive { get; set; } = new()
    {
        PermitLimit = 6,
        WindowSeconds = 60,
        QueueLimit = 0
    };

    /// <summary>
    /// Moderate limits for email verification-related auth operations.
    /// </summary>
    public EndpointRateLimit AuthVerification { get; set; } = new()
    {
        PermitLimit = 20,
        WindowSeconds = 60,
        QueueLimit = 5
    };
}

/// <summary>
/// Rate limit configuration for a specific endpoint category.
/// </summary>
public class EndpointRateLimit
{
    /// <summary>
    /// Maximum number of requests allowed in the time window.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Time window in seconds for the rate limit.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of requests to queue when limit is reached.
    /// Requests beyond this are rejected immediately.
    /// </summary>
    public int QueueLimit { get; set; } = 10;
}
