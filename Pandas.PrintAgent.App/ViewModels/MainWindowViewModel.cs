using System.Diagnostics;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pandas.PrintAgent.App.Services;
using Pandas.PrintAgent.Core;
using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Settings;
using Pandas.PrintAgent.Core.Worker;

namespace Pandas.PrintAgent.App.ViewModels;

public sealed record PrinterConnectorOption(PrinterConnectorType Value, string Label)
{
    public static IReadOnlyList<PrinterConnectorOption> All { get; } =
    [
        new PrinterConnectorOption(PrinterConnectorType.NetworkTcp, PrinterConnectorType.NetworkTcp.DisplayName()),
        new PrinterConnectorOption(PrinterConnectorType.Usb, PrinterConnectorType.Usb.DisplayName()),
        new PrinterConnectorOption(PrinterConnectorType.Bluetooth, PrinterConnectorType.Bluetooth.DisplayName()),
    ];

    public static PrinterConnectorOption Find(PrinterConnectorType value)
    {
        return All.First(option => option.Value == value);
    }
}

public sealed record InstalledPrinterOption(string Name, string Label, InstalledPrinterConnectorHint ConnectorHint, bool IsDetected);

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private const string GitHubUrl = "https://github.com/seventty";
    private const string LinkedInUrl = "https://www.linkedin.com/in/rainieryvalerio";

    private readonly string _baseDirectory;
    private readonly AgentSettingsService? _settingsService;
    private readonly BackendStatusService? _backendStatus;
    private readonly IPrinterService? _printer;
    private readonly IInstalledPrinterDiscoveryService? _printerDiscovery;
    private readonly FileAgentLogger? _logger;
    private readonly PrintAgentWorker? _worker;
    private readonly UpdateService? _updateService;
    private IReadOnlyList<InstalledPrinterInfo> installedPrinters = [];

    [ObservableProperty]
    private string backendBaseUrl = "https://backend.example.com";

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

    public IReadOnlyList<PrinterConnectorOption> ConnectorOptions { get; } = PrinterConnectorOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNetworkConnector))]
    [NotifyPropertyChangedFor(nameof(IsInstalledPrinterConnector))]
    [NotifyPropertyChangedFor(nameof(PrinterQueueLabel))]
    [NotifyPropertyChangedFor(nameof(TestPrinterButtonText))]
    private PrinterConnectorOption selectedConnectorOption = PrinterConnectorOption.Find(PrinterConnectorType.NetworkTcp);

    public bool IsNetworkConnector => SelectedConnectorOption.Value == PrinterConnectorType.NetworkTcp;

    public bool IsInstalledPrinterConnector => !IsNetworkConnector;

    public string PrinterQueueLabel => SelectedConnectorOption.Value switch
    {
        PrinterConnectorType.Usb => "Impresora USB",
        PrinterConnectorType.Bluetooth => "Impresora Bluetooth",
        _ => "Impresora instalada",
    };

    public string TestPrinterButtonText => $"Probar {SelectedConnectorOption.Value.TestButtonName()}";

    public ObservableCollection<InstalledPrinterOption> InstalledPrinterOptions { get; } = [];

    [ObservableProperty]
    private InstalledPrinterOption? selectedInstalledPrinterOption;

    [ObservableProperty]
    private string installedPrinterListStatusText = "Conecta o instala la impresora y presiona Refrescar.";

    [ObservableProperty]
    private string printerHost = "10.0.0.28";

    [ObservableProperty]
    private int printerPort = 9100;

    [ObservableProperty]
    private string printerQueueName = string.Empty;

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
    private string printerStatusText = "Sin prueba de impresora.";

    [ObservableProperty]
    private string printerStatusForeground = "#536170";

    [ObservableProperty]
    private string lastJob = "Ninguno";

    [ObservableProperty]
    private string lastLogLine = string.Empty;

    [ObservableProperty]
    private bool isContactModalOpen;

    public string CopyrightText => $"© {DateTime.Now.Year} Rainiery Valerio Gonzalez";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartForUpdate))]
    [NotifyCanExecuteChangedFor(nameof(RestartForUpdateCommand))]
    private bool isBusy;

    [ObservableProperty]
    private bool isWorkerRunning;

    [ObservableProperty]
    private string appVersionText = "desarrollo";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartForUpdate))]
    [NotifyCanExecuteChangedFor(nameof(RestartForUpdateCommand))]
    private bool isUpdateReady;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartForUpdate))]
    [NotifyCanExecuteChangedFor(nameof(RestartForUpdateCommand))]
    private bool isWorkerPrinting;

    public bool CanRestartForUpdate => IsUpdateReady && !IsWorkerPrinting && !IsBusy;

    public MainWindowViewModel()
    {
        _baseDirectory = AppContext.BaseDirectory;
        ApplySettings(new AgentSettings());
        StatusText = "Vista previa";
    }

    public MainWindowViewModel(
        string baseDirectory,
        AgentSettingsService settingsService,
        BackendStatusService backendStatus,
        IPrinterService printer,
        IInstalledPrinterDiscoveryService printerDiscovery,
        FileAgentLogger logger,
        PrintAgentWorker worker,
        UpdateService updateService)
    {
        _baseDirectory = baseDirectory;
        _settingsService = settingsService;
        _backendStatus = backendStatus;
        _printer = printer;
        _printerDiscovery = printerDiscovery;
        _logger = logger;
        _worker = worker;
        _updateService = updateService;
        AppVersionText = FormatVersionText(updateService.CurrentVersionText);

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
        if (_settingsService is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var settings = await _settingsService.LoadAsync();
            ApplySettings(settings);
            await RefreshInstalledPrintersAsync(settings.PrinterQueueName);
            await ReloadWorkerAsync(settings);
            await CheckBackendAsync(settings);
        });
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (_updateService is null || !_updateService.AutoCheckOnStartup)
        {
            return;
        }

        await CheckForUpdatesAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = BuildSettings();
            RequireRuntimeServices();
            await _settingsService!.SaveAsync(settings, AgentToken);
            await ReloadWorkerAsync(settings);
            await CheckBackendAsync(settings);
        });
    }

    [RelayCommand]
    private void ToggleTokenVisibility()
    {
        IsTokenVisible = !IsTokenVisible;
    }

    [RelayCommand]
    private async Task RefreshInstalledPrintersAsync()
    {
        await RunBusyAsync(async () => await RefreshInstalledPrintersAsync(PrinterQueueName));
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

    private async Task CheckForUpdatesAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (_updateService is null)
            {
                return;
            }

            try
            {
                IsUpdateReady = false;
                var result = await _updateService.CheckDownloadAndPrepareAsync();
                ApplyUpdateResult(result);
                _logger?.Log(result.Message);
            }
            catch (Exception error)
            {
                IsUpdateReady = false;
                _logger?.Log($"Update fallo: {error.Message}");
            }
        });
    }

    [RelayCommand]
    private async Task TestPrinterAsync()
    {
        await RunBusyAsync(async () =>
        {
            var settings = BuildSettings();
            var connectorName = settings.PrinterConnectorType.TestButtonName();
            RequireRuntimeServices();
            _logger!.UpdateLogFilePath(settings.LogFilePath);
            try
            {
                await PrintAgentDiagnostics.RunTestPrintAsync(settings, _printer!, _logger, CancellationToken.None);
                SetPrinterStatus($"Prueba {connectorName} enviada correctamente.", true);
                SetStatus($"Prueba {connectorName} enviada a la impresora.", true);
            }
            catch (Exception error)
            {
                SetPrinterStatus($"Prueba {connectorName} fallo: {error.Message}", false);
                throw;
            }
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
        catch
        {
            SetStatus("No se pudieron abrir los logs.", false);
        }
    }

    [RelayCommand]
    private void OpenContact()
    {
        IsContactModalOpen = true;
    }

    [RelayCommand]
    private void CloseContact()
    {
        IsContactModalOpen = false;
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        OpenExternalUrl(GitHubUrl);
    }

    [RelayCommand]
    private void OpenLinkedIn()
    {
        OpenExternalUrl(LinkedInUrl);
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

    [RelayCommand(CanExecute = nameof(CanRestartForUpdate))]
    private async Task RestartForUpdateAsync()
    {
        await RunBusyAsync(async () =>
        {
            if (_updateService is null || !IsUpdateReady)
            {
                return;
            }

            if (IsWorkerPrinting)
            {
                return;
            }

            if (_worker is not null)
            {
                await _worker.StopAsync();
            }

            _updateService.ApplyPendingUpdateOnExit();
            IsExitRequested = true;
            RequestExit?.Invoke();
        });
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
    }

    private AgentSettings BuildSettings()
    {
        var settings = new AgentSettings
        {
            BackendBaseUrl = BackendBaseUrl,
            ApiPrefix = ApiPrefix,
            AgentToken = AgentToken,
            PollIntervalMs = PollIntervalMs,
            PrinterConnectorType = SelectedConnectorOption.Value,
            PrinterHost = PrinterHost,
            PrinterPort = PrinterPort,
            PrinterQueueName = PrinterQueueName,
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
        SelectedConnectorOption = PrinterConnectorOption.Find(settings.PrinterConnectorType);
        PrinterHost = settings.PrinterHost;
        PrinterPort = settings.PrinterPort;
        PrinterQueueName = settings.PrinterQueueName;
        PrinterTimeoutMs = settings.PrinterTimeoutMs;
        UseJobPrinterTarget = settings.UseJobPrinterTarget;
        LogFilePath = settings.LogFilePath;
        SavePayloads = settings.SavePayloads;
        PayloadDumpDirectory = settings.PayloadDumpDirectory;
    }

    partial void OnSelectedConnectorOptionChanged(PrinterConnectorOption value)
    {
        ApplyInstalledPrinterOptions(PrinterQueueName);
    }

    partial void OnSelectedInstalledPrinterOptionChanged(InstalledPrinterOption? value)
    {
        if (value is not null)
        {
            PrinterQueueName = value.Name;
        }
    }

    private void ApplyStatus(AgentStatusSnapshot snapshot)
    {
        SetStatus(snapshot.Message, snapshot.State is AgentWorkerState.Idle or AgentWorkerState.Connected or AgentWorkerState.Printing);
        LastJob = string.IsNullOrWhiteSpace(snapshot.LastJob) ? "Ninguno" : snapshot.LastJob;
        IsWorkerRunning = _worker?.IsRunning ?? false;
        IsWorkerPrinting = snapshot.State == AgentWorkerState.Printing;
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
        if (_settingsService is null || _backendStatus is null || _printer is null || _printerDiscovery is null || _logger is null || _worker is null)
        {
            throw new InvalidOperationException("La aplicacion todavia no esta inicializada.");
        }
    }

    private async Task RefreshInstalledPrintersAsync(string? preferredQueueName)
    {
        RequireRuntimeServices();
        try
        {
            installedPrinters = await _printerDiscovery!.GetInstalledPrintersAsync();
            ApplyInstalledPrinterOptions(preferredQueueName);
            InstalledPrinterListStatusText = installedPrinters.Count == 0
                ? "No se detectaron impresoras instaladas en el sistema."
                : $"{installedPrinters.Count} impresora(s) instalada(s) detectada(s).";
        }
        catch (Exception error)
        {
            installedPrinters = [];
            InstalledPrinterOptions.Clear();
            SelectedInstalledPrinterOption = null;
            InstalledPrinterListStatusText = $"No se pudieron listar impresoras: {error.Message}";
        }
    }

    private void ApplyInstalledPrinterOptions(string? preferredQueueName)
    {
        var selectedConnector = SelectedConnectorOption.Value;
        var currentQueueName = FirstNonEmpty(preferredQueueName, PrinterQueueName);
        var candidates = selectedConnector switch
        {
            PrinterConnectorType.Usb => installedPrinters
                .Where(printer => printer.ConnectorHint is InstalledPrinterConnectorHint.Usb or InstalledPrinterConnectorHint.Unknown),
            PrinterConnectorType.Bluetooth => installedPrinters
                .Where(printer => printer.ConnectorHint is InstalledPrinterConnectorHint.Bluetooth or InstalledPrinterConnectorHint.Unknown),
            _ => [],
        };
        var candidateList = candidates.ToList();
        if (selectedConnector != PrinterConnectorType.NetworkTcp && candidateList.Count == 0)
        {
            candidateList = installedPrinters.ToList();
        }

        var options = candidateList.Select(ToOption).ToList();
        if (!string.IsNullOrWhiteSpace(currentQueueName) &&
            !options.Any(option => string.Equals(option.Name, currentQueueName, StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, new InstalledPrinterOption(currentQueueName, $"{currentQueueName} (no detectada)", InstalledPrinterConnectorHint.Unknown, false));
        }

        InstalledPrinterOptions.Clear();
        foreach (var option in options)
        {
            InstalledPrinterOptions.Add(option);
        }

        SelectedInstalledPrinterOption = InstalledPrinterOptions.FirstOrDefault(option => string.Equals(option.Name, currentQueueName, StringComparison.OrdinalIgnoreCase))
            ?? InstalledPrinterOptions.FirstOrDefault();

        if (SelectedInstalledPrinterOption is not null && string.IsNullOrWhiteSpace(PrinterQueueName))
        {
            PrinterQueueName = SelectedInstalledPrinterOption.Name;
        }
    }

    private static InstalledPrinterOption ToOption(InstalledPrinterInfo printer)
    {
        var defaultSuffix = printer.IsDefault ? ", predeterminada" : string.Empty;
        var label = $"{printer.Name} ({printer.ConnectorHint.DisplayName()}{defaultSuffix})";
        return new InstalledPrinterOption(printer.Name, label, printer.ConnectorHint, true);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private void SetStatus(string message, bool isPositive)
    {
        StatusText = message;
        StatusForeground = isPositive ? "#1F6F43" : "#B42318";
    }

    private void SetPrinterStatus(string message, bool isPositive)
    {
        PrinterStatusText = message;
        PrinterStatusForeground = isPositive ? "#1F6F43" : "#B42318";
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            SetStatus("No se pudo abrir el enlace.", false);
        }
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        IsUpdateReady = result.Kind == UpdateCheckKind.UpdateReady;
        AppVersionText = FormatVersionText(_updateService?.CurrentVersionText ?? AppVersionText);
    }

    private static string FormatVersionText(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "desarrollo";
        }

        var trimmed = version.Trim();
        return trimmed.Equals("desarrollo", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith('v') ||
            trimmed.StartsWith('V')
            ? trimmed
            : $"v{trimmed}";
    }
}
