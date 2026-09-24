using System.Windows.Controls;
using System.Windows.Threading;

namespace MeetingAssistant.Services;

public static class SmoothScroll
{
    public static void Attach(ScrollViewer viewer)
    {
        DispatcherTimer? timer = null;
        var target = 0d;
        viewer.PreviewMouseWheel += (_, args) =>
        {
            if (args.Handled) return;
            target = Math.Clamp(viewer.VerticalOffset + (args.Delta > 0 ? -120 : 120), 0, viewer.ScrollableHeight);
            if (timer is null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) =>
                {
                    var next = viewer.VerticalOffset + ((target - viewer.VerticalOffset) * 0.35);
                    if (Math.Abs(target - next) < 1)
                    {
                        viewer.ScrollToVerticalOffset(target);
                        timer.Stop();
                        return;
                    }

                    viewer.ScrollToVerticalOffset(next);
                };
            }

            timer.Start();
            args.Handled = true;
        };
    }
}
