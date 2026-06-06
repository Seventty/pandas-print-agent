using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pandas.PrintAgent.Core.Printing;

public enum InstalledPrinterConnectorHint
{
    Unknown,
    Network,
    Usb,
    Bluetooth,
}

public static class InstalledPrinterConnectorHintExtensions
{
    public static string DisplayName(this InstalledPrinterConnectorHint connectorHint)
    {
        return connectorHint switch
        {
            InstalledPrinterConnectorHint.Network => "Red",
            InstalledPrinterConnectorHint.Usb => "USB",
            InstalledPrinterConnectorHint.Bluetooth => "Bluetooth",
            _ => "Sistema",
        };
    }
}

public sealed record InstalledPrinterInfo(
    string Name,
    InstalledPrinterConnectorHint ConnectorHint,
    bool IsDefault,
    string? DevicePath);

public interface IInstalledPrinterDiscoveryService
{
    Task<IReadOnlyList<InstalledPrinterInfo>> GetInstalledPrintersAsync(CancellationToken cancellationToken = default);
}

public sealed class InstalledPrinterDiscoveryService : IInstalledPrinterDiscoveryService
{
    public async Task<IReadOnlyList<InstalledPrinterInfo>> GetInstalledPrintersAsync(CancellationToken cancellationToken = default)
    {
        return OperatingSystem.IsWindows()
            ? WindowsInstalledPrinters.GetPrinters()
            : await CupsInstalledPrinters.GetPrintersAsync(cancellationToken);
    }

    private static InstalledPrinterConnectorHint GuessConnector(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return InstalledPrinterConnectorHint.Unknown;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("bluetooth") || normalized.Contains("bt:") || normalized.StartsWith("bth", StringComparison.Ordinal))
        {
            return InstalledPrinterConnectorHint.Bluetooth;
        }

        if (normalized.Contains("usb") || normalized.StartsWith("usb", StringComparison.Ordinal))
        {
            return InstalledPrinterConnectorHint.Usb;
        }

        if (normalized.Contains("socket://") ||
            normalized.Contains("ipp://") ||
            normalized.Contains("ipps://") ||
            normalized.Contains("lpd://") ||
            normalized.Contains("dnssd://") ||
            normalized.StartsWith("ip_", StringComparison.Ordinal) ||
            normalized.StartsWith("tcp", StringComparison.Ordinal) ||
            normalized.Contains("9100"))
        {
            return InstalledPrinterConnectorHint.Network;
        }

        return InstalledPrinterConnectorHint.Unknown;
    }

    private static class CupsInstalledPrinters
    {
        public static async Task<IReadOnlyList<InstalledPrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken)
        {
            var printers = new List<InstalledPrinterInfo>();
            var defaultPrinter = await GetDefaultPrinterAsync(cancellationToken);
            var result = await RunProcessAsync("lpstat", ["-v"], cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"No se pudieron listar impresoras instaladas con CUPS: {result.Error.Trim()}");
            }

            foreach (var line in result.Output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
            {
                const string prefix = "device for ";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = line[prefix.Length..];
                var separator = rest.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var name = rest[..separator].Trim();
                var devicePath = rest[(separator + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                printers.Add(new InstalledPrinterInfo(
                    name,
                    GuessConnector(devicePath),
                    string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase),
                    devicePath));
            }

            return printers
                .GroupBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(printer => printer.IsDefault)
                .ThenBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static async Task<string?> GetDefaultPrinterAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await RunProcessAsync("lpstat", ["-d"], cancellationToken);
                if (result.ExitCode != 0)
                {
                    return null;
                }

                const string prefix = "system default destination:";
                var line = result.Output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                return line is null ? null : line[prefix.Length..].Trim();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static async Task<ProcessResult> RunProcessAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"No se pudo iniciar {fileName}.");
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
            }
            catch (Win32Exception error)
            {
                throw new InvalidOperationException("No se encontro lpstat. Instala o habilita CUPS para listar impresoras.", error);
            }
        }
    }

    private static class WindowsInstalledPrinters
    {
        private const uint PrinterEnumLocal = 0x00000002;
        private const uint PrinterEnumConnections = 0x00000004;
        private const int PrinterAttributeDefault = 0x00000004;

        public static IReadOnlyList<InstalledPrinterInfo> GetPrinters()
        {
            var flags = PrinterEnumLocal | PrinterEnumConnections;
            _ = EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out var needed, out _);
            if (needed <= 0)
            {
                return [];
            }

            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!EnumPrinters(flags, null, 2, buffer, needed, out _, out var returned))
                {
                    throw new InvalidOperationException($"No se pudieron listar impresoras instaladas. Win32Error={Marshal.GetLastWin32Error()}");
                }

                var printers = new List<InstalledPrinterInfo>(returned);
                var structureSize = Marshal.SizeOf<PrinterInfo2>();
                for (var index = 0; index < returned; index++)
                {
                    var pointer = IntPtr.Add(buffer, index * structureSize);
                    var info = Marshal.PtrToStructure<PrinterInfo2>(pointer);
                    if (string.IsNullOrWhiteSpace(info.PrinterName))
                    {
                        continue;
                    }

                    printers.Add(new InstalledPrinterInfo(
                        info.PrinterName,
                        GuessConnector($"{info.PortName} {info.DriverName} {info.Location}"),
                        (info.Attributes & PrinterAttributeDefault) == PrinterAttributeDefault,
                        info.PortName));
                }

                return printers
                    .GroupBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderByDescending(printer => printer.IsDefault)
                    .ThenBy(printer => printer.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PrinterInfo2
        {
            public string? ServerName;
            public string? PrinterName;
            public string? ShareName;
            public string? PortName;
            public string? DriverName;
            public string? Comment;
            public string? Location;
            public IntPtr DevMode;
            public string? SepFile;
            public string? PrintProcessor;
            public string? DataType;
            public string? Parameters;
            public IntPtr SecurityDescriptor;
            public int Attributes;
            public int Priority;
            public int DefaultPriority;
            public int StartTime;
            public int UntilTime;
            public int Status;
            public int Jobs;
            public int AveragePagesPerMinute;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool EnumPrinters(
            uint flags,
            string? name,
            int level,
            IntPtr printerEnum,
            int bufferSize,
            out int needed,
            out int returned);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
