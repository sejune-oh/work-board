namespace WorkBoard.Api.Infrastructure.GitHub;

public static class GitHubServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubApp(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GitHubAppOptions>(config.GetSection(GitHubAppOptions.Section));
        services.AddHttpClient<IGitHubAppClient, GitHubAppClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("work-board");
        });
        return services;
    }
}
