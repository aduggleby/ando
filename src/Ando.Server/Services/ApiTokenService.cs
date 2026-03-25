// =============================================================================
// ApiTokenService.cs
//
// Summary: Create and validate personal API tokens for REST API access.
//
// Tokens are generated once and stored as a hash (never plaintext). Validation
// uses an indexed prefix to avoid full-table scans.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

public record CreateApiTokenResult(
    int TokenId,
    string Name,
    string Prefix,
    string Token,
    DateTime CreatedAtUtc
);

public interface IApiTokenService
{
    Task<CreateApiTokenResult> CreateAsync(int userId, string name, CancellationToken ct);
    Task<ApiToken?> ValidateAsync(string token, CancellationToken ct);
    Task RevokeAsync(int userId, int tokenId, CancellationToken ct);
}

public class ApiTokenService : IApiTokenService
{
    private const string TokenPrefix = "ando_pat_";
    private const int PrefixLength = 8;

    private readonly AndoDbContext _db;
    private readonly IOptions<EncryptionSettings> _encryptionSettings;

    public ApiTokenService(AndoDbContext db, IOptions<EncryptionSettings> encryptionSettings)
    {
        _db = db;
        _encryptionSettings = encryptionSettings;
    }

    public async Task<CreateApiTokenResult> CreateAsync(int userId, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Token name is required", nameof(name));
        }

        var token = GenerateToken();
        var prefix = ExtractPrefix(token);
        var tokenHash = ComputeTokenHash(token);

        var now = DateTime.UtcNow;
        var entity = new ApiToken
        {
            UserId = userId,
            Name = name.Trim(),
            Prefix = prefix,
            TokenHash = tokenHash,
            CreatedAt = now
        };

        _db.ApiTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new CreateApiTokenResult(entity.Id, entity.Name, entity.Prefix, token, now);
    }

    public async Task<ApiToken?> ValidateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var prefix = ExtractPrefix(token);
        var tokenHash = ComputeTokenHash(token);

        // Index-backed lookup by prefix first, then verify hash.
        var candidates = await _db.ApiTokens
            .Where(t => t.Prefix == prefix && t.RevokedAt == null)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return null;
        }

        foreach (var t in candidates)
        {
            if (FixedTimeEquals(t.TokenHash, tokenHash))
            {
                t.LastUsedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return t;
            }
        }

        return null;
    }

    public async Task RevokeAsync(int userId, int tokenId, CancellationToken ct)
    {
        var token = await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, ct);
        if (token == null)
        {
            return;
        }

        if (token.RevokedAt == null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var secret = Base64UrlEncode(bytes);
        return TokenPrefix + secret;
    }

    private static string ExtractPrefix(string token)
    {
        var s = token.AsSpan();
        if (!s.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return "";
        }

        var start = TokenPrefix.Length;
        if (s.Length < start + PrefixLength)
        {
            return "";
        }

        return token.Substring(start, PrefixLength);
    }

    private string ComputeTokenHash(string token)
    {
        var key = GetHmacKey();
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash); // uppercase hex
    }

    private byte[] GetHmacKey()
    {
        var keyB64 = _encryptionSettings.Value.Key;
        if (string.IsNullOrWhiteSpace(keyB64))
        {
            throw new InvalidOperationException("Encryption key is not configured (Encryption:Key).");
        }

        return Convert.FromBase64String(keyB64);
    }

    private static bool FixedTimeEquals(string aHex, string bHex)
    {
        // Compare hex strings in constant time by comparing bytes.
        try
        {
            var a = Convert.FromHexString(aHex);
            var b = Convert.FromHexString(bHex);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch
        {
            return false;
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
