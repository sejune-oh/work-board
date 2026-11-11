using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Features.Connections;

public sealed record ConnectGithubRequest(string? Code, long? InstallationId);

public static class ConnectGithub
{
    private const string Columns =
        "id, account_id, account_login, account_type, repository_selection, created_at, disconnected_at";

    public static void MapConnectGithub(this RouteGroupBuilder api) =>
        api.MapPost("/connections/github", HandleAsync);

    private static async Task<IResult> HandleAsync(
        HttpContext http, ICurrentUser user, IOptions<GitHubAppOptions> options, IGitHubAppClient github,
        [FromKeyedServices(DataKeys.Sync)] NpgsqlDataSource sync, CancellationToken ct)
    {
        var caller = user.Identity;
        if (caller.Member.Role != "admin")
            return Error(StatusCodes.Status403Forbidden, "admin_only");
        if (!options.Value.Configured)
            return Error(StatusCodes.Status503ServiceUnavailable, "github_app_not_configured");

        var request = await ReadAsync(http, ct);
        if (request is not { Code: { Length: > 0 } code, InstallationId: > 0 and var installationId })
            return Error(StatusCodes.Status400BadRequest, "invalid_request");

        try
        {
            var token = await github.ExchangeCodeAsync(code, ct);
            if (token is null)
                return Error(StatusCodes.Status400BadRequest, "invalid_code");

            var githubUser = await github.GetUserAsync(token, ct);
            if (githubUser is null)
                return Error(StatusCodes.Status400BadRequest, "invalid_code");
            if (githubUser.Id != caller.Member.GithubUserId)
                return Error(StatusCodes.Status403Forbidden, "not_your_account");

            var owned = await github.ListUserInstallationIdsAsync(token, ct);
            if (!owned.Contains(installationId))
                return Error(StatusCodes.Status403Forbidden, "not_your_installation");

            var installation = await github.GetInstallationAsync(installationId, ct);
            if (installation is null)
                return Error(StatusCodes.Status403Forbidden, "not_your_installation");
            if (!string.Equals(installation.AccountType, "User", StringComparison.OrdinalIgnoreCase))
                return Error(StatusCodes.Status422UnprocessableEntity, "organization_not_supported");

            var saved = await SaveAsync(sync, caller.TenantId, installationId, installation, ct);
            return saved is null
                ? Error(StatusCodes.Status409Conflict, "account_connected")
                : Results.Json(saved.Connection, statusCode: saved.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (HttpRequestException)
        {
            return Error(StatusCodes.Status502BadGateway, "github_unavailable");
        }
    }

    private static IResult Error(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);

    private static async Task<ConnectGithubRequest?> ReadAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasJsonContentType())
            return null;
        try
        {
            return await http.Request.ReadFromJsonAsync<ConnectGithubRequest>(ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Saved(GithubConnection Connection, bool Created);

    private sealed record ExistingRow(Guid Id, Guid TenantId);

    // Null when the account is actively connected to another tenant.
    private static async Task<Saved?> SaveAsync(
        NpgsqlDataSource sync, Guid tenantId, long installationId, GitHubInstallation installation, CancellationToken ct)
    {
        await using var conn = await sync.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var args = new
        {
            tenantId, installationId, accountId = installation.AccountId,
            login = installation.AccountLogin, selection = installation.RepositorySelection,
        };

        var existing = await conn.QuerySingleOrDefaultAsync<ExistingRow>(new CommandDefinition(
            "select id, tenant_id from github_connections where account_id = @accountId and disconnected_at is null for update",
            args, tx, cancellationToken: ct));
        if (existing is not null && existing.TenantId != tenantId)
            return null;

        try
        {
            if (existing is not null)
            {
                var updated = await conn.QuerySingleAsync<GithubConnection>(new CommandDefinition($"""
                    update github_connections
                       set installation_id = @installationId, repository_selection = @selection, account_login = @login
                     where id = @id
                    returning {Columns}
                    """, new { installationId, args.selection, args.login, existing.Id }, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
                return new Saved(updated, false);
            }

            var inserted = await conn.QuerySingleAsync<GithubConnection>(new CommandDefinition($"""
                insert into github_connections (tenant_id, account_id, account_login, account_type, installation_id, repository_selection)
                values (@tenantId, @accountId, @login, 'user', @installationId, @selection)
                returning {Columns}
                """, args, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return new Saved(inserted, true);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }
}
