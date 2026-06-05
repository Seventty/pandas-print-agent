using Avalonia.Controls;

namespace Pandas.PrintAgent.App.Views;

public partial class PrintNotificationWindow : Window
{
    public PrintNotificationWindow()
    {
        InitializeComponent();
    }

    public PrintNotificationWindow(string title, string message)
        : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
    }
}
