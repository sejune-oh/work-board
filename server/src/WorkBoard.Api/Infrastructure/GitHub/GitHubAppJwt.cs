using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WorkBoard.Api.Infrastructure.GitHub;

public static class GitHubAppJwt
{
    // GitHub rejects tokens that live longer than 10 minutes from iat; iat is backdated for clock drift.
    private static readonly TimeSpan Backdate = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(9);

    public static string Create(long appId, RSA privateKey, DateTimeOffset now) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = appId.ToString(),
            IssuedAt = (now - Backdate).UtcDateTime,
            Expires = (now + Lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(privateKey), SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
        });
}
