using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace WorkBoard.Api.Infrastructure.Identity;

// AuthUserId is set on the bearer path, DevLogin on the Development header path.
public sealed record Caller(long GithubUserId, Guid? AuthUserId, string? DevLogin);

public interface ICallerResolver
{
    // Null when the request carries no valid sign-in. Says nothing about membership.
    Task<Caller?> ResolveAsync(HttpContext http, CancellationToken ct);
}

public sealed class CallerResolver(IWebHostEnvironment env, IMemberLookup members, IAuthLinkResolver links) : ICallerResolver
{
    public const string DevUserHeader = "X-Dev-User";

    public async Task<Caller?> ResolveAsync(HttpContext http, CancellationToken ct)
    {
        if (http.Request.Headers.ContainsKey("Authorization"))
        {
            var result = await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            var sub = result.Succeeded ? result.Principal?.FindFirst("sub")?.Value : null;
            if (!Guid.TryParse(sub, out var authUserId))
                return null;
            var id = await links.ResolveGithubUserIdAsync(authUserId, ct);
            return id is null ? null : new Caller(id.Value, authUserId, null);
        }

        if (!env.IsDevelopment())
            return null;

        var login = http.Request.Headers[DevUserHeader].ToString().Trim();
        if (login.Length == 0)
            return null;
        var devId = await members.FindUserIdByLoginAsync(login, ct);
        return devId is null ? null : new Caller(devId.Value, null, login);
    }
}
