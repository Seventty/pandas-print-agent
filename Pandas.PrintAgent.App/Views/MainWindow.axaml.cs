using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Pandas.PrintAgent.App.ViewModels;

namespace Pandas.PrintAgent.App.Views;

public partial class MainWindow : Window
{
    private const double PreferredWidth = 760;
    private const double PreferredHeight = 780;
    private const double MinimumUsableHeight = 560;
    private const double ScreenMargin = 48;

    public MainWindow()
    {
        InitializeComponent();
        CanResize = false;
        Width = PreferredWidth;
        MinWidth = PreferredWidth;
        MaxWidth = PreferredWidth;
        ApplyAdaptiveHeight();

        Opened += (_, _) => ApplyAdaptiveHeight();
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

    private void ApplyAdaptiveHeight()
    {
        var targetHeight = PreferredHeight;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is not null)
        {
            var availableHeight = Math.Floor(screen.WorkingArea.Height / screen.Scaling - ScreenMargin);
            if (availableHeight > 0)
            {
                targetHeight = availableHeight >= MinimumUsableHeight
                    ? Math.Min(PreferredHeight, availableHeight)
                    : availableHeight;
            }
        }

        MinHeight = Math.Min(MinimumUsableHeight, targetHeight);
        Height = targetHeight;
        MaxHeight = targetHeight;
    }
}
