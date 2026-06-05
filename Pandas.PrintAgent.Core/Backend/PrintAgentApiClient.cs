using System.Net.Http.Json;
using System.Text.Json;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Backend;

public sealed class PrintAgentApiClient : IPrintAgentApiClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly AgentSettings _settings;

    public PrintAgentApiClient(HttpClient http, AgentSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<PrintAgentBackendStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(_settings.Url("print-agent/status"), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BackendRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<PrintAgentBackendStatus>(JsonOptions.Default, cancellationToken)
            ?? throw new InvalidOperationException("El backend no devolvio status del agente.");
    }

    public async Task<PrintJob?> FetchNextJobAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(_settings.Url("print-agent/jobs/next"), JsonContent.Create(new { }), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BackendRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content) || content.Trim() == "null")
        {
            return null;
        }

        return JsonSerializer.Deserialize<PrintJob>(content, JsonOptions.Default);
    }

    public async Task CompleteJobAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(_settings.Url($"print-agent/jobs/{jobId}/complete"), JsonContent.Create(new { }), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BackendRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    public async Task FailJobAsync(string jobId, string error, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(_settings.Url($"print-agent/jobs/{jobId}/fail"), new { error }, JsonOptions.Default, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BackendRequestException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
