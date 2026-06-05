using Pandas.PrintAgent.Core.Backend;
using Pandas.PrintAgent.Core.Logging;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Settings;
using Pandas.PrintAgent.Core.Worker;

namespace Pandas.PrintAgent.Tests;

public sealed class PrintAgentWorkerTests
{
    [Fact]
    public async Task ReloadStopsPreviousLoopBeforeStartingReplacement()
    {
        using var temp = new TempDirectory();
        var first = new BlockingApiClient();
        var second = new BlockingApiClient();
        var clients = new Queue<BlockingApiClient>([first, second]);
        await using var worker = new PrintAgentWorker(
            temp.Path,
            new NoopPrinterService(),
            new NullAgentLogger(),
            _ => clients.Dequeue());

        await worker.StartAsync(ValidSettings());
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, worker.ActiveLoopCount);

        await worker.ReloadAsync(ValidSettings());
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(first.Disposed);
        Assert.Equal(1, worker.ActiveLoopCount);

        await worker.StopAsync();
        Assert.Equal(0, worker.ActiveLoopCount);
    }

    private static AgentSettings ValidSettings()
    {
        return new AgentSettings
        {
            BackendBaseUrl = "https://backend.example.com",
            AgentToken = "secret-token",
            PrinterHost = "10.0.0.28",
            PrinterPort = 9100,
            PollIntervalMs = 50,
            PrinterTimeoutMs = 50,
        };
    }
}
