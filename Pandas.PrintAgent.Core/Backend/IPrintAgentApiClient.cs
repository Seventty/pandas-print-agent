using Pandas.PrintAgent.Core.Printing;

namespace Pandas.PrintAgent.Core.Backend;

public interface IPrintAgentApiClient
{
    Task<PrintAgentBackendStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<PrintJob?> FetchNextJobAsync(CancellationToken cancellationToken);
    Task CompleteJobAsync(string jobId, CancellationToken cancellationToken);
    Task FailJobAsync(string jobId, string error, CancellationToken cancellationToken);
}
