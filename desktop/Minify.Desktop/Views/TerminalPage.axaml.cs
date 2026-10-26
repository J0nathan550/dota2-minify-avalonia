using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class TerminalPage : UserControl
{
    private TerminalPageViewModel? _viewModel;

    public TerminalPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
                _viewModel.Logs.CollectionChanged -= ScrollToEnd;
            _viewModel = DataContext as TerminalPageViewModel;
            if (_viewModel is not null)
            {
                _viewModel.Logs.CollectionChanged += ScrollToEnd;
                ScrollToEnd(null, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        };
    }

    private void ScrollToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is not { AutoScroll: true } || e.Action is NotifyCollectionChangedAction.Remove)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (LogList.ItemCount > 0)
                LogList.ScrollIntoView(LogList.ItemCount - 1);
        }, DispatcherPriority.Background);
    }
}
