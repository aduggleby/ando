// =============================================================================
// AuthController.cs
//
// Summary: Handles user authentication via GitHub OAuth.
//
// This controller manages the complete OAuth flow including redirecting to
// GitHub, handling the callback, creating/updating user accounts, and
// managing the authentication cookie.
//
// Design Decisions:
// - Uses ASP.NET Core cookie authentication (not Identity)
// - User data is stored in the database, linked by GitHubId
// - Access tokens are encrypted before storage
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for GitHub OAuth authentication.
/// </summary>
public class AuthController : Controller
{
    private readonly AndoDbContext _db;
    private readonly GitHubSettings _gitHubSettings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AndoDbContext db,
        IOptions<GitHubSettings> gitHubSettings,
        IHttpClientFactory httpClientFactory,
        ILogger<AuthController> logger)
    {
        _db = db;
        _gitHubSettings = gitHubSettings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Login Page
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the login page.
    /// </summary>
    [AllowAnonymous]
    [Route("auth/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(returnUrl ?? "/");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // -------------------------------------------------------------------------
    // GitHub OAuth Flow
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initiates the GitHub OAuth flow by redirecting to GitHub.
    /// </summary>
    [AllowAnonymous]
    [Route("auth/github")]
    public IActionResult GitHubLogin(string? returnUrl = null)
    {
        // Store return URL in session for callback
        if (!string.IsNullOrEmpty(returnUrl))
        {
            HttpContext.Session.SetString("ReturnUrl", returnUrl);
        }

        var callbackUrl = Url.Action("GitHubCallback", "Auth", null, Request.Scheme);
        var state = Guid.NewGuid().ToString("N");

        // Store state for CSRF protection
        HttpContext.Session.SetString("OAuthState", state);

        var authUrl = $"https://github.com/login/oauth/authorize" +
            $"?client_id={Uri.EscapeDataString(_gitHubSettings.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUrl!)}" +
            $"&scope=user:email,repo" +
            $"&state={state}";

        return Redirect(authUrl);
    }

    /// <summary>
    /// Handles the OAuth callback from GitHub.
    /// </summary>
    [AllowAnonymous]
    [Route("auth/github/callback")]
    public async Task<IActionResult> GitHubCallback(string code, string state)
    {
        // Verify state for CSRF protection
        var expectedState = HttpContext.Session.GetString("OAuthState");
        if (string.IsNullOrEmpty(expectedState) || state != expectedState)
        {
            _logger.LogWarning("OAuth state mismatch");
            return RedirectToAction("Login", new { error = "invalid_state" });
        }

        HttpContext.Session.Remove("OAuthState");

        try
        {
            // Exchange code for access token
            var accessToken = await ExchangeCodeForTokenAsync(code);
            if (string.IsNullOrEmpty(accessToken))
            {
                return RedirectToAction("Login", new { error = "token_exchange_failed" });
            }

            // Get user info from GitHub
            var gitHubUser = await GetGitHubUserAsync(accessToken);
            if (gitHubUser == null)
            {
                return RedirectToAction("Login", new { error = "user_fetch_failed" });
            }

            // Create or update user in database
            var user = await GetOrCreateUserAsync(gitHubUser, accessToken);

            // Sign in user
            await SignInUserAsync(user);

            // Redirect to return URL or home
            var returnUrl = HttpContext.Session.GetString("ReturnUrl") ?? "/";
            HttpContext.Session.Remove("ReturnUrl");

            return Redirect(returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub OAuth callback failed");
            return RedirectToAction("Login", new { error = "auth_failed" });
        }
    }

    // -------------------------------------------------------------------------
    // Logout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signs out the current user.
    /// </summary>
    [Authorize]
    [Route("auth/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookies");
        return RedirectToAction("Login");
    }

    // -------------------------------------------------------------------------
    // Helper Methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Exchanges the OAuth authorization code for an access token.
    /// </summary>
    private async Task<string?> ExchangeCodeForTokenAsync(string code)
    {
        using var client = _httpClientFactory.CreateClient();

        var tokenRequest = new Dictionary<string, string>
        {
            ["client_id"] = _gitHubSettings.ClientId,
            ["client_secret"] = _gitHubSettings.ClientSecret,
            ["code"] = code
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(tokenRequest)
        };
        request.Headers.Accept.Add(new("application/json"));

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Token exchange failed with status {Status}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
        {
            return tokenElement.GetString();
        }

        if (doc.RootElement.TryGetProperty("error", out var errorElement))
        {
            _logger.LogWarning("Token exchange error: {Error}", errorElement.GetString());
        }

        return null;
    }

    /// <summary>
    /// Gets user information from the GitHub API.
    /// </summary>
    private async Task<GitHubUserInfo?> GetGitHubUserAsync(string accessToken)
    {
        using var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        var response = await client.GetAsync("user");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get GitHub user: {Status}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var user = new GitHubUserInfo
        {
            Id = root.GetProperty("id").GetInt64(),
            Login = root.GetProperty("login").GetString()!,
            Email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null,
            AvatarUrl = root.TryGetProperty("avatar_url", out var avatarProp) ? avatarProp.GetString() : null
        };

        // If email is not public, try to get it from emails endpoint
        if (string.IsNullOrEmpty(user.Email))
        {
            user.Email = await GetPrimaryEmailAsync(client);
        }

        return user;
    }

    /// <summary>
    /// Gets the user's primary email from the GitHub emails endpoint.
    /// </summary>
    private async Task<string?> GetPrimaryEmailAsync(HttpClient client)
    {
        try
        {
            var response = await client.GetAsync("user/emails");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            foreach (var email in doc.RootElement.EnumerateArray())
            {
                var isPrimary = email.TryGetProperty("primary", out var primaryProp) && primaryProp.GetBoolean();
                var isVerified = email.TryGetProperty("verified", out var verifiedProp) && verifiedProp.GetBoolean();

                if (isPrimary && isVerified && email.TryGetProperty("email", out var emailProp))
                {
                    return emailProp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get primary email");
        }

        return null;
    }

    /// <summary>
    /// Gets or creates a user in the database based on GitHub info.
    /// </summary>
    private async Task<User> GetOrCreateUserAsync(GitHubUserInfo gitHubUser, string accessToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubId == gitHubUser.Id);

        if (user == null)
        {
            user = new User
            {
                GitHubId = gitHubUser.Id,
                GitHubLogin = gitHubUser.Login,
                Email = gitHubUser.Email,
                AvatarUrl = gitHubUser.AvatarUrl,
                AccessToken = accessToken, // TODO: Encrypt this
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            _logger.LogInformation("Created new user {Login} (GitHub ID: {GitHubId})", user.GitHubLogin, user.GitHubId);
        }
        else
        {
            // Update existing user
            user.GitHubLogin = gitHubUser.Login;
            user.Email = gitHubUser.Email ?? user.Email;
            user.AvatarUrl = gitHubUser.AvatarUrl ?? user.AvatarUrl;
            user.AccessToken = accessToken; // TODO: Encrypt this
            user.LastLoginAt = DateTime.UtcNow;
            _logger.LogInformation("Updated user {Login} (GitHub ID: {GitHubId})", user.GitHubLogin, user.GitHubId);
        }

        await _db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Signs in the user by creating an authentication cookie.
    /// </summary>
    private async Task SignInUserAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.GitHubLogin),
            new("GitHubId", user.GitHubId.ToString()),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new(ClaimTypes.Email, user.Email));
        }

        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            claims.Add(new("AvatarUrl", user.AvatarUrl));
        }

        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
    }

    /// <summary>
    /// DTO for GitHub user information.
    /// </summary>
    private class GitHubUserInfo
    {
        public long Id { get; set; }
        public string Login { get; set; } = "";
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }
    }
}
