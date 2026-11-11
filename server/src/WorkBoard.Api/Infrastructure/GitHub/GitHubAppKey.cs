using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace WorkBoard.Api.Infrastructure.GitHub;

public sealed class GitHubAppKeyException(string message, Exception inner) : Exception(message, inner);

// The App private key, read once per process. A wrong path or file fails every later call the same way
// until the settings are fixed and the API restarts.
public sealed class GitHubAppKey(IOptions<GitHubAppOptions> options, IHostEnvironment env)
{
    private readonly Lazy<RSA> _key = new(() => Load(options.Value.PrivateKeyPath, env.ContentRootPath));

    public RSA Get() => _key.Value;

    private static RSA Load(string? path, string contentRoot)
    {
        try
        {
            var full = Path.GetFullPath(path ?? "", contentRoot);
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(full));
            return rsa;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            throw new GitHubAppKeyException("GitHub:App:PrivateKeyPath does not point to a readable RSA private key.", e);
        }
    }
}
