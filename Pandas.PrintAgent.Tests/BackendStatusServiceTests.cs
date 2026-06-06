using System.Net;
using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Tests;

public sealed class BackendStatusServiceTests
{
    [Fact]
    public async Task CheckReportsConnectedForStatus200()
    {
        var service = new BackendStatusService(_ => Client(HttpStatusCode.OK, """
            {"ok":true,"mode":"queue","serverTime":"2026-06-02T00:00:00.000Z"}
            """));

        var result = await service.CheckAsync(ValidSettings());

        Assert.Equal(BackendStatusKind.Connected, result.Kind);
        Assert.Equal("queue", result.Status?.Mode);
    }

    [Fact]
    public async Task CheckReportsInvalidTokenForStatus401()
    {
        var service = new BackendStatusService(_ => Client(HttpStatusCode.Unauthorized, "invalid"));

        var result = await service.CheckAsync(ValidSettings());

        Assert.Equal(BackendStatusKind.InvalidToken, result.Kind);
    }

    [Fact]
    public async Task CheckReportsBackendUnreachableForNetworkFailure()
    {
        var service = new BackendStatusService(_ => new HttpClient(new ThrowingHandler())
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        });

        var result = await service.CheckAsync(ValidSettings());

        Assert.Equal(BackendStatusKind.BackendUnreachable, result.Kind);
    }

    [Fact]
    public void HttpClientAddsTokenHeaderWhenTokenIsConfigured()
    {
        using var client = PrintAgentHttpClientFactory.Create(ValidSettings() with { AgentToken = " secret-token " });

        Assert.True(client.DefaultRequestHeaders.TryGetValues("X-Print-Agent-Token", out var values));
        Assert.Equal("secret-token", Assert.Single(values));
    }

    [Fact]
    public void HttpClientOmitsTokenHeaderWhenTokenIsEmpty()
    {
        using var client = PrintAgentHttpClientFactory.Create(ValidSettings() with { AgentToken = "" });

        Assert.False(client.DefaultRequestHeaders.Contains("X-Print-Agent-Token"));
    }

    private static HttpClient Client(HttpStatusCode statusCode, string content)
    {
        return new HttpClient(new StaticResponseHandler(statusCode, content));
    }

    private static AgentSettings ValidSettings()
    {
        return new AgentSettings
        {
            BackendBaseUrl = "https://backend.example.com",
            AgentToken = "secret-token",
        };
    }
}
