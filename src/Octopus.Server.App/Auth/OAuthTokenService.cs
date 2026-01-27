using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Octopus.Server.App.Auth;

/// <summary>
/// Configuration options for OAuth token generation.
/// </summary>
public class OAuthTokenOptions
{
    /// <summary>
    /// The issuer claim for generated tokens.
    /// </summary>
    public string Issuer { get; set; } = "octopus";

    /// <summary>
    /// The audience claim for generated tokens.
    /// </summary>
    public string Audience { get; set; } = "octopus-api";

    /// <summary>
    /// The secret key for signing tokens (minimum 32 characters for HS256).
    /// In production, use a secure key from configuration or key vault.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime in minutes. Default: 60 minutes.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Authorization code lifetime in minutes. Default: 10 minutes.
    /// </summary>
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 10;

    /// <summary>
    /// Whether refresh tokens are enabled. Default: true.
    /// </summary>
    public bool EnableRefreshTokens { get; set; } = true;

    /// <summary>
    /// Refresh token lifetime in days. Default: 30 days.
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = 30;
}

/// <summary>
/// Service for generating and validating OAuth tokens.
/// </summary>
public interface IOAuthTokenService
{
    /// <summary>
    /// Generates a JWT access token with the specified claims.
    /// </summary>
    string GenerateAccessToken(
        string subject,
        Guid userId,
        Guid workspaceId,
        string clientId,
        IEnumerable<string> scopes);

    /// <summary>
    /// Generates a cryptographically random authorization code.
    /// </summary>
    string GenerateAuthorizationCode();

    /// <summary>
    /// Generates a cryptographically random refresh token.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Hashes an authorization code for storage.
    /// </summary>
    string HashCode(string code);

    /// <summary>
    /// Hashes a refresh token for storage using SHA-256.
    /// </summary>
    string HashRefreshToken(string token);

    /// <summary>
    /// Verifies a PKCE code verifier against a stored code challenge.
    /// </summary>
    bool VerifyPkceChallenge(string codeVerifier, string codeChallenge, string codeChallengeMethod);

    /// <summary>
    /// Validates a client secret against a stored hash.
    /// </summary>
    bool ValidateClientSecret(string secret, string secretHash);

    /// <summary>
    /// Gets the access token lifetime in seconds.
    /// </summary>
    int AccessTokenLifetimeSeconds { get; }

    /// <summary>
    /// Gets the refresh token lifetime in seconds.
    /// </summary>
    int RefreshTokenLifetimeSeconds { get; }

    /// <summary>
    /// Whether refresh tokens are enabled.
    /// </summary>
    bool RefreshTokensEnabled { get; }

    /// <summary>
    /// Gets the authorization code expiration time from now.
    /// </summary>
    DateTimeOffset GetAuthorizationCodeExpiration();

    /// <summary>
    /// Gets the refresh token expiration time from now.
    /// </summary>
    DateTimeOffset GetRefreshTokenExpiration();
}

/// <summary>
/// Default implementation of OAuth token service.
/// </summary>
public class OAuthTokenService : IOAuthTokenService
{
    private readonly OAuthTokenOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public OAuthTokenService(IOptions<OAuthTokenOptions> options)
    {
        _options = options.Value;

        // Ensure signing key is configured
        if (string.IsNullOrEmpty(_options.SigningKey))
        {
            // Generate a random key for development (not suitable for production)
            _options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public int AccessTokenLifetimeSeconds => _options.AccessTokenLifetimeMinutes * 60;

    public int RefreshTokenLifetimeSeconds => _options.RefreshTokenLifetimeDays * 24 * 60 * 60;

    public bool RefreshTokensEnabled => _options.EnableRefreshTokens;

    public string GenerateAccessToken(
        string subject,
        Guid userId,
        Guid workspaceId,
        string clientId,
        IEnumerable<string> scopes)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("user_id", userId.ToString()),
            new("tid", workspaceId.ToString()), // Tenant/workspace ID
            new("client_id", clientId),
            new("scp", string.Join(" ", scopes)), // Scopes
            new("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateAuthorizationCode()
    {
        // Generate a 256-bit random authorization code
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    public string GenerateRefreshToken()
    {
        // Generate a 256-bit random refresh token with "octr_" prefix for identification
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var token = Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        return $"octr_{token}";
    }

    public string HashRefreshToken(string token)
    {
        // Use SHA-256 for hashing refresh tokens
        // Refresh tokens are long-lived but single-use per rotation
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public string HashCode(string code)
    {
        // Use SHA-256 for hashing authorization codes
        // Unlike client secrets, we don't need PBKDF2 since codes are single-use and short-lived
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(code);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public bool VerifyPkceChallenge(string codeVerifier, string codeChallenge, string codeChallengeMethod)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
        {
            return false;
        }

        if (codeChallengeMethod == "plain")
        {
            // Plain method: verifier must equal challenge
            return codeVerifier == codeChallenge;
        }
        else if (codeChallengeMethod == "S256")
        {
            // S256 method: challenge = BASE64URL(SHA256(verifier))
            using var sha256 = SHA256.Create();
            var bytes = Encoding.ASCII.GetBytes(codeVerifier);
            var hash = sha256.ComputeHash(bytes);
            var computedChallenge = Convert.ToBase64String(hash)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
            return computedChallenge == codeChallenge;
        }

        return false;
    }

    public bool ValidateClientSecret(string secret, string secretHash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(secretHash))
        {
            return false;
        }

        // The hash format is: base64(salt[16] + hash[32])
        try
        {
            var combined = Convert.FromBase64String(secretHash);
            if (combined.Length != 48) // 16 byte salt + 32 byte hash
            {
                return false;
            }

            var salt = new byte[16];
            Buffer.BlockCopy(combined, 0, salt, 0, 16);

            using var deriveBytes = new Rfc2898DeriveBytes(
                secret,
                salt,
                iterations: 100000,
                HashAlgorithmName.SHA256);

            var computedHash = deriveBytes.GetBytes(32);
            var storedHash = new byte[32];
            Buffer.BlockCopy(combined, 16, storedHash, 0, 32);

            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch
        {
            return false;
        }
    }

    public DateTimeOffset GetAuthorizationCodeExpiration()
    {
        return DateTimeOffset.UtcNow.AddMinutes(_options.AuthorizationCodeLifetimeMinutes);
    }

    public DateTimeOffset GetRefreshTokenExpiration()
    {
        return DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenLifetimeDays);
    }
}
