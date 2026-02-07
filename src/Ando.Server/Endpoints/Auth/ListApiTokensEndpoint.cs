// =============================================================================
// ListApiTokensEndpoint.cs
//
// Summary: Lists personal API tokens for the authenticated user.
//
// GET /api/auth/tokens
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Auth;
using Ando.Server.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Auth;

public class ListApiTokensEndpoint : EndpointWithoutRequest<ListApiTokensResponse>
{
    private readonly AndoDbContext _db;

    public ListApiTokensEndpoint(AndoDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/auth/tokens");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId <= 0)
        {
            await SendAsync(new ListApiTokensResponse([]), 401, ct);
            return;
        }

        var tokens = await _db.ApiTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ApiTokenDto(
                t.Id,
                t.Name,
                t.Prefix,
                t.CreatedAt,
                t.LastUsedAt,
                t.RevokedAt
            ))
            .ToListAsync(ct);

        await SendAsync(new ListApiTokensResponse(tokens), cancellation: ct);
    }
}

