namespace WorkBoard.Api.Infrastructure.Identity;

public sealed class DevUserFilter(IWebHostEnvironment env, IMemberLookup members, CurrentUser currentUser) : IEndpointFilter
{
    public const string HeaderName = "X-Dev-User";
    public const string TenantHeaderName = "X-Dev-Tenant";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var login = http.Request.Headers[HeaderName].ToString().Trim();

        if (!env.IsDevelopment() || login.Length == 0)
            return Unauthenticated();

        var memberships = await members.FindByLoginAsync(login, http.RequestAborted);
        if (memberships.Count == 0)
            return Unauthenticated();

        var identity = memberships[0];
        if (memberships.Count > 1)
        {
            var tenant = http.Request.Headers[TenantHeaderName].ToString().Trim();
            var match = memberships.FirstOrDefault(m =>
                string.Equals(m.TenantId.ToString(), tenant, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Results.Json(new ErrorBody("tenant_required"), statusCode: StatusCodes.Status401Unauthorized);
            identity = match;
        }

        currentUser.Set(identity);
        return await next(context);
    }

    private static IResult Unauthenticated() =>
        Results.Json(new ErrorBody("unauthenticated"), statusCode: StatusCodes.Status401Unauthorized);
}
