using Avalonia.Data.Converters;
using Avalonia.Media;
using GymForge.Domain.Enums;
using System.Globalization;

namespace GymForge.Desktop.Converters;

/// <summary>Maps MemberStatus to a background color brush for status badges.</summary>
public class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    private static readonly IBrush Active    = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush Overdue   = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush Expired   = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush Frozen    = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush Suspended = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush Cancelled = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush Prospect  = new SolidColorBrush(Color.Parse("#8B5CF6"));
    private static readonly IBrush Default   = new SolidColorBrush(Color.Parse("#9CA3AF"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MemberStatus status ? status switch
        {
            MemberStatus.Active    => Active,
            MemberStatus.Overdue   => Overdue,
            MemberStatus.Expired   => Expired,
            MemberStatus.Frozen    => Frozen,
            MemberStatus.Suspended => Suspended,
            MemberStatus.Cancelled => Cancelled,
            MemberStatus.Prospect  => Prospect,
            _                      => Default
        } : Default;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
