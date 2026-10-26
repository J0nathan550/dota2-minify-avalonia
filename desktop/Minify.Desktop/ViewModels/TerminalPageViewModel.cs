using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

public sealed record LogLine(string Text, string Type)
{
    public bool IsSeparator => Type == "separator";
    public bool IsError => Type == "error";
    public bool IsWarning => Type == "warning";
    public bool IsSuccess => Type == "success";
}

public sealed partial class TerminalPageViewModel(AppSession session) : PageViewModel("terminal")
{
    private const int MaxLines = 5000;

    public override string Title => session.Loc["tab_terminal"];

    public Localizer Loc => session.Loc;

    public ObservableCollection<LogLine> Logs { get; } = [];

    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    public void Add(LogEntry entry)
    {
        if (entry.Type == "clear")
        {
            Logs.Clear();
            return;
        }
        Logs.Add(new LogLine(AnsiEscape().Replace(entry.Text, ""), entry.Type));
        while (Logs.Count > MaxLines)
            Logs.RemoveAt(0);
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        await session.Backend.CallAsync("clear_logs");
        Logs.Clear();
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiEscape();
}
