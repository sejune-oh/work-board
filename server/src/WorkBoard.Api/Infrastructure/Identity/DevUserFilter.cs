namespace WorkBoard.Api.Infrastructure.Identity;

public sealed class DevUserFilter(IWebHostEnvironment env, IMemberLookup members, CurrentUser currentUser) : IEndpointFilter
{
    public const string HeaderName = "X-Dev-User";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var login = http.Request.Headers[HeaderName].ToString().Trim();

        if (!env.IsDevelopment() || login.Length == 0)
            return Unauthenticated();

        var identity = await members.FindByLoginAsync(login, http.RequestAborted);
        if (identity is null)
            return Unauthenticated();

        currentUser.Set(identity);
        return await next(context);
    }

    private static IResult Unauthenticated() =>
        Results.Json(new ErrorBody("unauthenticated"), statusCode: StatusCodes.Status401Unauthorized);
}
