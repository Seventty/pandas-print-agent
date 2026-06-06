using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Printing;

public sealed class PrinterService : IPrinterService
{
    public async Task<PrintResult> SendPayloadAsync(byte[] payload, PrinterTarget target, AgentSettings settings, CancellationToken cancellationToken)
    {
        return target.ConnectorType == PrinterConnectorType.NetworkTcp
            ? await SendTcpPayloadAsync(payload, target, settings, cancellationToken)
            : await SendInstalledPrinterPayloadAsync(payload, target, settings, cancellationToken);
    }

    public static PrinterTarget TargetForSettings(AgentSettings settings)
    {
        return settings.PrinterConnectorType == PrinterConnectorType.NetworkTcp
            ? PrinterTarget.ForNetwork(settings.PrinterHost, settings.PrinterPort)
            : PrinterTarget.ForInstalledPrinter(settings.PrinterConnectorType, settings.PrinterQueueName);
    }

    public static PrinterTarget TargetForJob(PrintJob job, AgentSettings settings)
    {
        if (settings.PrinterConnectorType != PrinterConnectorType.NetworkTcp)
        {
            return PrinterTarget.ForInstalledPrinter(settings.PrinterConnectorType, settings.PrinterQueueName);
        }

        var host = settings.UseJobPrinterTarget && !string.IsNullOrWhiteSpace(job.TargetHost) ? job.TargetHost : settings.PrinterHost;
        var port = settings.UseJobPrinterTarget && job.TargetPort > 0 ? job.TargetPort : settings.PrinterPort;
        return PrinterTarget.ForNetwork(host!, port);
    }

    private static async Task<PrintResult> SendTcpPayloadAsync(byte[] payload, PrinterTarget target, AgentSettings settings, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.PrinterTimeoutMs);

        var total = Stopwatch.StartNew();
        using var client = new TcpClient { NoDelay = true };

        var connect = Stopwatch.StartNew();
        await client.ConnectAsync(target.Host, target.Port, timeout.Token);
        connect.Stop();

        await using var stream = client.GetStream();
        var write = Stopwatch.StartNew();
        await stream.WriteAsync(payload, timeout.Token);
        await stream.FlushAsync(timeout.Token);
        write.Stop();
        total.Stop();

        return new PrintResult(payload.Length, connect.ElapsedMilliseconds, write.ElapsedMilliseconds, total.ElapsedMilliseconds);
    }

    private static async Task<PrintResult> SendInstalledPrinterPayloadAsync(byte[] payload, PrinterTarget target, AgentSettings settings, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.PrinterTimeoutMs);

        return OperatingSystem.IsWindows()
            ? await SendWindowsInstalledPrinterPayloadAsync(payload, target, timeout.Token)
            : await SendCupsInstalledPrinterPayloadAsync(payload, target, timeout.Token);
    }

    private static async Task<PrintResult> SendWindowsInstalledPrinterPayloadAsync(byte[] payload, PrinterTarget target, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        await Task.Run(() => WindowsRawPrinter.Send(target.QueueName, payload), cancellationToken).WaitAsync(cancellationToken);
        total.Stop();
        return new PrintResult(payload.Length, 0, total.ElapsedMilliseconds, total.ElapsedMilliseconds);
    }

    private static async Task<PrintResult> SendCupsInstalledPrinterPayloadAsync(byte[] payload, PrinterTarget target, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var tempFile = Path.Combine(Path.GetTempPath(), $"pandas-print-agent-{Guid.NewGuid():N}.bin");

        try
        {
            await File.WriteAllBytesAsync(tempFile, payload, cancellationToken);
            var startInfo = new ProcessStartInfo
            {
                FileName = "lp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("raw");
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(target.QueueName);
            startInfo.ArgumentList.Add(tempFile);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("No se pudo iniciar el comando lp para imprimir por cola instalada.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidOperationException($"No se pudo enviar a la cola {target.QueueName}: {message.Trim()}");
            }

            total.Stop();
            return new PrintResult(payload.Length, 0, total.ElapsedMilliseconds, total.ElapsedMilliseconds);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new InvalidOperationException("No se encontro el comando lp. Instala o habilita CUPS para imprimir por USB/Bluetooth.", error);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // El archivo temporal no debe bloquear el flujo si CUPS ya recibio el trabajo.
            }
        }
    }

    public static byte[] BuildTestPayload()
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var payload = new List<byte>();

        payload.AddRange([0x1b, (byte)'@']);
        payload.AddRange([0x1b, (byte)'t', 0x00]);
        payload.AddRange([0x1b, (byte)'a', 0x01]);
        payload.AddRange([0x1b, (byte)'E', 0x01]);
        payload.AddRange(EncodeEscPosText(
            "█████▄ ▄████▄ ███  ██ ████▄  ▄████▄ ▄█████\n" +
            "██▄▄█▀ ██▄▄██ ██ ▀▄██ ██  ██ ██▄▄██ ▀▀▀▄▄▄\n" +
            "██     ██  ██ ██   ██ ████▀  ██  ██ █████▀\n" +
            "TEST POD\n"));
        payload.AddRange([0x1b, (byte)'E', 0x00]);
        payload.AddRange(EncodeEscPosText(
            "Prueba directa sin backend\n" +
            $"{now}\n" +
            "Si ves este papel, la conexion con tu impresora funciona.\n" +
            "\n\n\n"));
        payload.AddRange([0x1d, (byte)'V', 0x00]);

        return payload.ToArray();
    }

    private static byte[] EncodeEscPosText(string text)
    {
        var bytes = new List<byte>(text.Length);

        foreach (var character in text)
        {
            // ESC/POS PC437 bytes for the block glyphs used in the test header.
            var value = character switch
            {
                '█' => (byte)0xdb,
                '▄' => (byte)0xdc,
                '▀' => (byte)0xdf,
                <= '\x7f' => (byte)character,
                _ => (byte)'?',
            };

            bytes.Add(value);
        }

        return bytes.ToArray();
    }

    private static class WindowsRawPrinter
    {
        public static void Send(string printerName, byte[] payload)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new InvalidOperationException("El nombre de la impresora instalada no puede estar vacio.");
            }

            if (!OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
            {
                ThrowWinspoolError($"No se pudo abrir la impresora instalada {printerName}.");
            }

            try
            {
                var docInfo = new DocInfo1
                {
                    DocumentName = "PANDAS Print Agent",
                    OutputFile = null,
                    DataType = "RAW",
                };

                if (StartDocPrinter(printerHandle, 1, ref docInfo) == 0)
                {
                    ThrowWinspoolError($"No se pudo iniciar el documento RAW en {printerName}.");
                }

                try
                {
                    if (!StartPagePrinter(printerHandle))
                    {
                        ThrowWinspoolError($"No se pudo iniciar pagina en {printerName}.");
                    }

                    try
                    {
                        if (!WritePrinter(printerHandle, payload, payload.Length, out var written) || written != payload.Length)
                        {
                            ThrowWinspoolError($"No se pudo escribir el payload completo en {printerName}.");
                        }
                    }
                    finally
                    {
                        EndPagePrinter(printerHandle);
                    }
                }
                finally
                {
                    EndDocPrinter(printerHandle);
                }
            }
            finally
            {
                ClosePrinter(printerHandle);
            }
        }

        private static void ThrowWinspoolError(string message)
        {
            throw new InvalidOperationException($"{message} Win32Error={Marshal.GetLastWin32Error()}");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DocInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string DocumentName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? OutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string DataType;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo1 docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr printer, byte[] buffer, int count, out int written);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr printer);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr printer);
    }
}
