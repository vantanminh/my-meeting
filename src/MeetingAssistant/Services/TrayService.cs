namespace MeetingAssistant.Services;

public sealed class TrayService : ITrayService
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private Action? _showWindow;
    private Action? _toggleRecording;

    public void Initialize(Action showWindow, Action toggleRecording)
    {
        _showWindow = showWindow;
        _toggleRecording = toggleRecording;
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Meeting Assistant"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Meeting Assistant", null, (_, _) => _showWindow?.Invoke());
        menu.Items.Add("Start / stop recording", null, (_, _) => _toggleRecording?.Invoke());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => _showWindow?.Invoke();
    }

    public void SetRecordingState(bool isRecording, TimeSpan elapsed)
    {
        if (_notifyIcon is null) return;
        _notifyIcon.Text = isRecording
            ? $"Recording · {elapsed.ToString(@"mm\:ss")}"
            : "Meeting Assistant";
    }

    public void Dispose()
    {
        if (_notifyIcon is null) return;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }
}
