namespace Pandas.PrintAgent.Core.Worker;

public enum AgentWorkerState
{
    Stopped,
    Starting,
    Idle,
    Connected,
    Printing,
    InvalidToken,
    BackendUnreachable,
    PrinterUnreachable,
    Error,
}

public sealed record AgentStatusSnapshot(
    AgentWorkerState State,
    string Message,
    string? LastJob,
    string? LastError,
    DateTimeOffset UpdatedAt
);
