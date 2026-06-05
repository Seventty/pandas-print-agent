using System.Text;
using Pandas.PrintAgent.Core.Printing;
using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Tests;

public sealed class PrinterServiceTests
{
    [Fact]
    public void TargetForJobUsesConfiguredPrinterByDefault()
    {
        var settings = new AgentSettings { PrinterHost = "10.0.0.28", PrinterPort = 9100, UseJobPrinterTarget = false };
        var job = Job("192.168.1.50", 9200);

        var target = PrinterService.TargetForJob(job, settings);

        Assert.Equal("10.0.0.28", target.Host);
        Assert.Equal(9100, target.Port);
    }

    [Fact]
    public void TargetForJobUsesJobPrinterWhenEnabled()
    {
        var settings = new AgentSettings { PrinterHost = "10.0.0.28", PrinterPort = 9100, UseJobPrinterTarget = true };
        var job = Job("192.168.1.50", 9200);

        var target = PrinterService.TargetForJob(job, settings);

        Assert.Equal("192.168.1.50", target.Host);
        Assert.Equal(9200, target.Port);
    }

    [Fact]
    public void TestPayloadKeepsEscPosHeader()
    {
        var payload = PrinterService.BuildTestPayload();

        Assert.Equal(0x1b, payload[0]);
        Assert.Equal((byte)'@', payload[1]);
        Assert.Contains("PALEDEN TEST POS", Encoding.ASCII.GetString(payload));
    }

    private static PrintJob Job(string targetHost, int targetPort)
    {
        return new PrintJob("job-1", "ORDER", "0001", Convert.ToBase64String([1, 2, 3]), targetHost, targetPort, 1, 5);
    }
}
