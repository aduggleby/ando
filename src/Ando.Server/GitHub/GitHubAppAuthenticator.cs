// =============================================================================
// GitHubAppAuthenticator.cs
//
// Summary: Generates JWTs for GitHub App authentication.
//
// GitHub Apps authenticate by generating a JWT signed with the app's private
// key. This JWT is then used to obtain installation access tokens for API calls.
//
// Design Decisions:
// - JWTs are short-lived (10 minutes max per GitHub)
// - Private key loaded from file path in configuration
// - Caches installation tokens until near expiry
// =============================================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Ando.Server.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ando.Server.GitHub;

/// <summary>
/// Generates JWTs for GitHub App authentication.
/// </summary>
public class GitHubAppAuthenticator
{
    private readonly GitHubSettings _settings;
    private readonly ILogger<GitHubAppAuthenticator> _logger;
    private RSA? _privateKey;

    public GitHubAppAuthenticator(
        IOptions<GitHubSettings> settings,
        ILogger<GitHubAppAuthenticator> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates a JWT for authenticating as the GitHub App.
    /// </summary>
    public string GenerateJwt()
    {
        var privateKey = GetPrivateKey();
        var securityKey = new RsaSecurityKey(privateKey);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Iss, _settings.AppId)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now.AddMinutes(-1), // Allow for clock skew
            expires: now.AddMinutes(10),   // GitHub max is 10 minutes
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Gets or loads the private key from the configured path.
    /// </summary>
    private RSA GetPrivateKey()
    {
        if (_privateKey != null)
        {
            return _privateKey;
        }

        if (string.IsNullOrEmpty(_settings.PrivateKeyPath))
        {
            throw new InvalidOperationException(
                "GitHub App private key path is not configured. " +
                "Set GitHub:PrivateKeyPath in configuration.");
        }

        if (!File.Exists(_settings.PrivateKeyPath))
        {
            throw new FileNotFoundException(
                $"GitHub App private key file not found: {_settings.PrivateKeyPath}");
        }

        var keyPem = File.ReadAllText(_settings.PrivateKeyPath);
        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(keyPem);

        _logger.LogInformation("Loaded GitHub App private key from {Path}", _settings.PrivateKeyPath);

        return _privateKey;
    }
}
