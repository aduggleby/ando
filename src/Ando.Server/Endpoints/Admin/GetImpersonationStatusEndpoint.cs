// =============================================================================
// GetImpersonationStatusEndpoint.cs
//
// Summary: FastEndpoint for checking impersonation status.
//
// Returns whether the current session is impersonating and the original admin.
//
// Design Decisions:
// - Allows anonymous access for any authenticated user
// - Used by frontend to show impersonation banner
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/impersonation-status - Get impersonation status.
/// </summary>
public class GetImpersonationStatusEndpoint : EndpointWithoutRequest<ImpersonationStatusResponse>
{
    private readonly IImpersonationService _impersonationService;

    public GetImpersonationStatusEndpoint(IImpersonationService impersonationService)
    {
        _impersonationService = impersonationService;
    }

    public override void Configure()
    {
        Get("/admin/impersonation-status");
        AllowAnonymous(); // Any authenticated user can check
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // The service only provides OriginalAdminId, not email
        // For now, just return the ID
        await SendAsync(new ImpersonationStatusResponse(
            _impersonationService.IsImpersonating,
            _impersonationService.OriginalAdminId,
            null // Email not available from service
        ), cancellation: ct);
    }
}
