using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using WorkBoard.Api.Features.Me;

namespace WorkBoard.Api.Infrastructure.Identity;

public sealed class CurrentMemberFilter(
    IWebHostEnvironment env, IMemberLookup members, IAuthLinkResolver links, CurrentUser currentUser) : IEndpointFilter
{
    public const string DevUserHeader = "X-Dev-User";
    public const string DevTenantHeader = "X-Dev-Tenant";
    public const string TenantHeader = "X-Tenant";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;
        var bearer = http.Request.Headers.ContainsKey("Authorization");

        long? githubUserId;
        string tenantHeader;
        if (bearer)
        {
            githubUserId = await ResolveBearerAsync(http, ct);
            tenantHeader = http.Request.Headers[TenantHeader].ToString().Trim();
        }
        else if (env.IsDevelopment())
        {
            var login = http.Request.Headers[DevUserHeader].ToString().Trim();
            githubUserId = login.Length == 0 ? null : await members.FindUserIdByLoginAsync(login, ct);
            tenantHeader = http.Request.Headers[DevTenantHeader].ToString().Trim();
        }
        else
        {
            githubUserId = null;
            tenantHeader = "";
        }

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

    private async Task<long?> ResolveBearerAsync(HttpContext http, CancellationToken ct)
    {
        var result = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        var sub = result.Succeeded ? result.Principal?.FindFirst("sub")?.Value : null;
        return Guid.TryParse(sub, out var authUserId)
            ? await links.ResolveGithubUserIdAsync(authUserId, ct)
            : null;
    }

    private static IResult Fail(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);
}
