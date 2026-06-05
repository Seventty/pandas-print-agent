namespace Pandas.PrintAgent.Core.Printing;

public sealed record PrinterTarget(string Host, int Port);

public sealed record PrintResult(int Bytes, long ConnectMs, long WriteMs, long TotalMs);

public sealed record PrintJob(
    string Id,
    string DocumentType,
    string DocumentNumber,
    string PayloadBase64,
    string? TargetHost,
    int TargetPort,
    int Attempt,
    int MaxAttempts
);
