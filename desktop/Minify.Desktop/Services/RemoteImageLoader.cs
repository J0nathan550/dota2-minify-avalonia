using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Media.Imaging;

namespace Minify.Desktop.Services;

public interface IImageLoader
{
    /// <summary>Downloads and decodes an image; null when it can't be loaded.</summary>
    Task<Bitmap?> LoadAsync(string url, int decodeWidth);
}

/// <summary>Loads preview images over HTTP, decoding each URL once per size.</summary>
public sealed class RemoteImageLoader(HttpClient http) : IImageLoader
{
    private readonly ConcurrentDictionary<(string, int), Task<Bitmap?>> _cache = new();
    private readonly SemaphoreSlim _parallel = new(6);

    public Task<Bitmap?> LoadAsync(string url, int decodeWidth) =>
        _cache.GetOrAdd((url, decodeWidth), key => FetchAsync(key.Item1, key.Item2));

    private async Task<Bitmap?> FetchAsync(string url, int decodeWidth)
    {
        await _parallel.WaitAsync();
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Image {url} failed: {ex.Message}");
            return null;
        }
        finally
        {
            _parallel.Release();
        }
    }
}
