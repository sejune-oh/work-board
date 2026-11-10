using WorkBoard.Api.Features.Members;

namespace WorkBoard.Api.Infrastructure.Identity;

public sealed record DevIdentity(Member Member, Guid TenantId, string TenantName);

public sealed record DevUser(string GithubLogin, long GithubUserId, string? DisplayName, Guid TenantId, string TenantName);

public sealed record ErrorBody(string Error);
