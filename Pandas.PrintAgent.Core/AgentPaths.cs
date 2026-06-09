namespace Pandas.PrintAgent.Core;

public static class AgentPaths
{
    public const string DataDirectoryEnvironmentVariable = "PANDAS_PRINT_AGENT_DATA_DIR";

    public static string GetDefaultDataDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath.Trim();
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(FirstNonEmpty(localAppData, UserHomeDirectory()), "PANDAS", "PrintAgent");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(UserHomeDirectory(), "Library", "Application Support", "PANDAS", "PrintAgent");
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = FirstNonEmpty(xdgConfigHome, Path.Combine(UserHomeDirectory(), ".config"));
        return Path.Combine(configRoot, "pandas-print-agent");
    }

    public static string ResolveAgentPath(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
    }

    private static string UserHomeDirectory()
    {
        return FirstNonEmpty(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetEnvironmentVariable("USERPROFILE"),
            AppContext.BaseDirectory);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return AppContext.BaseDirectory;
    }
}
