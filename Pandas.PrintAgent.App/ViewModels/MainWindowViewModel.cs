using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pandas.PrintAgent.Core;
using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Security;
using Pandas.PrintAgent.Core.Settings;
using Pandas.PrintAgent.Core.Worker;

namespace Pandas.PrintAgent.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly string _baseDirectory;
    private readonly AgentSettingsService? _settingsService;
    private readonly ITokenStore? _tokenStore;
    private readonly BackendStatusService? _backendStatus;
    private readonly IPrinterService? _printer;
    private readonly FileAgentLogger? _logger;
    private readonly PrintAgentWorker? _worker;

    [ObservableProperty]
    private string backendBaseUrl = "https://tu-backend-paleden.example.com";

    [ObservableProperty]
    private string apiPrefix = "api";

    [ObservableProperty]
    private string agentToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenPasswordChar))]
    [NotifyPropertyChangedFor(nameof(TokenVisibilityLabel))]
    private bool isTokenVisible;

    public char TokenPasswordChar => IsTokenVisible ? '\0' : '*';

    public string TokenVisibilityLabel => IsTokenVisible ? "Ocultar token" : "Mostrar token";

    [ObservableProperty]
    private int pollIntervalMs = 2000;

    [ObservableProperty]
    private string printerHost = "10.0.0.28";

    [ObservableProperty]
    private int printerPort = 9100;

    [ObservableProperty]
    private int printerTimeoutMs = 5000;

    [ObservableProperty]
    private bool useJobPrinterTarget;

    [ObservableProperty]
    private string logFilePath = "logs/print-agent.log";

    [ObservableProperty]
    private bool savePayloads;

    [ObservableProperty]
    private string payloadDumpDirectory = "logs/payloads";

    [ObservableProperty]
    private string statusText = "Cargando...";

    [ObservableProperty]
    private string statusForeground = "#536170";

    [ObservableProperty]
    private string tokenStorageStatus = "Sin verificar";

    [ObservableProperty]
    private string lastJob = "Ninguno";

    [ObservableProperty]
    private string lastError = "Ninguno";

    [ObservableProperty]
    private string lastLogLine = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isWorkerRunning;

    public MainWindowViewModel()
    {
        _baseDirectory = AppContext.BaseDirectory;
        ApplySettings(new AgentSettings());
        StatusText = "Vista previa";
    }

    public MainWindowViewModel(
        string baseDirectory,
        AgentSettingsService settingsService,
        ITokenStore tokenStore,
        BackendStatusService backendStatus,
        IPrinterService printer,
        FileAgentLogger logger,
        PrintAgentWorker worker)
    {
        _baseDirectory = baseDirectory;
        _settingsService = settingsService;
        _tokenStore = tokenStore;
        _backendStatus = backendStatus;
        _printer = printer;
        _logger = logger;
        _worker = worker;

        _logger.LineWritten += (_, line) =>
        {
            Dispatcher.UIThread.Post(() => LastLogLine = line);
        };
        _worker.StatusChanged += (_, snapshot) =>
        {
            Dispatcher.UIThread.Post(() => ApplyStatus(snapshot));
        };
    }

    public bool IsExitRequested { get; private set; }

    public Action? RequestExit { get; set; }

    public async Task InitializeAsync()
    {
        if (_settingsService is null || _tokenStore is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var availability = await _tokenStore.CheckAvailabilityAsync();
            TokenStorageStatus = availability.Message;

            var settings = await _settingsService.LoadAsync();
            ApplySettings(settings);
            await ReloadWorkerAsync(settings);
            await CheckBackendAsync(settings);
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = BuildSettings();
            RequireRuntimeServices();
            await _settingsService!.SaveAsync(settings, AgentToken);
            _logger!.UpdateLogFilePath(settings.LogFilePath);
            SetStatus("Configuracion guardada.", true);
            TokenStorageStatus = "Token guardado en almacenamiento seguro.";
        });
    }

    [RelayCommand]
    private void ToggleTokenVisibility()
    {
        IsTokenVisible = !IsTokenVisible;
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = BuildSettings();
            await ReloadWorkerAsync(settings);
            await CheckBackendAsync(settings);
        });
    }

    [RelayCommand]
    private async Task CheckBackendAsync()
    {
        await RunBusyAsync(async () => await CheckBackendAsync(BuildSettings()));
    }

    [RelayCommand]
    private async Task TestPrinterAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = BuildSettings();
            RequireRuntimeServices();
            _logger!.UpdateLogFilePath(settings.LogFilePath);
            await PrintAgentDiagnostics.RunTestPrintAsync(settings, _printer!, _logger, CancellationToken.None);
            SetStatus("Prueba enviada a la impresora.", true);
            LastError = "Ninguno";
        });
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            var path = AgentPaths.ResolveAgentPath(_baseDirectory, LogFilePath);
            var directory = Path.GetDirectoryName(path) ?? _baseDirectory;
            Directory.CreateDirectory(directory);
            var target = File.Exists(path) ? path : directory;
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            LastError = error.Message;
            SetStatus("No se pudieron abrir los logs.", false);
        }
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        IsExitRequested = true;
        if (_worker is not null)
        {
            await _worker.StopAsync();
        }
        RequestExit?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }
    }

    private async Task ReloadWorkerAsync(AgentSettings settings)
    {
        RequireRuntimeServices();
        _logger!.UpdateLogFilePath(settings.LogFilePath);
        await _worker!.ReloadAsync(settings);
        IsWorkerRunning = _worker.IsRunning;
        SetStatus("Agente ejecutandose en segundo plano.", true);
    }

    private async Task CheckBackendAsync(AgentSettings settings)
    {
        RequireRuntimeServices();
        var result = await _backendStatus!.CheckAsync(settings);
        var connected = result.Kind == BackendStatusKind.Connected;
        var message = result.Kind switch
        {
            BackendStatusKind.Connected => $"Conectado al backend ({result.Status?.Mode ?? "queue"}).",
            BackendStatusKind.InvalidToken => "Token invalido.",
            _ => result.Message,
        };
        SetStatus(message, connected);
        LastError = result.Kind == BackendStatusKind.Connected ? "Ninguno" : result.Message;
    }

    private AgentSettings BuildSettings()
    {
        var settings = new AgentSettings
        {
            BackendBaseUrl = BackendBaseUrl,
            ApiPrefix = ApiPrefix,
            AgentToken = AgentToken,
            PollIntervalMs = PollIntervalMs,
            PrinterHost = PrinterHost,
            PrinterPort = PrinterPort,
            PrinterTimeoutMs = PrinterTimeoutMs,
            UseJobPrinterTarget = UseJobPrinterTarget,
            LogFilePath = LogFilePath,
            SavePayloads = SavePayloads,
            PayloadDumpDirectory = PayloadDumpDirectory,
        };
        settings.Validate();
        return settings;
    }

    private void ApplySettings(AgentSettings settings)
    {
        BackendBaseUrl = settings.BackendBaseUrl;
        ApiPrefix = settings.ApiPrefix;
        AgentToken = settings.AgentToken;
        PollIntervalMs = settings.PollIntervalMs;
        PrinterHost = settings.PrinterHost;
        PrinterPort = settings.PrinterPort;
        PrinterTimeoutMs = settings.PrinterTimeoutMs;
        UseJobPrinterTarget = settings.UseJobPrinterTarget;
        LogFilePath = settings.LogFilePath;
        SavePayloads = settings.SavePayloads;
        PayloadDumpDirectory = settings.PayloadDumpDirectory;
    }

    private void ApplyStatus(AgentStatusSnapshot snapshot)
    {
        SetStatus(snapshot.Message, snapshot.State is AgentWorkerState.Idle or AgentWorkerState.Connected or AgentWorkerState.Printing);
        LastJob = string.IsNullOrWhiteSpace(snapshot.LastJob) ? "Ninguno" : snapshot.LastJob;
        LastError = string.IsNullOrWhiteSpace(snapshot.LastError) ? "Ninguno" : snapshot.LastError;
        IsWorkerRunning = _worker?.IsRunning ?? false;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception error)
        {
            LastError = error.Message;
            SetStatus(error.Message, false);
        }
        finally
        {
            IsBusy = false;
            IsWorkerRunning = _worker?.IsRunning ?? IsWorkerRunning;
        }
    }

    private void RequireRuntimeServices()
    {
        if (_settingsService is null || _backendStatus is null || _printer is null || _logger is null || _worker is null)
        {
            throw new InvalidOperationException("La aplicacion todavia no esta inicializada.");
        }
    }

    private void SetStatus(string message, bool isPositive)
    {
        StatusText = message;
        StatusForeground = isPositive ? "#1F6F43" : "#B42318";
    }
}
