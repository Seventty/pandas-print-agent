namespace Pandas.PrintAgent.Core.Logging;

public sealed class NullAgentLogger : IAgentLogger
{
    public event EventHandler<string>? LineWritten;

    public void Log(string message)
    {
        LineWritten?.Invoke(this, message);
    }
}
