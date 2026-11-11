using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using WorkBoard.Api.Features.Me;
using WorkBoard.Api.Features.Members;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Features.Tenants;

public sealed record CreateTenantRequest(string? Name);

public sealed record CreateTenantResponse(TenantInfo Tenant, Member Member);

public static class TenantsEndpoints
{
    public const int NameMaxLength = 80;
    public const int MaxTenantsPerPerson = 5;

    // Not behind CurrentMemberFilter: the caller has no membership yet.
    public static void MapTenants(this RouteGroupBuilder api) =>
        api.MapPost("/tenants", async (
            HttpContext http, ICallerResolver callers, IAuthIdentitySource source, IMemberLookup members,
            [FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync, CancellationToken ct) =>
        {
            var caller = await callers.ResolveAsync(http, ct);
            if (caller is null)
                return Error(StatusCodes.Status401Unauthorized, "unauthenticated");

            var profile = await ProfileAsync(caller, source, members, ct);
            if (profile is null || string.IsNullOrWhiteSpace(profile.Login))
                return Error(StatusCodes.Status401Unauthorized, "unauthenticated");

            var name = await ReadNameAsync(http, ct);
            if (name is null)
                return Error(StatusCodes.Status400BadRequest, "invalid_name");

            var created = await CreateAsync(sync, name, profile, ct);
            return created is null
                ? Error(StatusCodes.Status409Conflict, "tenant_limit")
                : Results.Json(created, statusCode: StatusCodes.Status201Created);
        });

    private static IResult Error(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);

    private static async Task<GithubProfile?> ProfileAsync(
        Caller caller, IAuthIdentitySource source, IMemberLookup members, CancellationToken ct)
    {
        if (caller.AuthUserId is { } authUserId)
        {
            var profile = await source.FindGithubProfileAsync(authUserId, ct);
            return profile?.Id == caller.GithubUserId ? profile : null;
        }

        var existing = (await members.FindByUserIdAsync(caller.GithubUserId, ct)).Select(i => i.Member).FirstOrDefault();
        return new GithubProfile(caller.GithubUserId, caller.DevLogin, existing?.DisplayName, existing?.AvatarUrl);
    }

    private static async Task<string?> ReadNameAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasJsonContentType())
            return null;

        CreateTenantRequest? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<CreateTenantRequest>(ct);
        }
        catch (JsonException)
        {
            return null;
        }

        var name = body?.Name?.Trim();
        return string.IsNullOrEmpty(name) || name.Length > NameMaxLength || !IsPrintable(name) ? null : name;
    }

    // Rejects control and invisible format characters (zero-width, bidi overrides) and broken surrogates.
    private static bool IsPrintable(string name)
    {
        for (var i = 0; i < name.Length;)
        {
            if (Rune.DecodeFromUtf16(name.AsSpan(i), out var rune, out var used) != OperationStatus.Done)
                return false;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format)
                return false;
            i += used;
        }
        return true;
    }

    // Null when the person already created MaxTenantsPerPerson tenants.
    private static async Task<CreateTenantResponse?> CreateAsync(
        NpgsqlDataSource sync, string name, GithubProfile profile, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Serializes concurrent creations by the same person so the limit holds.
        await conn.ExecuteAsync(new CommandDefinition(
            "select pg_advisory_xact_lock(@key)", new { key = profile.Id }, tx, cancellationToken: ct));

        var owned = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(*) from tenants where created_by_github_user_id = @id", new { id = profile.Id }, tx, cancellationToken: ct));
        if (owned >= MaxTenantsPerPerson)
            return null;

        var tenantId = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "insert into tenants (name, created_by_github_user_id) values (@name, @id) returning id", new { name, id = profile.Id }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "insert into tenant_settings (tenant_id) values (@tenantId)", new { tenantId }, tx, cancellationToken: ct));
        var row = await conn.QuerySingleAsync<MemberRow>(new CommandDefinition("""
            insert into members (tenant_id, github_user_id, github_login, display_name, avatar_url, role)
            values (@tenantId, @id, @login, @displayName, @avatarUrl, 'admin')
            returning id, github_user_id, github_login, display_name, avatar_url, role
            """, new { tenantId, id = profile.Id, login = profile.Login, displayName = profile.DisplayName, avatarUrl = profile.AvatarUrl },
            tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new CreateTenantResponse(new TenantInfo(tenantId, name), row.ToMember());
    }
}
