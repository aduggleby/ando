// =============================================================================
// RevokeApiTokenEndpoint.cs
//
// Summary: Revoke a personal API token owned by the authenticated user.
//
// DELETE /api/auth/tokens/{id}
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Auth;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Auth;

public class RevokeApiTokenEndpoint : EndpointWithoutRequest<RevokeApiTokenResponse>
{
    private readonly IApiTokenService _apiTokenService;

    public RevokeApiTokenEndpoint(IApiTokenService apiTokenService)
    {
        _apiTokenService = apiTokenService;
    }

    public override void Configure()
    {
        Delete("/auth/tokens/{id}");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId <= 0)
        {
            await SendAsync(new RevokeApiTokenResponse(false), 401, ct);
            return;
        }

        var tokenId = Route<int>("id");
        await _apiTokenService.RevokeAsync(userId, tokenId, ct);

        await SendAsync(new RevokeApiTokenResponse(true), cancellation: ct);
    }
}

