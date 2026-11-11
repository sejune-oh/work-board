using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkBoard.Api.Infrastructure.Data;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

public sealed record WriteResult(int StatusCode, object? Body = null);

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

// Call inside TenantDb.WriteAsync so the stored response commits together with the change. A key is
// scoped to the tenant and expires after 24 hours: an older row counts as absent and is replaced.
// Only 2xx results are stored; return validation errors before writing anything.
public static class Idempotency
{
    public const string HeaderName = "Idempotency-Key";
    public const int KeyMaxLength = 200;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private sealed record StoredKey(Guid? MemberId, string RequestHash, int StatusCode, string? Response);

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
            return new IdempotentResponse(StatusCodes.Status400BadRequest, ErrorJson(ApiErrors.InvalidRequest), false);

        var tenantId = user.Identity.TenantId;

        // Concurrent requests with one key queue here and see the stored row instead of both running.
        await session.ExecuteAsync(
            "select pg_advisory_xact_lock(hashtextextended(@scope, 0))", new { scope = $"idem:{tenantId:N}:{key}" });

        var stored = await session.QuerySingleOrDefaultAsync<StoredKey>("""
            select member_id as MemberId, request_hash as RequestHash, status_code as StatusCode, response::text as Response
              from idempotency_keys
             where tenant_id = @tenantId and key = @key and created_at > now() - @lifetime
            """, new { tenantId, key, lifetime = Lifetime });

        if (stored is not null)
            return stored.RequestHash == requestHash
                ? new IdempotentResponse(stored.StatusCode, stored.Response, Replayed: true)
                : new IdempotentResponse(StatusCodes.Status409Conflict, ErrorJson(ApiErrors.IdempotencyKeyReused), false);

        await session.ExecuteAsync(
            "delete from idempotency_keys where tenant_id = @tenantId and key = @key", new { tenantId, key });

        var result = Render(await run());
        if (result.StatusCode is >= 200 and < 300)
            await session.ExecuteAsync("""
                insert into idempotency_keys (tenant_id, key, member_id, request_hash, status_code, response)
                values (@tenantId, @key, @memberId, @requestHash, @statusCode, cast(@response as jsonb))
                """, new
            {
                tenantId, key, memberId = user.Identity.Member.Id, requestHash,
                statusCode = result.StatusCode, response = result.Json,
            });
        return result;
    }

    private static IdempotentResponse Render(WriteResult result) =>
        new(result.StatusCode,
            result.Body is null ? null : JsonSerializer.Serialize(result.Body, JsonSerializerOptions.Web),
            Replayed: false);

    private static string ErrorJson(string code) => JsonSerializer.Serialize(new ErrorBody(code), JsonSerializerOptions.Web);
}
