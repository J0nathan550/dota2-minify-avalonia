using System.Net.Http.Json;
using System.Text.Json;
using Minify.Desktop.Backend;

namespace Minify.Desktop.Services.Updates;

/// <summary>The parts of a GitHub release (REST API "release" object) the updater uses.</summary>
public sealed record Release(string TagName, string? Name, string? Body, string? HtmlUrl, List<ReleaseAsset> Assets);

/// <summary>A release download; <c>digest</c> is GitHub's "sha256:&lt;hex&gt;" checksum when available.</summary>
public sealed record ReleaseAsset(string Name, string BrowserDownloadUrl, long Size, string? Digest);

public interface IReleaseFeed
{
    Task<Release?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the latest release from GitHub, or from <c>MINIFY_UPDATE_FEED</c> (a URL or a local JSON file with the
/// same shape), which the release pipeline uses to test updates end to end.
/// </summary>
public sealed class GitHubReleaseFeed(HttpClient http, string source) : IReleaseFeed
{
    public const string FeedVariable = "MINIFY_UPDATE_FEED";
    public const string DefaultSource = "https://api.github.com/repos/J0nathan550/dota2-minify-avalonia/releases/latest";

    public static GitHubReleaseFeed FromEnvironment(HttpClient http, string? explicitSource = null)
    {
        var source = string.IsNullOrEmpty(explicitSource) ? Environment.GetEnvironmentVariable(FeedVariable) : explicitSource;
        return new GitHubReleaseFeed(http, string.IsNullOrEmpty(source) ? DefaultSource : source);
    }

    public string Source { get; } = source;

    public async Task<Release?> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(Source))
        {
            await using var file = File.OpenRead(Source);
            return await JsonSerializer.DeserializeAsync<Release>(file, BackendJson.Options, cancellationToken);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, Source);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken);
        // No release published yet.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Release>(BackendJson.Options, cancellationToken);
    }
}
