using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace WorkBoard.Api.Infrastructure.Identity;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddWorkBoardAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<SupabaseAuthOptions>()
            .Bind(config.GetSection(SupabaseAuthOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SupabaseAuthOptions>, SupabaseAuthOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, SupabaseJwtBearerSetup>();

        services.AddMemoryCache();
        services.AddHttpClient<IAuthIdentitySource, SupabaseAdminIdentitySource>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IAuthLinkStore, NpgsqlAuthLinkStore>();
        services.AddSingleton<IAuthLinkResolver, AuthLinkResolver>();
        services.AddScoped<CurrentMemberFilter>();
        return services;
    }
}
