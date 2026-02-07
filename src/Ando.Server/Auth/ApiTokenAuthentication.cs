// =============================================================================
// ApiTokenAuthentication.cs
//
// Summary: API token authentication scheme definitions for Ando.Server.
// =============================================================================

namespace Ando.Server.Auth;

public static class ApiTokenAuthentication
{
    public const string Scheme = "ApiToken";
    public const string PolicyScheme = "AndoAuth";

    // Supported header names:
    // - Authorization: Bearer <token>
    // - X-Api-Token: <token>
    public const string ApiTokenHeaderName = "X-Api-Token";
}

