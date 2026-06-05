using System.Net;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Backend;

public sealed class BackendStatusService
{
    private readonly Func<AgentSettings, HttpClient> _httpClientFactory;

    public BackendStatusService(Func<AgentSettings, HttpClient>? httpClientFactory = null)
    {
        _httpClientFactory = httpClientFactory ?? PrintAgentHttpClientFactory.Create;
    }

    public async Task<BackendStatusCheck> CheckAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = _httpClientFactory(settings);
            var client = new PrintAgentApiClient(http, settings);
            var status = await client.GetStatusAsync(cancellationToken);
            return new BackendStatusCheck(BackendStatusKind.Connected, "Conectado", status);
        }
        catch (BackendRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new BackendStatusCheck(BackendStatusKind.InvalidToken, "Token invalido");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return new BackendStatusCheck(BackendStatusKind.BackendUnreachable, $"Backend no alcanzable: {error.Message}");
        }
    }
}
