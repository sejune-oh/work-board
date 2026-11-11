using WorkBoard.Api.Features.Me;

namespace WorkBoard.Api.Infrastructure.Identity;

public sealed class CurrentMemberFilter(
    ICallerResolver callers, IMemberLookup members, CurrentUser currentUser) : IEndpointFilter
{
    public const string DevUserHeader = CallerResolver.DevUserHeader;
    public const string DevTenantHeader = "X-Dev-Tenant";
    public const string TenantHeader = "X-Tenant";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;
        var bearer = http.Request.Headers.ContainsKey("Authorization");

        var caller = await callers.ResolveAsync(http, ct);
        var tenantHeader = http.Request.Headers[bearer ? TenantHeader : DevTenantHeader].ToString().Trim();
        var githubUserId = caller?.GithubUserId;

        if (githubUserId is null)
            return Fail(StatusCodes.Status401Unauthorized, "unauthenticated");

        var memberships = await members.FindByUserIdAsync(githubUserId.Value, ct);
        if (memberships.Count == 0)
            return bearer
                ? Fail(StatusCodes.Status403Forbidden, "no_membership")
                : Fail(StatusCodes.Status401Unauthorized, "unauthenticated");

        var identity = memberships[0];
        if (memberships.Count > 1)
        {
            var match = memberships.FirstOrDefault(m =>
                string.Equals(m.TenantId.ToString(), tenantHeader, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Results.Json(
                    new TenantRequiredBody("tenant_required", memberships.Select(m => new TenantInfo(m.TenantId, m.TenantName)).ToList()),
                    statusCode: StatusCodes.Status401Unauthorized);
            identity = match;
        }

        currentUser.Set(identity);
        return await next(context);
    }

    private static IResult Fail(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);
}
