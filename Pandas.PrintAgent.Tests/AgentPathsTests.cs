using Pandas.PrintAgent.Core;

namespace Pandas.PrintAgent.Tests;

public sealed class AgentPathsTests
{
    [Fact]
    public void ResolveAgentPathKeepsRootedPaths()
    {
        var rootedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pandas-print-agent.log"));

        var resolved = AgentPaths.ResolveAgentPath("ignored", rootedPath);

        Assert.Equal(rootedPath, resolved);
    }

    [Fact]
    public void ResolveAgentPathCombinesRelativePathsWithBaseDirectory()
    {
        var resolved = AgentPaths.ResolveAgentPath("base", "logs/print-agent.log");

        Assert.Equal(Path.Combine("base", "logs/print-agent.log"), resolved);
    }

    [Fact]
    public void DefaultDataDirectoryUsesEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable(AgentPaths.DataDirectoryEnvironmentVariable);
        using var temp = new TempDirectory();

        try
        {
            Environment.SetEnvironmentVariable(AgentPaths.DataDirectoryEnvironmentVariable, temp.Path);

            Assert.Equal(temp.Path, AgentPaths.GetDefaultDataDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable(AgentPaths.DataDirectoryEnvironmentVariable, previous);
        }
    }
}
