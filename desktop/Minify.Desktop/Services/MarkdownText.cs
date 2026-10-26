using System.Text.RegularExpressions;

namespace Minify.Desktop.Services;

public static partial class MarkdownText
{
    private static readonly Dictionary<string, string> AlertTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = "Note",
        ["TIP"] = "Tip",
        ["IMPORTANT"] = "Important",
        ["WARNING"] = "Warning",
        ["CAUTION"] = "Caution",
    };

    /// <summary>
    /// GitHub alerts (<c>&gt; [!WARNING]</c>) aren't standard Markdown; turn the marker into a bold
    /// heading inside the quote so the renderer shows something sensible.
    /// </summary>
    public static string ConvertAlerts(string markdown) =>
        AlertMarker().Replace(markdown, m =>
            AlertTitles.TryGetValue(m.Groups["kind"].Value, out var title)
                ? $"{m.Groups["quote"].Value}**{title}**"
                : m.Value);

    [GeneratedRegex(@"^(?<quote>\s*>\s*)\[!(?<kind>[A-Za-z]+)\][ \t]*$", RegexOptions.Multiline)]
    private static partial Regex AlertMarker();
}
