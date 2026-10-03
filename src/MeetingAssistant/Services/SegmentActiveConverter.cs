using System.Globalization;
using System.Windows.Data;

namespace MeetingAssistant.Services;

public sealed class SegmentActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;
        var segmentId = values[0] as string;
        var activeId = values[1] as string;
        return !string.IsNullOrEmpty(segmentId)
            && string.Equals(segmentId, activeId, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
