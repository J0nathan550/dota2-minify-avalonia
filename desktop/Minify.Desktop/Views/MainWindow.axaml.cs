using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.Logs.CollectionChanged += ScrollLogToEnd;
        };
    }

    private void ScrollLogToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || LogList.ItemCount == 0)
            return;
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(LogList.ItemCount - 1), DispatcherPriority.Background);
    }
}
