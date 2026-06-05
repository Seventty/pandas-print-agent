using System.Text.Json;
using Pandas.PrintAgent.Core;
using Pandas.PrintAgent.Core.Security;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Tests;

public sealed class AgentSettingsServiceTests
{
    [Fact]
    public void UrlBuildsWithAndWithoutApiPrefix()
    {
        var settings = new AgentSettings
        {
            BackendBaseUrl = "https://backend.example.com/",
            ApiPrefix = "api",
        };

        Assert.Equal("https://backend.example.com/api/print-agent/status", settings.Url("print-agent/status"));
        Assert.Equal("https://backend.example.com/print-agent/status", (settings with { ApiPrefix = "" }).Url("/print-agent/status"));
    }

    [Fact]
    public async Task SaveWritesNonSensitiveSettingsOnly()
    {
        using var temp = new TempDirectory();
        var tokenStore = new InMemoryTokenStore();
        var service = new AgentSettingsService(temp.Path, tokenStore);
        var settings = ValidSettings() with { AgentToken = "secret-token" };

        await service.SaveAsync(settings, settings.AgentToken);

        var content = await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "appsettings.json"));
        Assert.DoesNotContain("AgentToken", content);
        Assert.DoesNotContain("secret-token", content);
        Assert.Equal("secret-token", await tokenStore.GetTokenAsync());
    }

    [Fact]
    public async Task SaveDoesNotFallBackToPlaintextWhenTokenStoreIsUnavailable()
    {
        using var temp = new TempDirectory();
        var service = new AgentSettingsService(temp.Path, new UnavailableTokenStore());
        var settings = ValidSettings() with { AgentToken = "secret-token" };

        await Assert.ThrowsAsync<TokenStoreUnavailableException>(() => service.SaveAsync(settings, settings.AgentToken));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "appsettings.json")));
    }

    [Fact]
    public async Task LoadUsesLegacyFileTokenWhenSecureTokenIsMissing()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(temp.Path, "appsettings.json"),
            JsonSerializer.Serialize(new { BackendBaseUrl = "https://backend.example.com", AgentToken = "legacy-token" }, JsonOptions.Default));
        var service = new AgentSettingsService(temp.Path, new InMemoryTokenStore());

        var settings = await service.LoadAsync();

        Assert.Equal("legacy-token", settings.AgentToken);
    }

    private static AgentSettings ValidSettings()
    {
        return new AgentSettings
        {
            BackendBaseUrl = "https://backend.example.com",
            AgentToken = "secret-token",
            PrinterHost = "10.0.0.28",
            PrinterPort = 9100,
            PollIntervalMs = 1000,
            PrinterTimeoutMs = 1000,
        };
    }
}
