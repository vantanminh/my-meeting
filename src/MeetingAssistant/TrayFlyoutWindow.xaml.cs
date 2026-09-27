using System.Windows;
using System.Windows.Input;
using MeetingAssistant.Services;
using MeetingAssistant.ViewModels;

namespace MeetingAssistant;

public partial class TrayFlyoutWindow : Window
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(350);
    private const double ScreenGap = 4;

    private readonly Action _openApp;
    private readonly Action _exit;
    private DateTime _hiddenAt = DateTime.MinValue;

    public TrayFlyoutWindow(MainViewModel viewModel, Action openApp, Action exit)
    {
        InitializeComponent();
        DataContext = viewModel;
        _openApp = openApp;
        _exit = exit;
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        // Clicking the tray icon deactivates the open flyout first; without this guard the same click reopens it.
        if (DateTime.UtcNow - _hiddenAt < ReopenGuard)
            return;

        ShowNearTray();
    }

    private void ShowNearTray()
    {
        LocalizationService.Refresh();
        Opacity = 0;
        Show();
        UpdateLayout();

        var workArea = SystemParameters.WorkArea;
        var taskbarOnTop = workArea.Top > 0;
        var taskbarOnLeft = workArea.Left > 0;
        Left = taskbarOnLeft ? workArea.Left + ScreenGap : workArea.Right - ActualWidth - ScreenGap;
        Top = taskbarOnTop ? workArea.Top + ScreenGap : workArea.Bottom - ActualHeight - ScreenGap;

        Opacity = 1;
        Activate();
        Focus();
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => HideFlyout();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        HideFlyout();
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HideFlyout();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        _openApp();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        _exit();
    }
}
