using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Pandas.PrintAgent.App.Services;

public enum UpdateCheckKind
{
    NotInstalled,
    NoUpdates,
    UpdateReady,
}

public sealed record UpdateCheckResult(UpdateCheckKind Kind, string Message, string? Version = null);

public sealed record UpdateServiceOptions(
    string GithubRepoUrl,
    bool IncludePrereleases,
    string Channel,
    bool AutoCheckOnStartup)
{
    public const string DefaultGithubRepoUrl = "https://github.com/Seventty/pandas-print-agent";
    public const string DefaultChannel = "stable";

    public static UpdateServiceOptions FromEnvironment()
    {
        return new UpdateServiceOptions(
            Clean(Environment.GetEnvironmentVariable("PANDAS_UPDATE_GITHUB_REPO_URL")) ?? DefaultGithubRepoUrl,
            BoolEnv("PANDAS_UPDATE_GITHUB_PRERELEASE", false),
            Clean(Environment.GetEnvironmentVariable("PANDAS_UPDATE_CHANNEL")) ?? DefaultChannel,
            BoolEnv("PANDAS_UPDATE_AUTO_CHECK", true));
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool BoolEnv(string name, bool fallback)
    {
        return bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    }
}

public sealed class UpdateService
{
    private readonly UpdateServiceOptions _options;
    private UpdateManager? _manager;
    private VelopackAsset? _pendingUpdate;

    public UpdateService(UpdateServiceOptions options)
    {
        _options = options;
    }

    public bool AutoCheckOnStartup => _options.AutoCheckOnStartup;

    public string SourceDescription =>
        $"{_options.GithubRepoUrl} / channel={_options.Channel} / prerelease={_options.IncludePrereleases}";

    public string CurrentVersionText
    {
        get
        {
            return GetManager().CurrentVersion?.ToString()
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                ?? "desarrollo";
        }
    }

    public async Task<UpdateCheckResult> CheckDownloadAndPrepareAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manager = GetManager();
        if (manager.CurrentVersion is null)
        {
            return new UpdateCheckResult(
                UpdateCheckKind.NotInstalled,
                $"Updates disponibles solo en instalaciones creadas con Velopack. Source: {SourceDescription}");
        }

        var preparedUpdate = manager.UpdatePendingRestart;
        if (preparedUpdate is not null)
        {
            _pendingUpdate = preparedUpdate;
            return new UpdateCheckResult(
                UpdateCheckKind.UpdateReady,
                $"Update {manager.CurrentVersion} -> {preparedUpdate.Version} listo para aplicar. Source: {SourceDescription}",
                preparedUpdate.Version.ToString());
        }

        var updateInfo = await manager.CheckForUpdatesAsync();
        if (updateInfo is null)
        {
            return new UpdateCheckResult(
                UpdateCheckKind.NoUpdates,
                $"No hay updates disponibles. Current={manager.CurrentVersion}. Source: {SourceDescription}");
        }

        progress?.Invoke(0);
        await manager.DownloadUpdatesAsync(updateInfo, progress, cancellationToken);
        _pendingUpdate = manager.UpdatePendingRestart ?? updateInfo.TargetFullRelease;

        return new UpdateCheckResult(
            UpdateCheckKind.UpdateReady,
            $"Update {manager.CurrentVersion} -> {updateInfo.TargetFullRelease.Version} descargado. Reinicia para aplicar. Source: {SourceDescription}",
            updateInfo.TargetFullRelease.Version.ToString());
    }

    public void ApplyPendingUpdateOnExit(bool silent = false)
    {
        var manager = GetManager();
        var pendingUpdate = manager.UpdatePendingRestart ?? _pendingUpdate;
        if (pendingUpdate is null)
        {
            throw new InvalidOperationException("No hay update descargado para aplicar.");
        }

        manager.WaitExitThenApplyUpdates(pendingUpdate, silent, true, Array.Empty<string>());
    }

    private UpdateManager GetManager()
    {
        return _manager ??= CreateManager();
    }

    private UpdateManager CreateManager()
    {
        var source = new GithubSource(
            _options.GithubRepoUrl,
            null,
            _options.IncludePrereleases,
            downloader: null);
        var options = new UpdateOptions
        {
            ExplicitChannel = _options.Channel,
        };

        return new UpdateManager(source, options);
    }
}
