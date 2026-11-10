using Microsoft.Extensions.Options;

namespace WorkBoard.Api.Infrastructure.Identity;

public sealed class SupabaseAuthOptions
{
    public const string Section = "Auth:Supabase";
    public const string Audience = "authenticated";

    public string? Url { get; set; }
    public string? ServiceRoleKey { get; set; }

    // Set only for legacy projects that sign with a shared HS256 secret. Otherwise keys come from JWKS.
    public string? JwtSecret { get; set; }

    public string BaseUrl => (Url ?? "").TrimEnd('/');
    public string Issuer => $"{BaseUrl}/auth/v1";
    public string JwksUrl => $"{Issuer}/.well-known/jwks.json";
    public bool Configured => BaseUrl.Length > 0;
}

public sealed class SupabaseAuthOptionsValidator(IHostEnvironment env) : IValidateOptions<SupabaseAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, SupabaseAuthOptions options)
    {
        if (env.IsDevelopment())
            return ValidateOptionsResult.Success;

        var missing = new List<string>();
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
            missing.Add("Auth:Supabase:Url");
        if (string.IsNullOrWhiteSpace(options.ServiceRoleKey))
            missing.Add("Auth:Supabase:ServiceRoleKey");

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Missing or invalid configuration: {string.Join(", ", missing)}.");
    }
}
