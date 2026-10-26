namespace Minify.Desktop.Services;

public static class Http
{
    /// <summary>One shared client for the app's own requests (announcements, updates).</summary>
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"dota2-minify-avalonia/{AppInfo.Version}");
        return client;
    }
}
