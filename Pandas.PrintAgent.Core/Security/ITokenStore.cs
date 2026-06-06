namespace Pandas.PrintAgent.Core.Security;

public interface ITokenStore
{
    Task<TokenStoreAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
    Task SaveTokenAsync(string token, CancellationToken cancellationToken = default);
    Task DeleteTokenAsync(CancellationToken cancellationToken = default);
}

public sealed record TokenStoreAvailability(bool IsAvailable, string Message);

public sealed class TokenStoreUnavailableException : InvalidOperationException
{
    public TokenStoreUnavailableException(string message)
        : base(message)
    {
    }
}
