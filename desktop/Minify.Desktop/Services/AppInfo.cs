using System.Reflection;

namespace Minify.Desktop.Services;

public static class AppInfo
{
    /// <summary>The app's 4-part version (major.minor.build.revision), set by the release build.</summary>
    public static Version Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

    public static string InformationalVersion { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? Version.ToString();

    public const string RepositoryUrl = "https://github.com/J0nathan550/dota2-minify-avalonia";
    public const string UpstreamUrl = "https://github.com/Egezenn/dota2-minify";
}
