using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Pandas.PrintAgent.App.ViewModels;

namespace Pandas.PrintAgent.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        CanResize = false;
        Width = 760;
        Height = 820;
        MinWidth = 760;
        MinHeight = 820;
        MaxWidth = 760;
        MaxHeight = 820;
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty && WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel { IsExitRequested: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
