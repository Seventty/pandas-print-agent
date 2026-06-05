using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Pandas.PrintAgent.App.Views;

namespace Pandas.PrintAgent.App.Services;

public sealed class PrintNotificationService
{
    private readonly Window _owner;
    private PrintNotificationWindow? _currentWindow;

    public PrintNotificationService(Window owner)
    {
        _owner = owner;
    }

    public void ShowPrintReceived(string? jobDescription)
    {
        var message = string.IsNullOrWhiteSpace(jobDescription)
            ? "El ticket se va a imprimir en el POS."
            : $"{jobDescription} se va a imprimir en el POS.";

        Dispatcher.UIThread.Post(async () =>
        {
            _currentWindow?.Close();

            var notification = new PrintNotificationWindow("Impresion recibida", message);
            PositionWindow(notification);
            notification.Show();
            _currentWindow = notification;

            await Task.Delay(TimeSpan.FromSeconds(5));
            if (_currentWindow == notification)
            {
                notification.Close();
                _currentWindow = null;
            }
        });
    }

    private void PositionWindow(Window notification)
    {
        var screen = _owner.Screens.ScreenFromWindow(_owner) ?? _owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        const int width = 360;
        const int height = 118;
        const int margin = 18;
        notification.Position = new PixelPoint(
            area.X + area.Width - width - margin,
            area.Y + area.Height - height - margin);
    }
}
