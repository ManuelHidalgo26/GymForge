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
    private readonly SessionContext _session;

    [ObservableProperty] private int _totalActiveMembers;
    [ObservableProperty] private int _checkInsToday;
    [ObservableProperty] private int _overdueMembers;
    [ObservableProperty] private decimal _revenueThisMonth;
    [ObservableProperty] private bool _isLoading;

    public DashboardViewModel(IMemberRepository members, IPaymentRepository payments, SessionContext session)
    {
        _members = members;
        _payments = payments;
        _session = session;
    }

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

            CheckInsToday = 0;   // AccessLog aggregation — pendiente
        }
        finally
        {
            IsLoading = false;
        }
    }
}
