using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            // Let a nested list (the transcript) scroll first; the page takes over at its ends.
            if (NestedCanScroll(viewer, args.OriginalSource as DependencyObject, args.Delta)) return;
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

    private static bool NestedCanScroll(ScrollViewer outer, DependencyObject? source, int delta)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, outer); node = Parent(node))
        {
            if (node is not ScrollViewer inner || inner.ScrollableHeight <= 0) continue;
            if (delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight - 0.5)
                return true;
        }

        return false;
    }

    private static DependencyObject? Parent(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}
