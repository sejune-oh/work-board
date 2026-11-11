using System.Text.Json;
using WorkBoard.Api.Features.Tenants;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;
using WorkBoard.Api.Infrastructure.Writes;

namespace WorkBoard.Api.Features.Sprints;

public static class SprintsEndpoints
{
    public const int NameMaxLength = 80;
    public const int MaxIssuesPerRequest = 200;
    private const int MaxBodyBytes = 64 * 1024;

    public static void MapSprints(this RouteGroupBuilder api)
    {
        api.MapGet("/sprints", async (TenantDb db, CancellationToken ct) =>
            Results.Ok(await db.ReadAsync(SprintQueries.ListAsync, ct)));

        api.MapPost("/sprints", (HttpContext http, ICurrentUser user, TenantDb db, SprintWrites writes, CancellationToken ct) =>
            RunAsync<CreateSprintRequest>(http, user, db, ct,
                r => ValidName(r.Name) is { } name && r.StartDate is { } start && r.EndDate is { } end
                    ? (start <= end ? Do(s => writes.CreateAsync(s, name, start, end)) : Invalid(SprintWrites.InvalidDates))
                    : Invalid(),
                body: true));

        api.MapPatch("/sprints/{id:guid}", (Guid id, HttpContext http, ICurrentUser user, TenantDb db, SprintWrites writes, CancellationToken ct) =>
            RunAsync<UpdateSprintRequest>(http, user, db, ct, r =>
            {
                var hasChange = r.Name is not null || r.StartDate is not null || r.EndDate is not null;
                if (!hasChange || r.UpdatedAt is null || (r.Name is not null && ValidName(r.Name) is null))
                    return Invalid();
                if (r.StartDate is { } start && r.EndDate is { } end && start > end)
                    return Invalid(SprintWrites.InvalidDates);
                var request = r with { Name = r.Name?.Trim() };
                return Do(s => writes.UpdateAsync(s, id, request));
            }, body: true));

        api.MapPost("/sprints/{id:guid}/issues", (Guid id, HttpContext http, ICurrentUser user, TenantDb db, SprintWrites writes, CancellationToken ct) =>
            RunAsync<SprintIssuesRequest>(http, user, db, ct,
                r => IssueIds(r) is { } ids ? Do(s => writes.AddIssuesAsync(s, id, ids)) : Invalid(), body: true));

        api.MapDelete("/sprints/{id:guid}/issues", (Guid id, HttpContext http, ICurrentUser user, TenantDb db, SprintWrites writes, CancellationToken ct) =>
            RunAsync<SprintIssuesRequest>(http, user, db, ct,
                r => IssueIds(r) is { } ids ? Do(s => writes.RemoveIssuesAsync(s, id, ids)) : Invalid(), body: true));

        api.MapPost("/sprints/{id:guid}/carry-over", (Guid id, HttpContext http, ICurrentUser user, TenantDb db, SprintWrites writes, CancellationToken ct) =>
            RunAsync<CarryOverRequest>(http, user, db, ct,
                r => r.ToSprintId is { } to && to != id ? Do(s => writes.CarryOverAsync(s, id, to)) : Invalid(), body: true));

        api.MapPost("/sprints/{id:guid}/carry-over/{carryOverId:guid}/undo",
            (Guid id, Guid carryOverId, HttpContext http, ICurrentUser user, TenantDb db, SprintWrites writes, CancellationToken ct) =>
                RunAsync<object>(http, user, db, ct, _ => Do(s => writes.UndoCarryOverAsync(s, id, carryOverId)), body: false));
    }

    private static Guid[]? IssueIds(SprintIssuesRequest r)
    {
        var ids = r.IssueIds?.Distinct().Order().ToArray();
        return ids is { Length: > 0 and <= MaxIssuesPerRequest } ? ids : null;
    }

    private static string? ValidName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength || !TenantsEndpoints.IsPrintable(trimmed)
            ? null
            : trimmed;
    }

    // Either the work to run or the validation error to return.
    private readonly record struct Plan(Func<TenantSession, Task<WriteResult>>? Work, IResult? Error);

    private static Plan Do(Func<TenantSession, Task<WriteResult>> work) => new(work, null);

    private static Plan Invalid(string code = ApiErrors.InvalidRequest) => new(null, ApiErrors.BadRequest(code));

    private static async Task<IResult> RunAsync<T>(
        HttpContext http, ICurrentUser user, TenantDb db, CancellationToken ct, Func<T, Plan> plan, bool body)
    {
        var bytes = await ReadBodyAsync(http, ct);
        T? request = default;
        if (body)
        {
            if (bytes is null || !http.Request.HasJsonContentType())
                return ApiErrors.BadRequest();
            try
            {
                request = JsonSerializer.Deserialize<T>(bytes, JsonSerializerOptions.Web);
            }
            catch (JsonException)
            {
                return ApiErrors.BadRequest();
            }
            if (request is null)
                return ApiErrors.BadRequest();
        }

        var chosen = plan(request!);
        if (chosen.Error is not null)
            return chosen.Error;

        var key = http.Request.Headers[Idempotency.HeaderName].ToString();
        var hash = Idempotency.HashRequest(
            user.Identity.Member.Id, http.Request.Method, http.Request.Path + http.Request.QueryString, bytes ?? []);
        return await db.WriteAsync(s => Idempotency.RunAsync(s, user, key, hash, () => chosen.Work!(s)), ct);
    }

    // Null when the body is larger than allowed.
    private static async Task<byte[]?> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
