using System.Diagnostics;

namespace Minify.Desktop.Services.Updates;

/// <summary>What the interface needs from the updater; faked in tests.</summary>
public interface IUpdateService
{
    Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default);

    /// <summary>Downloads and installs the update, then starts the new version; the app should close afterwards.</summary>
    Task InstallAsync(UpdateInfo update, IProgress<(long Done, long Total)> progress);
}

public sealed class UpdateService(Updater updater) : IUpdateService
{
    public Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellationToken = default) =>
        updater.CheckAsync(current, cancellationToken);

    public async Task InstallAsync(UpdateInfo update, IProgress<(long Done, long Total)> progress)
    {
        var download = await updater.DownloadAsync(update, progress);
        var next = await Task.Run(() => updater.Install(download, update, Environment.ProcessId));
        Process.Start(next)?.Dispose();
    }
}
