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
    public async Task SaveAllowsEmptyTokenAndClearsStoredToken()
    {
        using var temp = new TempDirectory();
        var tokenStore = new InMemoryTokenStore();
        await tokenStore.SaveTokenAsync("old-token");
        var service = new AgentSettingsService(temp.Path, tokenStore);
        var settings = ValidSettings() with { AgentToken = "" };

        await service.SaveAsync(settings, settings.AgentToken);

        var content = await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "appsettings.json"));
        Assert.DoesNotContain("AgentToken", content);
        Assert.Null(await tokenStore.GetTokenAsync());
    }

    [Fact]
    public async Task SaveAllowsEmptyTokenWhenTokenStoreIsUnavailable()
    {
        using var temp = new TempDirectory();
        var service = new AgentSettingsService(temp.Path, new UnavailableTokenStore());
        var settings = ValidSettings() with { AgentToken = "" };

        await service.SaveAsync(settings, settings.AgentToken);

        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "appsettings.json")));
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

    [Fact]
    public async Task LoadUsesPandasEnvironmentOverrides()
    {
        var environment = new Dictionary<string, string?>
        {
            ["PANDAS_BACKEND_BASE_URL"] = "https://env-backend.example.com",
            ["PANDAS_API_PREFIX"] = "v1",
            ["PANDAS_PRINT_AGENT_TOKEN"] = "env-token",
            ["PANDAS_PRINTER_HOST"] = "192.168.1.20",
            ["PANDAS_PRINTER_PORT"] = "9200",
            ["PANDAS_POLL_INTERVAL_MS"] = "3000",
            ["PANDAS_PRINTER_TIMEOUT_MS"] = "7000",
            ["PANDAS_USE_JOB_PRINTER_TARGET"] = "true",
            ["PANDAS_PRINT_AGENT_LOG"] = "custom/log.txt",
            ["PANDAS_SAVE_PAYLOADS"] = "true",
            ["PANDAS_PAYLOAD_DUMP_DIRECTORY"] = "custom/payloads",
        };
        var previous = environment.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));

        try
        {
            foreach (var pair in environment)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            using var temp = new TempDirectory();
            var service = new AgentSettingsService(temp.Path, new InMemoryTokenStore());

            var settings = await service.LoadAsync();

            Assert.Equal("https://env-backend.example.com", settings.BackendBaseUrl);
            Assert.Equal("v1", settings.ApiPrefix);
            Assert.Equal("env-token", settings.AgentToken);
            Assert.Equal("192.168.1.20", settings.PrinterHost);
            Assert.Equal(9200, settings.PrinterPort);
            Assert.Equal(3000, settings.PollIntervalMs);
            Assert.Equal(7000, settings.PrinterTimeoutMs);
            Assert.True(settings.UseJobPrinterTarget);
            Assert.Equal("custom/log.txt", settings.LogFilePath);
            Assert.True(settings.SavePayloads);
            Assert.Equal("custom/payloads", settings.PayloadDumpDirectory);
        }
        finally
        {
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Fact]
    public void ValidateAllowsEmptyToken()
    {
        var settings = ValidSettings() with { AgentToken = "" };

        settings.Validate();
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
