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
        var text = Encoding.ASCII.GetString(payload);

        Assert.Equal(0x1b, payload[0]);
        Assert.Equal((byte)'@', payload[1]);
        Assert.True(ContainsSequence(payload, [0x1b, (byte)'t', 0x00]));
        Assert.True(ContainsSequence(payload, [0x1b, (byte)'a', 0x01]));
        Assert.True(ContainsSequence(payload, [0x1b, (byte)'E', 0x01]));
        Assert.Contains((byte)0xdb, payload);
        Assert.Contains((byte)0xdc, payload);
        Assert.Contains((byte)0xdf, payload);
        Assert.Contains("TEST POD", text);
        Assert.DoesNotContain("PANDAS TEST POS", text);
    }

    private static PrintJob Job(string targetHost, int targetPort)
    {
        return new PrintJob("job-1", "ORDER", "0001", Convert.ToBase64String([1, 2, 3]), targetHost, targetPort, 1, 5);
    }

    private static bool ContainsSequence(byte[] payload, ReadOnlySpan<byte> sequence)
    {
        for (var index = 0; index <= payload.Length - sequence.Length; index++)
        {
            if (payload.AsSpan(index, sequence.Length).SequenceEqual(sequence))
            {
                return true;
            }
        }

        return false;
    }
}
