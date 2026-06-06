using System.Diagnostics;
using System.Net.Sockets;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Printing;

public sealed class PrinterService : IPrinterService
{
    public async Task<PrintResult> SendPayloadAsync(byte[] payload, PrinterTarget target, AgentSettings settings, CancellationToken cancellationToken)
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

    public static PrinterTarget TargetForJob(PrintJob job, AgentSettings settings)
    {
        var host = settings.UseJobPrinterTarget && !string.IsNullOrWhiteSpace(job.TargetHost) ? job.TargetHost : settings.PrinterHost;
        var port = settings.UseJobPrinterTarget && job.TargetPort > 0 ? job.TargetPort : settings.PrinterPort;
        return new PrinterTarget(host!, port);
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
            "Si ves este papel, el socket 9100 funciona.\n" +
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
}
