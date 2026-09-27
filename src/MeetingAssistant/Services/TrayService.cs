using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace MeetingAssistant.Services;

public sealed class TrayService : ITrayService
{
    private const int MaxTooltipLength = 63;

    private WinForms.NotifyIcon? _notifyIcon;
    private WinForms.ToolStripMenuItem? _statusItem;
    private WinForms.ToolStripMenuItem? _recordItem;
    private WinForms.ToolStripMenuItem? _openItem;
    private WinForms.ToolStripMenuItem? _exitItem;
    private TrayActions? _actions;
    private readonly Dictionary<TrayState, Icon> _icons = [];
    private TrayState _state = TrayState.Idle;

    public void Initialize(TrayActions actions)
    {
        _actions = actions;
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = IconFor(TrayState.Idle),
            Visible = true,
            Text = "Meeting Assistant"
        };

        var menu = new WinForms.ContextMenuStrip();
        _statusItem = new WinForms.ToolStripMenuItem("Meeting Assistant") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _openItem = new WinForms.ToolStripMenuItem(LocalizationService.Translate("Open Meeting Assistant"), null, (_, _) => _actions?.ShowWindow());
        menu.Items.Add(_openItem);
        _recordItem = new WinForms.ToolStripMenuItem(LocalizationService.Translate("Start recording"), null, (_, _) => _actions?.ToggleRecording());
        menu.Items.Add(_recordItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _exitItem = new WinForms.ToolStripMenuItem(LocalizationService.Translate("Exit"), null, (_, _) => _actions?.Exit());
        menu.Items.Add(_exitItem);
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) _actions?.ToggleFlyout();
        };
        _notifyIcon.DoubleClick += (_, _) => _actions?.ShowWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => _actions?.ShowWindow();
    }

    public void UpdateStatus(TrayState state, string tooltip)
    {
        if (_notifyIcon is null) return;
        var text = string.IsNullOrWhiteSpace(tooltip) ? "Meeting Assistant" : tooltip.Trim();
        if (text.Length > MaxTooltipLength) text = text[..(MaxTooltipLength - 1)] + "…";
        if (_notifyIcon.Text != text) _notifyIcon.Text = text;
        if (_statusItem is not null && _statusItem.Text != text) _statusItem.Text = text;
        if (_recordItem is not null)
        {
            _recordItem.Enabled = state != TrayState.Processing;
            _recordItem.Text = LocalizationService.Translate(state == TrayState.Recording ? "Stop recording" : "Start recording");
        }
        if (_openItem is not null) _openItem.Text = LocalizationService.Translate("Open Meeting Assistant");
        if (_exitItem is not null) _exitItem.Text = LocalizationService.Translate("Exit");

        if (_state == state) return;
        _state = state;
        _notifyIcon.Icon = IconFor(state);
    }

    public void ShowNotification(string title, string message, bool isError = false)
    {
        if (_notifyIcon is null) return;
        _notifyIcon.ShowBalloonTip(
            6000,
            string.IsNullOrWhiteSpace(title) ? "Meeting Assistant" : title,
            string.IsNullOrWhiteSpace(message) ? " " : message,
            isError ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
    }

    private Icon IconFor(TrayState state)
    {
        if (_icons.TryGetValue(state, out var cached)) return cached;
        var icon = DrawIcon(state);
        _icons[state] = icon;
        return icon;
    }

    private static Icon DrawIcon(TrayState state)
    {
        var size = Math.Max(16, WinForms.SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            var inset = size / 16f;
            var body = new RectangleF(inset, inset, size - inset * 2, size - inset * 2);
            using (var path = RoundedRect(body, size * 0.28f))
            using (var fill = new SolidBrush(Color.FromArgb(0x5E, 0xE0, 0xB5)))
            {
                graphics.FillPath(fill, path);
            }

            using (var font = new Font("Segoe UI", size * 0.5f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Color.FromArgb(0x0B, 0x1A, 0x14)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("M", font, ink, body, format);
            }

            var dot = state switch
            {
                TrayState.Recording => Color.FromArgb(0xFF, 0x4D, 0x5E),
                TrayState.Processing => Color.FromArgb(0xFC, 0xC8, 0x00),
                TrayState.Failed => Color.FromArgb(0xFF, 0x99, 0xA4),
                _ => Color.Empty
            };
            if (dot != Color.Empty)
            {
                var diameter = size * 0.46f;
                var dotRect = new RectangleF(size - diameter, size - diameter, diameter, diameter);
                using var ring = new SolidBrush(Color.FromArgb(0x20, 0x20, 0x20));
                using var fill = new SolidBrush(dot);
                graphics.FillEllipse(ring, dotRect);
                dotRect.Inflate(-size / 16f, -size / 16f);
                graphics.FillEllipse(fill, dotRect);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
