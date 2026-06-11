using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IMemberRepository _members;
    private readonly IPaymentRepository _payments;
    private readonly IAccessLogRepository _accessLogs;
    private readonly SessionContext _session;

    /// <summary>Acciones rápidas: las cablea MainWindowViewModel a la navegación.</summary>
    public event Action? CreateMemberRequested;
    public event Action? CheckInRequested;
    public event Action? ChargeRequested;

    [ObservableProperty] private int _totalActiveMembers;
    [ObservableProperty] private int _checkInsToday;
    [ObservableProperty] private int _overdueMembers;
    [ObservableProperty] private decimal _revenueThisMonth;
    [ObservableProperty] private bool _isLoading;

    public DashboardViewModel(
        IMemberRepository members, IPaymentRepository payments,
        IAccessLogRepository accessLogs, SessionContext session)
    {
        _members = members;
        _payments = payments;
        _accessLogs = accessLogs;
        _session = session;
    }

    [RelayCommand] private void QuickCreateMember() => CreateMemberRequested?.Invoke();
    [RelayCommand] private void QuickCheckIn() => CheckInRequested?.Invoke();
    [RelayCommand] private void QuickCharge() => ChargeRequested?.Invoke();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            TotalActiveMembers = await _members.CountAsync(
                _session.CompanyId, _session.SiteId, MemberStatus.Active);
            OverdueMembers = await _members.CountAsync(
                _session.CompanyId, _session.SiteId, MemberStatus.Overdue);

            // Recaudación del mes en curso (pagos persistidos). Se refresca al volver al Dashboard.
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            RevenueThisMonth = await _payments.SumReceivedAsync(
                _session.CompanyId, _session.SiteId, monthStart, monthStart.AddMonths(1));

            // Accesos del día (día local convertido a UTC, que es como se guardan)
            var dayStartUtc = DateTime.Today.ToUniversalTime();
            var logs = await _accessLogs.GetBySiteAsync(_session.SiteId, dayStartUtc, dayStartUtc.AddDays(1));
            CheckInsToday = logs.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
