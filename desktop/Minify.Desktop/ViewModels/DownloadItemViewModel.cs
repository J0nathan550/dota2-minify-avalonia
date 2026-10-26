using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

/// <summary>One backend download (onDownloadProgress), shown as a notification card.</summary>
public sealed partial class DownloadItemViewModel(string id, Localizer loc) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent), nameof(Details), nameof(HasTotal), nameof(IsDownloading), nameof(IsFinished), nameof(IsFailed))]
    public partial string Status { get; set; } = "downloading";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent), nameof(Details))]
    public partial long DownloadedBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent), nameof(Details), nameof(HasTotal))]
    public partial long TotalBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    public partial string? Error { get; set; }

    public bool IsDownloading => Status == "downloading";
    public bool IsFinished => Status == "finished";
    public bool IsFailed => Status == "error";
    public bool HasTotal => IsDownloading && TotalBytes > 0;
    public double Percent => TotalBytes > 0 ? Math.Min(100, DownloadedBytes * 100.0 / TotalBytes) : 0;

    public string Details => Status switch
    {
        "finished" => loc.Format("status_download_complete", Mb(DownloadedBytes)),
        "error" => loc.Format("status_download_failed", Error ?? "Unknown error"),
        _ when TotalBytes > 0 => $"{Mb(DownloadedBytes)} / {Mb(TotalBytes)} ({Math.Round(Percent)}%)",
        _ => Mb(DownloadedBytes),
    };

    public void Update(JsonElement data)
    {
        Name = data.GetProperty("name").GetString() ?? Id;
        DownloadedBytes = data.GetProperty("downloaded_bytes").GetInt64();
        TotalBytes = data.GetProperty("total_bytes").GetInt64();
        Error = data.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        Status = data.GetProperty("status").GetString() ?? "downloading";
    }

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.00} MB";
}
