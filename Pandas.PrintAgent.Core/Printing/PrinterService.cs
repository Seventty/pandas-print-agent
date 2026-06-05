using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
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
        var payload =
            "\x1b@" +
            "\x1ba\x01" +
            "\x1bE\x01" +
            "PALEDEN TEST POS\n" +
            "\x1bE\x00" +
            "Prueba directa sin backend\n" +
            $"{now}\n" +
            "Si ves este papel, el socket 9100 funciona.\n" +
            "\n\n\n" +
            "\x1dV\x00";

        return Encoding.ASCII.GetBytes(payload);
    }
}
