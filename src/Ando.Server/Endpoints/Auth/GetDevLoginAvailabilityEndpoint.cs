// =============================================================================
// GetDevLoginAvailabilityEndpoint.cs
//
// Summary: Exposes whether the development login shortcut should be shown.
//
// The SPA uses this endpoint to decide if the "Development Login" button should
// be visible. This avoids brittle hostname checks and ensures visibility tracks
// the server's actual environment configuration.
//
// Design Decisions:
// - Anonymous endpoint because it is only environment metadata
// - Returns a minimal response payload for simple client checks
// - Uses server-side environment truth as the source of record
// =============================================================================

using Ando.Server.Contracts.Auth;
using FastEndpoints;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// GET /api/auth/dev-login-availability - Indicates whether dev login is enabled.
/// </summary>
public class GetDevLoginAvailabilityEndpoint : EndpointWithoutRequest<DevLoginAvailabilityResponse>
{
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Initializes the endpoint with access to host environment metadata.
    /// </summary>
    public GetDevLoginAvailabilityEndpoint(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Get("/auth/dev-login-availability");
        AllowAnonymous();
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendAsync(new DevLoginAvailabilityResponse(_environment.IsDevelopment()), cancellation: ct);
    }
}
