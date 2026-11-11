namespace WorkBoard.Api.Infrastructure.Identity;

public interface ICurrentUser
{
    DevIdentity Identity { get; }
}

public sealed class CurrentUser : ICurrentUser
{
    private DevIdentity? _identity;

    public DevIdentity Identity =>
        _identity ?? throw new InvalidOperationException("No authenticated user on this request.");

    public void Set(DevIdentity identity) => _identity = identity;
}
