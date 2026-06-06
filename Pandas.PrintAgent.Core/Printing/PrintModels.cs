namespace Pandas.PrintAgent.Core.Printing;

public enum PrinterConnectorType
{
    NetworkTcp,
    Usb,
    Bluetooth,
}

public static class PrinterConnectorTypeExtensions
{
    public static string DisplayName(this PrinterConnectorType connectorType)
    {
        return connectorType switch
        {
            PrinterConnectorType.NetworkTcp => "WiFi/Ethernet (TCP)",
            PrinterConnectorType.Usb => "USB",
            PrinterConnectorType.Bluetooth => "Bluetooth",
            _ => connectorType.ToString(),
        };
    }

    public static string TestButtonName(this PrinterConnectorType connectorType)
    {
        return connectorType switch
        {
            PrinterConnectorType.NetworkTcp => "WiFi/Ethernet",
            PrinterConnectorType.Usb => "USB",
            PrinterConnectorType.Bluetooth => "Bluetooth",
            _ => connectorType.ToString(),
        };
    }
}

public sealed record PrinterTarget(PrinterConnectorType ConnectorType, string Host, int Port, string QueueName)
{
    public PrinterTarget(string host, int port)
        : this(PrinterConnectorType.NetworkTcp, host, port, string.Empty)
    {
    }

    public static PrinterTarget ForNetwork(string host, int port)
    {
        return new PrinterTarget(PrinterConnectorType.NetworkTcp, host, port, string.Empty);
    }

    public static PrinterTarget ForInstalledPrinter(PrinterConnectorType connectorType, string queueName)
    {
        if (connectorType == PrinterConnectorType.NetworkTcp)
        {
            throw new ArgumentException("NetworkTcp usa host y puerto, no una cola instalada.", nameof(connectorType));
        }

        return new PrinterTarget(connectorType, string.Empty, 0, queueName);
    }

    public string Description => ConnectorType == PrinterConnectorType.NetworkTcp
        ? $"{Host}:{Port}"
        : $"{ConnectorType.DisplayName()} queue=\"{QueueName}\"";
}

public sealed record PrintResult(int Bytes, long ConnectMs, long WriteMs, long TotalMs);

public sealed record PrintJob(
    string Id,
    string DocumentType,
    string DocumentNumber,
    string PayloadBase64,
    string? TargetHost,
    int TargetPort,
    int Attempt,
    int MaxAttempts
);
