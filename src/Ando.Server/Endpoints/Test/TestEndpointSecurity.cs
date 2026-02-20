// =============================================================================
// TestEndpointSecurity.cs
//
// Summary: Shared guard helpers and API-key-protected endpoint base classes for
// test-only endpoints.
//
// This keeps security checks centralized so individual test endpoints can focus
// on behavior while preserving identical authorization semantics.
// =============================================================================

using System.Text;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Ando.Server.Endpoints.Test;

internal static class TestEndpointGuard
{
    internal static bool IsTestEnvironment(IWebHostEnvironment env)
    {
        return env.IsEnvironment("Testing") || env.IsEnvironment("E2E");
    }

    internal static void EnsureTestEnvironmentOrThrow(IWebHostEnvironment env)
    {
        // Intentionally no-op at startup. Environment enforcement happens per-request
        // to avoid crashing the whole app if test endpoints are present in other envs.
        _ = env;
    }

    internal static void EnsureApiKeyConfiguredOrThrow(string apiKey)
    {
        // Intentionally no-op at startup. Missing key is handled per-request.
        _ = apiKey;
    }

    internal static bool HasValidApiKey(HttpContext context, string configuredApiKey)
    {
        var apiKey = context.Request.Headers["X-Test-Api-Key"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(apiKey) && apiKey == configuredApiKey;
    }

    internal static string GenerateTestAuthToken(ApplicationUser user)
    {
        return Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{user.Id}:{user.EffectiveDisplayName}:{DateTime.UtcNow.Ticks}"));
    }
}

/// <summary>
/// Base endpoint for test routes that require a valid test API key.
/// </summary>
public abstract class ApiKeyProtectedTestEndpoint<TRequest> : Endpoint<TRequest, object>
    where TRequest : notnull
{
    /// <summary>
    /// Configured test API key from application settings.
    /// </summary>
    protected abstract string TestApiKey { get; }

    /// <summary>
    /// Validates the test API key and writes a 401 response when invalid.
    /// </summary>
    protected async Task<bool> EnsureApiKeyAsync(CancellationToken ct)
    {
        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!TestEndpointGuard.IsTestEnvironment(env))
        {
            await SendNotFoundAsync(ct);
            return false;
        }

        if (string.IsNullOrWhiteSpace(TestApiKey))
        {
            await SendAsync(new { error = "Test API key is not configured." }, 503, ct);
            return false;
        }

        if (TestEndpointGuard.HasValidApiKey(HttpContext, TestApiKey))
        {
            return true;
        }

        await SendAsync(new { error = "Invalid API key" }, 401, ct);
        return false;
    }
}

/// <summary>
/// Base endpoint for test routes (no request body) that require a valid test API key.
/// </summary>
public abstract class ApiKeyProtectedTestEndpointWithoutRequest : EndpointWithoutRequest<object>
{
    /// <summary>
    /// Configured test API key from application settings.
    /// </summary>
    protected abstract string TestApiKey { get; }

    /// <summary>
    /// Validates the test API key and writes a 401 response when invalid.
    /// </summary>
    protected async Task<bool> EnsureApiKeyAsync(CancellationToken ct)
    {
        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!TestEndpointGuard.IsTestEnvironment(env))
        {
            await SendNotFoundAsync(ct);
            return false;
        }

        if (string.IsNullOrWhiteSpace(TestApiKey))
        {
            await SendAsync(new { error = "Test API key is not configured." }, 503, ct);
            return false;
        }

        if (TestEndpointGuard.HasValidApiKey(HttpContext, TestApiKey))
        {
            return true;
        }

        await SendAsync(new { error = "Invalid API key" }, 401, ct);
        return false;
    }
}
