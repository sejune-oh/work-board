using WorkBoard.Api.Features.Members;

namespace WorkBoard.Api.Infrastructure.Identity;

public sealed record DevIdentity(Member Member, Guid TenantId, string TenantLogin);

public sealed record DevUser(string GithubLogin, string? DisplayName, string TenantLogin);

public sealed record ErrorBody(string Error);
