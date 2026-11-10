using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WorkBoard.Api.Infrastructure.Identity;

public interface IAuthIdentitySource
{
    // Null when the auth user has no GitHub identity or does not exist.
    Task<long?> FindGithubUserIdAsync(Guid authUserId, CancellationToken ct);
}

// Asks the Supabase Admin API for the user's identities. user_metadata is never read: the user can edit it.
public sealed class SupabaseAdminIdentitySource(HttpClient http, IOptions<SupabaseAuthOptions> options) : IAuthIdentitySource
{
    public async Task<long?> FindGithubUserIdAsync(Guid authUserId, CancellationToken ct)
    {
        var supabase = options.Value;
        if (!supabase.Configured || string.IsNullOrWhiteSpace(supabase.ServiceRoleKey))
            throw new InvalidOperationException("Auth:Supabase:Url and Auth:Supabase:ServiceRoleKey are required.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{supabase.BaseUrl}/auth/v1/admin/users/{authUserId}");
        request.Headers.Add("apikey", supabase.ServiceRoleKey);
        request.Headers.Authorization = new("Bearer", supabase.ServiceRoleKey);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return ParseGithubUserId(doc.RootElement);
    }

    public static long? ParseGithubUserId(JsonElement user)
    {
        if (!user.TryGetProperty("identities", out var identities) || identities.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var identity in identities.EnumerateArray())
        {
            if (!identity.TryGetProperty("provider", out var provider) || provider.GetString() != "github")
                continue;

            var data = identity.TryGetProperty("identity_data", out var d) && d.ValueKind == JsonValueKind.Object ? d : (JsonElement?)null;
            long? id = Parse(identity, "provider_id")
                ?? (data is { } x ? Parse(x, "provider_id") ?? Parse(x, "sub") : null)
                ?? Parse(identity, "id");
            if (id is > 0)
                return id;
        }
        return null;
    }

    private static long? Parse(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String when long.TryParse(value.GetString(), out var s) => s,
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            _ => null,
        };
    }
}
