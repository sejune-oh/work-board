using WorkBoard.Api.Infrastructure.GitHub;
using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

// Turns a failed GitHub write into the documented error response.
public static class GitHubWriteErrors
{
    public static WriteResult ToResult(GitHubWriteException e)
    {
        var (status, code) = e.Kind switch
        {
            GitHubWriteErrorKind.NotGranted => (StatusCodes.Status403Forbidden, ApiErrors.GithubWriteNotGranted),
            GitHubWriteErrorKind.AssigneeNotCollaborator => (StatusCodes.Status422UnprocessableEntity, ApiErrors.AssigneeNotCollaborator),
            GitHubWriteErrorKind.InvalidRequest => (StatusCodes.Status422UnprocessableEntity, ApiErrors.InvalidRequest),
            GitHubWriteErrorKind.NotFound => (StatusCodes.Status404NotFound, ApiErrors.NotFound),
            _ => (e.NoResponse ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway, ApiErrors.GithubUnavailable),
        };
        return new WriteResult(status, new ErrorBody(code));
    }

    // For Idempotency.RunExternalAsync. A failure with NoResponse is not converted: let it propagate so the
    // reservation stays pending and the caller gets 504.
    public static ExternalOutcome ToOutcome(GitHubWriteException e) => new(ToResult(e), e.SideEffectPossible);
}
