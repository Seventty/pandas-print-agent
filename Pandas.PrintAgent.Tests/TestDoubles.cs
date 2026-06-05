using System.Net;
using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Security;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pandas-print-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class InMemoryTokenStore : ITokenStore
{
    private string? _token;

    public Task<TokenStoreAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TokenStoreAvailability(true, "Disponible"));
    }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_token);
    }

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        _token = token;
        return Task.CompletedTask;
    }
}

internal sealed class UnavailableTokenStore : ITokenStore
{
    public Task<TokenStoreAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TokenStoreAvailability(false, "No disponible"));
    }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        throw new TokenStoreUnavailableException("No disponible");
    }

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        throw new TokenStoreUnavailableException("No disponible");
    }
}

internal sealed class StaticResponseHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;

    public StaticResponseHandler(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content),
        });
    }
}

internal sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new HttpRequestException("offline");
    }
}

internal sealed class BlockingApiClient : IPrintAgentApiClient, IDisposable
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Disposed { get; private set; }

    public Task<PrintAgentBackendStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new PrintAgentBackendStatus(true, "queue", DateTimeOffset.UtcNow));
    }

    public async Task<PrintJob?> FetchNextJobAsync(CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return null;
    }

    public Task CompleteJobAsync(string jobId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task FailJobAsync(string jobId, string error, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disposed = true;
    }
}

internal sealed class NoopPrinterService : IPrinterService
{
    public Task<PrintResult> SendPayloadAsync(byte[] payload, PrinterTarget target, AgentSettings settings, CancellationToken cancellationToken)
    {
        return Task.FromResult(new PrintResult(payload.Length, 0, 0, 0));
    }
}
