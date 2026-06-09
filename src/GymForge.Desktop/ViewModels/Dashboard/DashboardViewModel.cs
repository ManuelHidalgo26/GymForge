using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IMemberRepository _members;
    private readonly SessionContext _session;

    [ObservableProperty] private int _totalActiveMembers;
    [ObservableProperty] private int _checkInsToday;
    [ObservableProperty] private int _overdueMembers;
    [ObservableProperty] private decimal _revenueThisMonth;
    [ObservableProperty] private bool _isLoading;

    public DashboardViewModel(IMemberRepository members, SessionContext session)
    {
        _members = members;
        _session = session;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            // KPIs reales de la sede activa; check-ins/recaudación se cablean más adelante.
            TotalActiveMembers = await _members.CountAsync(
                _session.CompanyId, _session.SiteId, MemberStatus.Active);
            OverdueMembers = await _members.CountAsync(
                _session.CompanyId, _session.SiteId, MemberStatus.Overdue);
            CheckInsToday    = 0;   // AccessLog aggregation — Sprint 2
            RevenueThisMonth = 0m;  // Payment aggregation — Sprint 2
        }
        finally
        {
            IsLoading = false;
        }
    }
}
