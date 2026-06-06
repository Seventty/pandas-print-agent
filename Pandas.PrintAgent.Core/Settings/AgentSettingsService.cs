using System.Text.Json;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Security;

namespace Pandas.PrintAgent.Core.Settings;

public sealed class AgentSettingsService
{
    public const string DefaultSettingsFileName = "appsettings.json";

    private readonly string _baseDirectory;
    private readonly string _settingsPath;
    private readonly ITokenStore _tokenStore;

    public AgentSettingsService(string baseDirectory, ITokenStore tokenStore, string settingsFileName = DefaultSettingsFileName)
    {
        _baseDirectory = baseDirectory;
        _settingsPath = Path.Combine(baseDirectory, settingsFileName);
        _tokenStore = tokenStore;
    }

    public string SettingsPath => _settingsPath;

    public async Task<AgentSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var fileSettings = await LoadFileAsync(cancellationToken);
        var settings = fileSettings.ToSettings();
        var storedToken = await ReadStoredTokenAsync(cancellationToken);
        var environmentToken = Environment.GetEnvironmentVariable("PANDAS_PRINT_AGENT_TOKEN");

        settings = settings with
        {
            BackendBaseUrl = Environment.GetEnvironmentVariable("PANDAS_BACKEND_BASE_URL") ?? settings.BackendBaseUrl,
            ApiPrefix = Environment.GetEnvironmentVariable("PANDAS_API_PREFIX") ?? settings.ApiPrefix,
            AgentToken = FirstNonEmpty(environmentToken, storedToken, fileSettings.AgentToken, settings.AgentToken),
            PrinterConnectorType = ConnectorEnv("PANDAS_PRINTER_CONNECTOR", settings.PrinterConnectorType),
            PrinterHost = Environment.GetEnvironmentVariable("PANDAS_PRINTER_HOST") ?? settings.PrinterHost,
            PrinterPort = IntEnv("PANDAS_PRINTER_PORT", settings.PrinterPort),
            PrinterQueueName = Environment.GetEnvironmentVariable("PANDAS_PRINTER_QUEUE_NAME") ?? settings.PrinterQueueName,
            PollIntervalMs = IntEnv("PANDAS_POLL_INTERVAL_MS", settings.PollIntervalMs),
            PrinterTimeoutMs = IntEnv("PANDAS_PRINTER_TIMEOUT_MS", settings.PrinterTimeoutMs),
            UseJobPrinterTarget = BoolEnv("PANDAS_USE_JOB_PRINTER_TARGET", settings.UseJobPrinterTarget),
            LogFilePath = Environment.GetEnvironmentVariable("PANDAS_PRINT_AGENT_LOG") ?? settings.LogFilePath,
            SavePayloads = BoolEnv("PANDAS_SAVE_PAYLOADS", settings.SavePayloads),
            PayloadDumpDirectory = Environment.GetEnvironmentVariable("PANDAS_PAYLOAD_DUMP_DIRECTORY") ?? settings.PayloadDumpDirectory,
        };

        settings.Validate();
        return settings;
    }

    public async Task SaveAsync(AgentSettings settings, string? tokenToSave = null, CancellationToken cancellationToken = default)
    {
        settings.Validate();

        if (tokenToSave is not null)
        {
            var trimmedToken = tokenToSave.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedToken))
            {
                var availability = await _tokenStore.CheckAvailabilityAsync(cancellationToken);
                if (!availability.IsAvailable)
                {
                    throw new TokenStoreUnavailableException(availability.Message);
                }

                await _tokenStore.SaveTokenAsync(trimmedToken, cancellationToken);
            }
            else
            {
                var availability = await _tokenStore.CheckAvailabilityAsync(cancellationToken);
                if (availability.IsAvailable)
                {
                    await _tokenStore.DeleteTokenAsync(cancellationToken);
                }
            }
        }

        Directory.CreateDirectory(_baseDirectory);
        var payload = AgentSettingsFile.FromSettings(settings);
        await File.WriteAllTextAsync(_settingsPath, JsonSerializer.Serialize(payload, JsonOptions.Default), cancellationToken);
    }

    private async Task<AgentSettingsFile> LoadFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AgentSettingsFile();
        }

        var content = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new AgentSettingsFile();
        }

        return JsonSerializer.Deserialize<AgentSettingsFile>(content, JsonOptions.Default) ?? new AgentSettingsFile();
    }

    private async Task<string?> ReadStoredTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _tokenStore.GetTokenAsync(cancellationToken);
        }
        catch (TokenStoreUnavailableException)
        {
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return AgentSettings.DefaultToken;
    }

    private static int IntEnv(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }

    private static bool BoolEnv(string name, bool fallback)
    {
        return bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }

    private static PrinterConnectorType ConnectorEnv(string name, PrinterConnectorType fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var normalized = raw.Trim().Replace("-", string.Empty).Replace("_", string.Empty).Replace("/", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "networktcp" or "network" or "tcp" or "tcpip" or "wifi" or "ethernet" or "wifiethernet" => PrinterConnectorType.NetworkTcp,
            "usb" => PrinterConnectorType.Usb,
            "bluetooth" or "bt" => PrinterConnectorType.Bluetooth,
            _ => Enum.TryParse<PrinterConnectorType>(raw, ignoreCase: true, out var parsed) ? parsed : fallback,
        };
    }
}
