namespace Pandas.PrintAgent.Core.Settings;

public sealed record AgentSettings
{
    public const string DefaultToken = "change-me-print-agent-token";

    public string BackendBaseUrl { get; init; } = "https://tu-backend-paleden.example.com";
    public string ApiPrefix { get; init; } = "api";
    public string AgentToken { get; init; } = DefaultToken;
    public int PollIntervalMs { get; init; } = 2000;
    public string PrinterHost { get; init; } = "10.0.0.28";
    public int PrinterPort { get; init; } = 9100;
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
            throw new InvalidOperationException("BackendBaseUrl debe ser una URL absoluta, por ejemplo https://api.paleden.com.");
        }
        if (string.IsNullOrWhiteSpace(AgentToken))
        {
            throw new InvalidOperationException("AgentToken no puede estar vacio.");
        }
        if (string.IsNullOrWhiteSpace(PrinterHost))
        {
            throw new InvalidOperationException("PrinterHost no puede estar vacio.");
        }
        if (PrinterPort <= 0)
        {
            throw new InvalidOperationException("PrinterPort debe ser mayor que cero.");
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
