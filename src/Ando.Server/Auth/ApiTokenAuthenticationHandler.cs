// =============================================================================
// ApiTokenAuthenticationHandler.cs
//
// Summary: Authenticates API requests using personal API tokens.
//
// Supports:
// - Authorization: Bearer ando_pat_...
// - X-Api-Token: ando_pat_...
//
// Notes:
// - If no token is present, returns NoResult() so other auth (cookies) can apply.
// - If a token is present but invalid, returns Fail() (401).
// =============================================================================

using System.Security.Claims;
using System.Text.Encodings.Web;
using Ando.Server.Models;
using Ando.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Ando.Server.Auth;

public class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiTokenService _apiTokenService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiTokenService apiTokenService,
        UserManager<ApplicationUser> userManager)
        : base(options, logger, encoder)
    {
        _apiTokenService = apiTokenService;
        _userManager = userManager;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var apiToken = await _apiTokenService.ValidateAsync(token, Context.RequestAborted);
        if (apiToken == null)
        {
            return AuthenticateResult.Fail("Invalid API token");
        }

        var user = await _userManager.FindByIdAsync(apiToken.UserId.ToString());
        if (user == null)
        {
            return AuthenticateResult.Fail("User not found");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
        };

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        // Token metadata is useful for debugging/auditing in logs.
        claims.Add(new Claim("ando:api_token_id", apiToken.Id.ToString()));
        claims.Add(new Claim("ando:api_token_prefix", apiToken.Prefix));

        var identity = new ClaimsIdentity(claims, ApiTokenAuthentication.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiTokenAuthentication.Scheme);

        return AuthenticateResult.Success(ticket);
    }

    private string? ExtractToken()
    {
        var authz = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authz) &&
            authz.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authz.Substring("Bearer ".Length).Trim();
        }

        if (Request.Headers.TryGetValue(ApiTokenAuthentication.ApiTokenHeaderName, out var headerVals))
        {
            return headerVals.ToString().Trim();
        }

        return null;
    }
}

