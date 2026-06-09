using Avalonia;
using System;
using System.Threading;
using Velopack;

namespace Pandas.PrintAgent.App;

sealed class Program
{
    private const string SingleInstanceMutexName = "com.pandas.printagent.singleinstance";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, SingleInstanceMutexName, out var ownsInstance);
        if (!ownsInstance)
        {
            return;
        }

        VelopackApp.Build().Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        GC.KeepAlive(singleInstance);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
