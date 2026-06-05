namespace Pandas.PrintAgent.Core.Logging;

public interface IAgentLogger
{
    event EventHandler<string>? LineWritten;
    void Log(string message);
}
