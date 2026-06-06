using System.Net;
using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Worker;

public sealed class PrintAgentWorker : IAsyncDisposable
{
    private readonly string _baseDirectory;
    private readonly IPrinterService _printer;
    private readonly IAgentLogger _logger;
    private readonly Func<AgentSettings, IPrintAgentApiClient> _apiClientFactory;
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private string? _lastJob;
    private string? _lastError;
    private int _activeLoopCount;

    public PrintAgentWorker(
        string baseDirectory,
        IPrinterService printer,
        IAgentLogger logger,
        Func<AgentSettings, IPrintAgentApiClient>? apiClientFactory = null)
    {
        _baseDirectory = baseDirectory;
        _printer = printer;
        _logger = logger;
        _apiClientFactory = apiClientFactory ?? (settings => new PrintAgentApiClient(PrintAgentHttpClientFactory.Create(settings), settings));
    }

    public event EventHandler<AgentStatusSnapshot>? StatusChanged;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _loopTask is { IsCompleted: false };
            }
        }
    }

    public int ActiveLoopCount => Volatile.Read(ref _activeLoopCount);

    public async Task StartAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        settings.Validate();

        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _cancellation = linkedCancellation;
            _loopTask = Task.Run(() => RunLoopAsync(settings, linkedCancellation.Token), CancellationToken.None);
        }
    }

    public Task ReloadAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        return StartAsync(settings, cancellationToken);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? loopTask;

        lock (_sync)
        {
            cancellation = _cancellation;
            loopTask = _loopTask;
            _cancellation = null;
            _loopTask = null;
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync();
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        cancellation?.Dispose();
        Report(AgentWorkerState.Stopped, "Agente detenido.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task RunLoopAsync(AgentSettings settings, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeLoopCount);
        Report(AgentWorkerState.Starting, "Iniciando agente...");

        var apiClient = _apiClientFactory(settings);
        try
        {
            _logger.Log("PANDAS Print Agent");
            LogStartup(settings);

            while (!cancellationToken.IsCancellationRequested)
            {
                await PollOnceAsync(settings, apiClient, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            if (apiClient is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Interlocked.Decrement(ref _activeLoopCount);
        }
    }

    private async Task PollOnceAsync(AgentSettings settings, IPrintAgentApiClient apiClient, CancellationToken cancellationToken)
    {
        try
        {
            var job = await apiClient.FetchNextJobAsync(cancellationToken);
            if (job is null)
            {
                Report(AgentWorkerState.Idle, "Conectado. Sin trabajos pendientes.");
                await Task.Delay(settings.PollIntervalMs, cancellationToken);
                return;
            }

            await PrintJobAsync(settings, apiClient, job, cancellationToken);
        }
        catch (BackendRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            _lastError = error.Message;
            _logger.Log($"Token de agente invalido: {error.Message}");
            Report(AgentWorkerState.InvalidToken, "Token invalido.", lastError: _lastError);
            await Task.Delay(settings.PollIntervalMs, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or BackendRequestException or TaskCanceledException or TimeoutException)
        {
            _lastError = error.Message;
            _logger.Log($"Backend no alcanzable: {error.Message}");
            Report(AgentWorkerState.BackendUnreachable, "Backend no alcanzable.", lastError: _lastError);
            await Task.Delay(settings.PollIntervalMs, cancellationToken);
        }
        catch (Exception error)
        {
            _lastError = error.Message;
            _logger.Log($"Error del agente: {error.Message}");
            Report(AgentWorkerState.Error, "Error del agente.", lastError: _lastError);
            await Task.Delay(settings.PollIntervalMs, cancellationToken);
        }
    }

    private async Task PrintJobAsync(AgentSettings settings, IPrintAgentApiClient apiClient, PrintJob job, CancellationToken cancellationToken)
    {
        var target = PrinterService.TargetForJob(job, settings);
        var payload = Convert.FromBase64String(job.PayloadBase64);
        _lastJob = $"{job.DocumentType} {job.DocumentNumber} (job {job.Id})";

        Report(AgentWorkerState.Printing, $"Imprimiendo {_lastJob}", lastJob: _lastJob);
        _logger.Log($"Imprimiendo {job.DocumentType} {job.DocumentNumber} (job {job.Id}, intento {job.Attempt}/{job.MaxAttempts}) connector={settings.PrinterConnectorType.DisplayName()} target={target.Description} {PrintAgentDiagnostics.DescribePayload(payload)}");
        PrintAgentDiagnostics.SavePayloadIfEnabled(_baseDirectory, settings, job, payload, _logger);

        try
        {
            var result = await _printer.SendPayloadAsync(payload, target, settings, cancellationToken);
            await apiClient.CompleteJobAsync(job.Id, cancellationToken);
            _lastError = null;
            _logger.Log($"Impreso {job.DocumentNumber}: bytes={result.Bytes} connectMs={result.ConnectMs} writeMs={result.WriteMs} totalMs={result.TotalMs}");
            Report(AgentWorkerState.Connected, $"Impreso {job.DocumentNumber}.", lastJob: _lastJob);
        }
        catch (Exception error)
        {
            _lastError = error.Message;
            await apiClient.FailJobAsync(job.Id, error.Message, cancellationToken);
            _logger.Log($"Error imprimiendo {job.DocumentNumber}: {error.Message}");
            Report(AgentWorkerState.PrinterUnreachable, $"Error imprimiendo {job.DocumentNumber}.", lastJob: _lastJob, lastError: _lastError);
        }
    }

    private void LogStartup(AgentSettings settings)
    {
        _logger.Log($"Backend: {settings.BackendBaseUrl}");
        _logger.Log($"Connector: {settings.PrinterConnectorType.DisplayName()}");
        if (settings.PrinterConnectorType == PrinterConnectorType.NetworkTcp)
        {
            _logger.Log($"Printer: {settings.PrinterHost}:{settings.PrinterPort}");
        }
        else
        {
            _logger.Log($"Printer queue: {settings.PrinterQueueName}");
        }
        _logger.Log($"UseJobPrinterTarget: {settings.UseJobPrinterTarget}");
        _logger.Log($"Log: {AgentPaths.ResolveAgentPath(_baseDirectory, settings.LogFilePath)}");
        if (settings.SavePayloads)
        {
            _logger.Log($"Payload dumps: {AgentPaths.ResolveAgentPath(_baseDirectory, settings.PayloadDumpDirectory)}");
        }
    }

    private void Report(AgentWorkerState state, string message, string? lastJob = null, string? lastError = null)
    {
        StatusChanged?.Invoke(this, new AgentStatusSnapshot(
            state,
            message,
            lastJob ?? _lastJob,
            lastError ?? _lastError,
            DateTimeOffset.Now));
    }
}
