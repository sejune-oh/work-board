using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

public sealed record WriteResult(int StatusCode, object? Body = null);

// Result of the external call in two-phase mode. SideEffectPossible says whether GitHub may have applied the change.
public sealed record ExternalOutcome(WriteResult Result, bool SideEffectPossible);

// What a write endpoint returns: a fresh result or the stored one of an earlier request with the same key.
public sealed record IdempotentResponse(int StatusCode, string? Json, bool Replayed) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        if (Json is null)
            return Task.CompletedTask;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        return httpContext.Response.WriteAsync(Json, httpContext.RequestAborted);
    }
}

// Two modes, both scoped to the tenant, expiring after 24 hours (an older row counts as absent and is replaced).
//
// RunAsync: DB-only writes. Call inside TenantDb.WriteAsync so the stored response commits with the change.
// Only 2xx results are stored; return validation errors before writing anything.
//
// RunExternalAsync: writes that call GitHub. Phase 1 reserves the key as 'pending' in its own committed
// transaction, the external call runs with no transaction or lock held, phase 2 stores the response.
// A repeat while pending is 409 request_in_progress; a pending row older than PendingTimeout is taken over.
// Storing rule: a 2xx result is always stored; a non-2xx result is stored only when the call may have had a
// side effect (a retry must not repeat it), otherwise the reservation is released so a retry runs again.
// An exception from the call also releases it, so the call must catch failures after the side effect and
// return them as an outcome.
public static class Idempotency
{
    public const string HeaderName = "Idempotency-Key";
    public const int KeyMaxLength = 200;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(2);

    private sealed record StoredKey(string State, string RequestHash, int? StatusCode, string? Response, bool PendingStale);

    // Binds the key to the caller, method, path with query, and body.
    public static string HashRequest(Guid memberId, string method, string pathAndQuery, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{memberId:N}\n{method.ToUpperInvariant()}\n{pathAndQuery}\n");
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data, 0);
        body.CopyTo(data.AsSpan(prefix.Length));
        return Convert.ToHexString(SHA256.HashData(data));
    }

    public static async Task<IdempotentResponse> RunAsync(
        TenantSession session, ICurrentUser user, string? key, string requestHash, Func<Task<WriteResult>> run)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Render(await run());
        if (key.Length > KeyMaxLength)
            return TooLong();

        var tenantId = user.Identity.TenantId;
        await LockAsync(session, tenantId, key);

        var early = Decide(await FindAsync(session, tenantId, key), requestHash);
        if (early is not null)
            return early;

        await DeleteAsync(session, tenantId, key);

        var result = Render(await run());
        if (result.StatusCode is >= 200 and < 300)
            await session.ExecuteAsync("""
                insert into idempotency_keys (tenant_id, key, member_id, request_hash, state, status_code, response)
                values (@tenantId, @key, @memberId, @requestHash, 'completed', @statusCode, cast(@response as jsonb))
                """, new
            {
                tenantId, key, memberId = user.Identity.Member.Id, requestHash,
                statusCode = result.StatusCode, response = result.Json,
            });
        return result;
    }

    public static async Task<IdempotentResponse> RunExternalAsync(
        TenantDb db, ICurrentUser user, string? key, string requestHash,
        Func<Task<ExternalOutcome>> call, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Render((await call()).Result);
        if (key.Length > KeyMaxLength)
            return TooLong();

        var tenantId = user.Identity.TenantId;
        var early = await db.WriteAsync<IdempotentResponse?>(async session =>
        {
            await LockAsync(session, tenantId, key);
            var decided = Decide(await FindAsync(session, tenantId, key), requestHash);
            if (decided is not null)
                return decided;

            await DeleteAsync(session, tenantId, key);
            await session.ExecuteAsync("""
                insert into idempotency_keys (tenant_id, key, member_id, request_hash, state)
                values (@tenantId, @key, @memberId, @requestHash, 'pending')
                """, new { tenantId, key, memberId = user.Identity.Member.Id, requestHash });
            return null;
        }, ct);
        if (early is not null)
            return early;

        ExternalOutcome outcome;
        try
        {
            outcome = await call();
        }
        catch
        {
            await ReleaseAsync(db, tenantId, key);
            throw;
        }

        var result = Render(outcome.Result);
        if (result.StatusCode is >= 200 and < 300 || outcome.SideEffectPossible)
            await db.WriteAsync(session => session.ExecuteAsync("""
                update idempotency_keys
                   set state = 'completed', status_code = @statusCode, response = cast(@response as jsonb)
                 where tenant_id = @tenantId and key = @key and state = 'pending'
                """, new { tenantId, key, statusCode = result.StatusCode, response = result.Json }), CancellationToken.None);
        else
            await ReleaseAsync(db, tenantId, key);
        return result;
    }

    private static Task ReleaseAsync(TenantDb db, Guid tenantId, string key) =>
        db.WriteAsync(session => session.ExecuteAsync(
            "delete from idempotency_keys where tenant_id = @tenantId and key = @key and state = 'pending'",
            new { tenantId, key }), CancellationToken.None);

    // Null means the request may proceed.
    private static IdempotentResponse? Decide(StoredKey? stored, string requestHash)
    {
        if (stored is null || stored is { State: "pending", PendingStale: true })
            return null;
        if (stored.RequestHash != requestHash)
            return new IdempotentResponse(StatusCodes.Status409Conflict, ErrorJson(ApiErrors.IdempotencyKeyReused), false);
        return stored.State == "pending"
            ? new IdempotentResponse(StatusCodes.Status409Conflict, ErrorJson(ApiErrors.RequestInProgress), false)
            : new IdempotentResponse(stored.StatusCode!.Value, stored.Response, Replayed: true);
    }

    // Concurrent requests with one key queue here and see each other's row instead of both running.
    private static Task LockAsync(TenantSession session, Guid tenantId, string key) =>
        session.ExecuteAsync(
            "select pg_advisory_xact_lock(hashtextextended(@scope, 0))", new { scope = $"idem:{tenantId:N}:{key}" });

    private static Task<StoredKey?> FindAsync(TenantSession session, Guid tenantId, string key) =>
        session.QuerySingleOrDefaultAsync<StoredKey>("""
            select state as State, request_hash as RequestHash, status_code as StatusCode, response::text as Response,
                   (state = 'pending' and created_at <= now() - @pendingTimeout) as PendingStale
              from idempotency_keys
             where tenant_id = @tenantId and key = @key and created_at > now() - @lifetime
            """, new { tenantId, key, lifetime = Lifetime, pendingTimeout = PendingTimeout });

    private static Task DeleteAsync(TenantSession session, Guid tenantId, string key) =>
        session.ExecuteAsync("delete from idempotency_keys where tenant_id = @tenantId and key = @key", new { tenantId, key });

    private static IdempotentResponse TooLong() =>
        new(StatusCodes.Status400BadRequest, ErrorJson(ApiErrors.InvalidRequest), false);

    private static IdempotentResponse Render(WriteResult result) =>
        new(result.StatusCode,
            result.Body is null ? null : JsonSerializer.Serialize(result.Body, JsonSerializerOptions.Web),
            Replayed: false);

    private static string ErrorJson(string code) => JsonSerializer.Serialize(new ErrorBody(code), JsonSerializerOptions.Web);
}
