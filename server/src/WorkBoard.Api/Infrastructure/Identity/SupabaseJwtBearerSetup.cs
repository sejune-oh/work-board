using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace WorkBoard.Api.Infrastructure.Identity;

public sealed class SupabaseJwtBearerSetup(IOptions<SupabaseAuthOptions> auth) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
            return;

        var supabase = auth.Value;
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = supabase.Issuer,
            ValidateAudience = true,
            ValidAudience = SupabaseAuthOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        if (!supabase.Configured)
            return;

        if (!string.IsNullOrWhiteSpace(supabase.JwtSecret))
        {
            options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabase.JwtSecret));
            options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
            return;
        }

        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            supabase.JwksUrl,
            new JwksConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = supabase.JwksUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) });
    }
}

// Supabase publishes a bare JWKS document, not OpenID discovery metadata.
internal sealed class JwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var json = await retriever.GetDocumentAsync(address, cancel);
        var config = new OpenIdConnectConfiguration();
        foreach (var key in new JsonWebKeySet(json).GetSigningKeys())
            config.SigningKeys.Add(key);
        return config;
    }
}
