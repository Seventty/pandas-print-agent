namespace Pandas.PrintAgent.Core;

public static class AgentPaths
{
    public static string ResolveAgentPath(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);
    }
}
