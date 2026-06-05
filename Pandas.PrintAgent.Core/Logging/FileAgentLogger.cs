namespace Pandas.PrintAgent.Core.Logging;

public sealed class FileAgentLogger : IAgentLogger
{
    private readonly string _baseDirectory;
    private string? _logFilePath;
    private readonly bool _writeToConsole;

    public FileAgentLogger(string baseDirectory, string? logFilePath, bool writeToConsole = true)
    {
        _baseDirectory = baseDirectory;
        _logFilePath = logFilePath;
        _writeToConsole = writeToConsole;
    }

    public event EventHandler<string>? LineWritten;

    public void UpdateLogFilePath(string? logFilePath)
    {
        _logFilePath = logFilePath;
    }

    public void Log(string message)
    {
        var line = string.IsNullOrEmpty(message)
            ? string.Empty
            : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        if (_writeToConsole)
        {
            Console.WriteLine(line);
        }

        LineWritten?.Invoke(this, line);

        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        try
        {
            var path = AgentPaths.ResolveAgentPath(_baseDirectory, _logFilePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never stop printing.
        }
    }
}
