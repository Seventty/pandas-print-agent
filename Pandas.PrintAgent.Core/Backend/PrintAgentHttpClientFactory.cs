using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Backend;

public static class PrintAgentHttpClientFactory
{
    public static HttpClient Create(AgentSettings settings)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!string.IsNullOrWhiteSpace(settings.AgentToken))
        {
            http.DefaultRequestHeaders.Add("X-Print-Agent-Token", settings.AgentToken.Trim());
        }

        return http;
    }
}
