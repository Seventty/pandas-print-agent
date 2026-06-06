using Pandas.PrintAgent.Core.Printing;

namespace Pandas.PrintAgent.Core.Settings;

internal sealed record AgentSettingsFile
{
    public string? BackendBaseUrl { get; init; }
    public string? ApiPrefix { get; init; }
    public string? AgentToken { get; init; }
    public int? PollIntervalMs { get; init; }
    public PrinterConnectorType? PrinterConnectorType { get; init; }
    public string? PrinterHost { get; init; }
    public int? PrinterPort { get; init; }
    public string? PrinterQueueName { get; init; }
    public int? PrinterTimeoutMs { get; init; }
    public bool? UseJobPrinterTarget { get; init; }
    public string? LogFilePath { get; init; }
    public bool? SavePayloads { get; init; }
    public string? PayloadDumpDirectory { get; init; }

    public AgentSettings ToSettings()
    {
        var defaults = new AgentSettings();
        return defaults with
        {
            BackendBaseUrl = BackendBaseUrl ?? defaults.BackendBaseUrl,
            ApiPrefix = ApiPrefix ?? defaults.ApiPrefix,
            AgentToken = AgentToken ?? defaults.AgentToken,
            PollIntervalMs = PollIntervalMs ?? defaults.PollIntervalMs,
            PrinterConnectorType = PrinterConnectorType ?? defaults.PrinterConnectorType,
            PrinterHost = PrinterHost ?? defaults.PrinterHost,
            PrinterPort = PrinterPort ?? defaults.PrinterPort,
            PrinterQueueName = PrinterQueueName ?? defaults.PrinterQueueName,
            PrinterTimeoutMs = PrinterTimeoutMs ?? defaults.PrinterTimeoutMs,
            UseJobPrinterTarget = UseJobPrinterTarget ?? defaults.UseJobPrinterTarget,
            LogFilePath = LogFilePath ?? defaults.LogFilePath,
            SavePayloads = SavePayloads ?? defaults.SavePayloads,
            PayloadDumpDirectory = PayloadDumpDirectory ?? defaults.PayloadDumpDirectory,
        };
    }

    public static AgentSettingsFile FromSettings(AgentSettings settings)
    {
        return new AgentSettingsFile
        {
            BackendBaseUrl = settings.BackendBaseUrl,
            ApiPrefix = settings.ApiPrefix,
            PollIntervalMs = settings.PollIntervalMs,
            PrinterConnectorType = settings.PrinterConnectorType,
            PrinterHost = settings.PrinterHost,
            PrinterPort = settings.PrinterPort,
            PrinterQueueName = settings.PrinterQueueName,
            PrinterTimeoutMs = settings.PrinterTimeoutMs,
            UseJobPrinterTarget = settings.UseJobPrinterTarget,
            LogFilePath = settings.LogFilePath,
            SavePayloads = settings.SavePayloads,
            PayloadDumpDirectory = settings.PayloadDumpDirectory,
        };
    }
}
