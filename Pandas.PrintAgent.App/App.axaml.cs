using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Security;
using Pandas.PrintAgent.Core.Settings;
using Pandas.PrintAgent.Core.Worker;
using Pandas.PrintAgent.App.Services;
using Pandas.PrintAgent.App.ViewModels;
using Pandas.PrintAgent.App.Views;
using System;
using System.Linq;

namespace Pandas.PrintAgent.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var baseDirectory = AppContext.BaseDirectory;
            var tokenStore = new SecureTokenStore();
            var settingsService = new AgentSettingsService(baseDirectory, tokenStore);
            var backendStatus = new BackendStatusService();
            var printer = new PrinterService();
            var printerDiscovery = new InstalledPrinterDiscoveryService();
            var logger = new FileAgentLogger(baseDirectory, null, writeToConsole: false);
            var worker = new PrintAgentWorker(baseDirectory, printer, logger);
            var viewModel = new MainWindowViewModel(baseDirectory, settingsService, backendStatus, printer, printerDiscovery, logger, worker);
            var window = new MainWindow
            {
                DataContext = viewModel,
            };
            var notificationService = new PrintNotificationService(window);
            worker.StatusChanged += (_, snapshot) =>
            {
                if (snapshot.State == AgentWorkerState.Printing)
                {
                    notificationService.ShowPrintReceived(snapshot.LastJob);
                }
            };

            viewModel.RequestExit = () => desktop.Shutdown();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = window;
            ConfigureTray(window, viewModel);

            desktop.Startup += async (_, _) =>
            {
                window.Hide();
                await viewModel.InitializeAsync();
            };

            desktop.Exit += async (_, _) =>
            {
                await viewModel.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureTray(MainWindow window, MainWindowViewModel viewModel)
    {
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem { Header = "Abrir" };
        openItem.Click += (_, _) => ShowWindow(window);
        menu.Items.Add(openItem);

        var reloadItem = new NativeMenuItem { Header = "Reload" };
        reloadItem.Click += async (_, _) =>
        {
            if (viewModel.ReloadCommand.CanExecute(null))
            {
                await viewModel.ReloadCommand.ExecuteAsync(null);
            }
        };
        menu.Items.Add(reloadItem);

        var testPrinterItem = new NativeMenuItem { Header = viewModel.TestPrinterButtonText };
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.TestPrinterButtonText))
            {
                testPrinterItem.Header = viewModel.TestPrinterButtonText;
            }
        };
        testPrinterItem.Click += async (_, _) =>
        {
            if (viewModel.TestPrinterCommand.CanExecute(null))
            {
                await viewModel.TestPrinterCommand.ExecuteAsync(null);
            }
        };
        menu.Items.Add(testPrinterItem);
        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem { Header = "Salir" };
        exitItem.Click += async (_, _) =>
        {
            if (viewModel.ExitCommand.CanExecute(null))
            {
                await viewModel.ExitCommand.ExecuteAsync(null);
            }
        };
        menu.Items.Add(exitItem);

        var trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(),
            ToolTipText = "PANDAS Print Agent",
            IsVisible = true,
            Menu = menu,
        };
        trayIcon.Clicked += (_, _) => ShowWindow(window);
        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }

    private static void ShowWindow(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static WindowIcon LoadTrayIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Pandas.PrintAgent.App/Assets/pandas-logo.ico"));
        return new WindowIcon(stream);
    }
}
