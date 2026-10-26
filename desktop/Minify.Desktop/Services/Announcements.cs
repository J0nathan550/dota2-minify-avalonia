using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Minify.Desktop.Backend;

namespace Minify.Desktop.Services;

/// <summary>An entry of docs/announcements.json.</summary>
public sealed record Announcement(
    string? Time,
    string? Text,
    string? Title,
    List<string>? Urls,
    string? Url,
    [property: JsonPropertyName("versions")] JsonElement Versions)
{
    /// <summary>The start timestamp of the time condition, used to remember dismissed announcements.</summary>
    public string Id => (Time ?? "").Replace("-", "").Replace("+", "").Split('=')[0].Trim();

    public IReadOnlyList<string> AllUrls => [.. Urls ?? [], .. Url is null ? Array.Empty<string>() : [Url]];
}

public interface IAnnouncementSource
{
    Task<IReadOnlyList<Announcement>> FetchAsync(CancellationToken cancellationToken);
}

public sealed class HttpAnnouncementSource(HttpClient http, string url) : IAnnouncementSource
{
    public const string DefaultUrl =
        "https://raw.githubusercontent.com/J0nathan550/dota2-minify-avalonia/main/docs/announcements.json";

    public async Task<IReadOnlyList<Announcement>> FetchAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<Announcement>>(url, BackendJson.Options, cancellationToken) ?? [];
}

public static class Announcements
{
    public const string SeenStateKey = "announcements_seen";

    /// <summary>
    /// Time conditions as in the web UI: "T-" until T, "T+" from T, "A=B" between A and B (unix seconds).
    /// Anything else is always shown.
    /// </summary>
    public static bool IsActive(string? time, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(time))
            return false;
        var seconds = now.ToUnixTimeSeconds();
        bool TryParse(string s, out long value) => long.TryParse(s.Trim(), out value);

        if (time.Contains('-'))
            return TryParse(time.Replace("-", ""), out var until) && seconds <= until;
        if (time.Contains('+'))
            return TryParse(time.Replace("+", ""), out var from) && seconds >= from;
        if (time.Contains('='))
        {
            var parts = time.Split('=');
            return parts.Length == 2 && TryParse(parts[0], out var start) && TryParse(parts[1], out var end) &&
                   seconds >= start && seconds <= end;
        }
        return true;
    }

    /// <summary>Versions are a list or a comma-separated string of prefixes; none means everyone.</summary>
    public static bool MatchesVersion(JsonElement versions, string currentVersion)
    {
        var allowed = versions.ValueKind switch
        {
            JsonValueKind.Array => versions.EnumerateArray().Select(v => v.ToString().Trim()).ToList(),
            JsonValueKind.String => (versions.GetString() ?? "").Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList(),
            _ => [],
        };
        return allowed.Count == 0 || allowed.Any(v => currentVersion == v || currentVersion.StartsWith(v, StringComparison.Ordinal));
    }

    public static List<Announcement> Pending(
        IEnumerable<Announcement> all, IReadOnlyCollection<string> seen, DateTimeOffset now, string currentVersion) =>
        all.Where(a => !string.IsNullOrEmpty(a.Text) && a.Id.Length > 0 && !seen.Contains(a.Id) &&
                       IsActive(a.Time, now) && MatchesVersion(a.Versions, currentVersion))
            .ToList();
}
