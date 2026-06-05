namespace Pandas.PrintAgent.Core.Security;

public sealed class NullTokenStore : ITokenStore
{
    private readonly string _message;

    public NullTokenStore(string message = "No hay almacenamiento seguro de token configurado.")
    {
        _message = message;
    }

    public Task<TokenStoreAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TokenStoreAvailability(false, _message));
    }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        throw new TokenStoreUnavailableException(_message);
    }

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        throw new TokenStoreUnavailableException(_message);
    }
}
