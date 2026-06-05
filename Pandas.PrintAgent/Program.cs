using System.Text;
using Pandas.PrintAgent.Core;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Security;
using Pandas.PrintAgent.Core.Settings;
using Pandas.PrintAgent.Core.Worker;

Console.OutputEncoding = Encoding.UTF8;

try
{
    var baseDirectory = AppContext.BaseDirectory;
    var settingsService = new AgentSettingsService(baseDirectory, new SecureTokenStore());
    var settings = await settingsService.LoadAsync();
    var logger = new FileAgentLogger(baseDirectory, settings.LogFilePath);
    var printer = new PrinterService();

    if (args.Any(arg => string.Equals(arg, "--test-print", StringComparison.OrdinalIgnoreCase)))
    {
        logger.Log("PANDAS Print Agent - prueba directa de POS");
        LogStartup(baseDirectory, settings, logger);
        await PrintAgentDiagnostics.RunTestPrintAsync(settings, printer, logger, CancellationToken.None);
        return;
    }

    logger.Log("Presiona Ctrl+C para detener.");
    logger.Log(string.Empty);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    await using var worker = new PrintAgentWorker(baseDirectory, printer, logger);
    await worker.StartAsync(settings, cancellation.Token);

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C requested.
    }

    await worker.StopAsync();
}
catch (Exception error)
{
    Console.WriteLine($"No se pudo iniciar el agente: {error.Message}");
    Console.WriteLine("Presiona cualquier tecla para cerrar.");
    Console.ReadKey(intercept: true);
}

static void LogStartup(string baseDirectory, AgentSettings settings, IAgentLogger logger)
{
    logger.Log($"Backend: {settings.BackendBaseUrl}");
    logger.Log($"POS: {settings.PrinterHost}:{settings.PrinterPort}");
    logger.Log($"UseJobPrinterTarget: {settings.UseJobPrinterTarget}");
    logger.Log($"Log: {AgentPaths.ResolveAgentPath(baseDirectory, settings.LogFilePath)}");
    if (settings.SavePayloads)
    {
        logger.Log($"Payload dumps: {AgentPaths.ResolveAgentPath(baseDirectory, settings.PayloadDumpDirectory)}");
    }
}
