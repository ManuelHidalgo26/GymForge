using Avalonia.Data.Converters;
using Avalonia.Media;
using GymForge.Domain.Enums;
using System.Globalization;

namespace GymForge.Desktop.Converters;

/// <summary>Mapea MemberStatus a un Color para los badges de estado.</summary>
public class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    private static readonly Color Active    = Color.Parse("#22C55E");
    private static readonly Color Overdue   = Color.Parse("#F59E0B");
    private static readonly Color Expired   = Color.Parse("#EF4444");
    private static readonly Color Frozen    = Color.Parse("#3B82F6");
    private static readonly Color Suspended = Color.Parse("#EF4444");
    private static readonly Color Cancelled = Color.Parse("#6B7280");
    private static readonly Color Prospect  = Color.Parse("#8B5CF6");
    private static readonly Color Default   = Color.Parse("#9CA3AF");

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
