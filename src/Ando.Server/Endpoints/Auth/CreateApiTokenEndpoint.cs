// =============================================================================
// CreateApiTokenEndpoint.cs
//
// Summary: Create a personal API token for the authenticated user.
//
// POST /api/auth/tokens
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Auth;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Auth;

public class CreateApiTokenEndpoint : Endpoint<CreateApiTokenRequest, CreateApiTokenResponse>
{
    private readonly IApiTokenService _apiTokenService;

    public CreateApiTokenEndpoint(IApiTokenService apiTokenService)
    {
        _apiTokenService = apiTokenService;
    }

    public override void Configure()
    {
        Post("/auth/tokens");
    }

    public override async Task HandleAsync(CreateApiTokenRequest req, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId <= 0)
        {
            await SendAsync(new CreateApiTokenResponse(false, Error: "Not authenticated"), 401, ct);
            return;
        }

        try
        {
            var created = await _apiTokenService.CreateAsync(userId, req.Name, ct);
            var dto = new ApiTokenDto(
                created.TokenId,
                created.Name,
                created.Prefix,
                created.CreatedAtUtc,
                LastUsedAtUtc: null,
                RevokedAtUtc: null
            );

            // IMPORTANT: `Value` is only returned once.
            await SendAsync(new CreateApiTokenResponse(true, dto, created.Token), cancellation: ct);
        }
        catch (Exception ex)
        {
            await SendAsync(new CreateApiTokenResponse(false, Error: ex.Message), cancellation: ct);
        }
    }
}

