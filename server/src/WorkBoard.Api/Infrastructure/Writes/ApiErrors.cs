using WorkBoard.Api.Infrastructure.Identity;

namespace WorkBoard.Api.Infrastructure.Writes;

// Error responses for write endpoints: always `{ "error": "<code>" }`. Codes are listed in docs/api-contract.md.
public static class ApiErrors
{
    public const string InvalidRequest = "invalid_request";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string AdminOnly = "admin_only";
    public const string Conflict = "conflict";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
    public const string RequestInProgress = "request_in_progress";
    public const string GithubUnavailable = "github_unavailable";
    public const string GithubWriteNotGranted = "github_write_not_granted";

    public static IResult Of(int status, string code) => Results.Json(new ErrorBody(code), statusCode: status);

    public static IResult BadRequest(string code = InvalidRequest) => Of(StatusCodes.Status400BadRequest, code);
    public static IResult NotFoundResult() => Of(StatusCodes.Status404NotFound, NotFound);
    public static IResult ForbiddenResult(string code = Forbidden) => Of(StatusCodes.Status403Forbidden, code);
    public static IResult AdminOnlyResult() => Of(StatusCodes.Status403Forbidden, AdminOnly);
    public static IResult ConflictResult(string code = Conflict) => Of(StatusCodes.Status409Conflict, code);
    public static IResult GithubUnavailableResult() => Of(StatusCodes.Status502BadGateway, GithubUnavailable);
    public static IResult GithubWriteNotGrantedResult() => Of(StatusCodes.Status403Forbidden, GithubWriteNotGranted);
}
