namespace Pandas.PrintAgent.Core.Backend;

public sealed record PrintAgentBackendStatus(bool Ok, string Mode, DateTimeOffset ServerTime);

public enum BackendStatusKind
{
    Connected,
    InvalidToken,
    BackendUnreachable,
}

public sealed record BackendStatusCheck(BackendStatusKind Kind, string Message, PrintAgentBackendStatus? Status = null);
