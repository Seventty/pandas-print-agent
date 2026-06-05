using Pandas.PrintAgent.Core.Settings;

namespace Pandas.PrintAgent.Core.Printing;

public interface IPrinterService
{
    Task<PrintResult> SendPayloadAsync(byte[] payload, PrinterTarget target, AgentSettings settings, CancellationToken cancellationToken);
}
