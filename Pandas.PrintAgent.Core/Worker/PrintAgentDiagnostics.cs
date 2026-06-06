using System.Security.Cryptography;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Worker;

public static class PrintAgentDiagnostics
{
    public static async Task<PrintResult> RunTestPrintAsync(AgentSettings settings, IPrinterService printer, IAgentLogger logger, CancellationToken cancellationToken)
    {
        var target = PrinterService.TargetForSettings(settings);
        var payload = PrinterService.BuildTestPayload();
        logger.Log($"Enviando prueba directa al POS connector={settings.PrinterConnectorType.DisplayName()} target={target.Description} {DescribePayload(payload)}");

        var result = await printer.SendPayloadAsync(payload, target, settings, cancellationToken);
        logger.Log($"Prueba enviada: bytes={result.Bytes} connectMs={result.ConnectMs} writeMs={result.WriteMs} totalMs={result.TotalMs}");
        logger.Log("Si no salio papel con esta prueba, revisa el conector configurado, la cola instalada, IP/puerto, papel, tapa, firmware o modo de red de la impresora.");
        return result;
    }

    public static string DescribePayload(byte[] payload)
    {
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var shortHash = hash.Length > 16 ? hash[..16] : hash;
        var firstBytes = Convert.ToHexString(payload.Take(Math.Min(payload.Length, 16)).ToArray());
        return $"bytes={payload.Length} sha256={shortHash} firstBytes={firstBytes}";
    }

    public static void SavePayloadIfEnabled(string baseDirectory, AgentSettings settings, PrintJob job, byte[] payload, IAgentLogger logger)
    {
        if (!settings.SavePayloads)
        {
            return;
        }

        var directory = AgentPaths.ResolveAgentPath(baseDirectory, settings.PayloadDumpDirectory);
        Directory.CreateDirectory(directory);
        var filename = $"{DateTime.Now:yyyyMMdd-HHmmss}-{SafeFileName(job.DocumentType)}-{SafeFileName(job.DocumentNumber)}-{SafeFileName(job.Id)}.bin";
        var path = Path.Combine(directory, filename);
        File.WriteAllBytes(path, payload);
        logger.Log($"Payload guardado: {path}");
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "print-job" : cleaned;
    }
}
