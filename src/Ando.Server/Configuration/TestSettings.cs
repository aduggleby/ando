// =============================================================================
// TestSettings.cs
//
// Summary: Configuration settings for the test API.
//
// Used only in the Testing environment for E2E tests. The API key protects
// test endpoints from unauthorized access even in test environments.
//
// Design Decisions:
// - API key should be set via environment variable
// - Only used when ASPNETCORE_ENVIRONMENT=Testing
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for the test API.
/// </summary>
public class TestSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Test";

    /// <summary>
    /// API key required for test endpoints.
    /// Should be set via environment variable: Test__ApiKey
    /// </summary>
    public string ApiKey { get; set; } = "";
}
