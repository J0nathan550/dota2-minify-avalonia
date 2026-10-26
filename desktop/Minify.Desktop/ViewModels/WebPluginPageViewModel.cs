namespace Minify.Desktop.ViewModels;

/// <summary>A third-party plugin whose interface is an HTML page; the native app has no web view to show it.</summary>
public sealed class WebPluginPageViewModel(AppSession session, string id, string name) : PageViewModel(id)
{
    public override string Title => name.StartsWith('&') ? session.Loc[name[1..]] : name;

    public string Message =>
        $"\"{Title}\" has a web-based interface, which this version of Minify can't display. " +
        "Its settings are still available in the Settings tab.";
}
