using CommunityToolkit.Mvvm.ComponentModel;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.ViewModels;

/// <summary>What every page needs; recreated whenever the backend (re)starts.</summary>
public sealed record AppSession(IBackend Backend, Localizer Loc, IDialogService Dialogs);

/// <summary>A tab in the main window.</summary>
public abstract class PageViewModel(string id) : ObservableObject
{
    public string Id { get; } = id;

    public abstract string Title { get; }

    /// <summary>Called whenever the page becomes the selected tab.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}
