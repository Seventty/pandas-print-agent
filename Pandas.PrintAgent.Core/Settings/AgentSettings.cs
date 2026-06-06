using Pandas.PrintAgent.Core.Printing;

namespace Pandas.PrintAgent.Core.Settings;

public sealed record AgentSettings
{
    public const string DefaultToken = "";

    public string BackendBaseUrl { get; init; } = "https://backend.example.com";
    public string ApiPrefix { get; init; } = "api";
    public string AgentToken { get; init; } = DefaultToken;
    public int PollIntervalMs { get; init; } = 2000;
    public PrinterConnectorType PrinterConnectorType { get; init; } = PrinterConnectorType.NetworkTcp;
    public string PrinterHost { get; init; } = "10.0.0.28";
    public int PrinterPort { get; init; } = 9100;
    public string PrinterQueueName { get; init; } = string.Empty;
    public int PrinterTimeoutMs { get; init; } = 5000;
    public bool UseJobPrinterTarget { get; init; }
    public string LogFilePath { get; init; } = "logs/print-agent.log";
    public bool SavePayloads { get; init; }
    public string PayloadDumpDirectory { get; init; } = "logs/payloads";

    public string Url(string path)
    {
        var root = BackendBaseUrl.TrimEnd('/');
        var prefix = ApiPrefix.Trim('/');
        return string.IsNullOrWhiteSpace(prefix)
            ? $"{root}/{path.TrimStart('/')}"
            : $"{root}/{prefix}/{path.TrimStart('/')}";
    }

    public void Validate()
    {
        if (!Uri.TryCreate(BackendBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("BackendBaseUrl debe ser una URL absoluta, por ejemplo https://backend.example.com.");
        }
        if (PrinterConnectorType == PrinterConnectorType.NetworkTcp && string.IsNullOrWhiteSpace(PrinterHost))
        {
            throw new InvalidOperationException("PrinterHost no puede estar vacio.");
        }
        if (PrinterConnectorType == PrinterConnectorType.NetworkTcp && PrinterPort <= 0)
        {
            throw new InvalidOperationException("PrinterPort debe ser mayor que cero.");
        }
        if (PrinterConnectorType != PrinterConnectorType.NetworkTcp && string.IsNullOrWhiteSpace(PrinterQueueName))
        {
            throw new InvalidOperationException("PrinterQueueName no puede estar vacio para conectores USB o Bluetooth.");
        }
        if (PollIntervalMs <= 0)
        {
            throw new InvalidOperationException("PollIntervalMs debe ser mayor que cero.");
        }
        if (PrinterTimeoutMs <= 0)
        {
            throw new InvalidOperationException("PrinterTimeoutMs debe ser mayor que cero.");
        }
        if (string.IsNullOrWhiteSpace(LogFilePath))
        {
            throw new InvalidOperationException("LogFilePath no puede estar vacio.");
        }
        if (string.IsNullOrWhiteSpace(PayloadDumpDirectory))
        {
            throw new InvalidOperationException("PayloadDumpDirectory no puede estar vacio.");
        }
    }
}
