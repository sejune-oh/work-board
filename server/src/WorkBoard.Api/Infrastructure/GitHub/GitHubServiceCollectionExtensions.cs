using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WorkBoard.Api.Infrastructure.GitHub;

public static class GitHubServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubApp(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GitHubAppOptions>(config.GetSection(GitHubAppOptions.Section));
        services.AddSingleton<GitHubAppKey>();
        services.AddHttpClient<IGitHubAppClient, GitHubAppClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("work-board");
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GitHubInstallationTokens.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("work-board");
        });
        services.AddSingleton<IGitHubInstallationTokens, GitHubInstallationTokens>();
        services.AddHttpClient<IGitHubWriter, GitHubWriter>(c =>
        {
            // Below Idempotency.CallTimeout, so the client times out first and takes the same 504 path.
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("work-board");
        });
        services.AddHttpClient<IGitHubSource, GitHubSource>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("work-board");
        });
        return services;
    }
}
