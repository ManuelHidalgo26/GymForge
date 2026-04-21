using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IMemberRepository _members;

    [ObservableProperty] private int _totalActiveMembers;
    [ObservableProperty] private int _checkInsToday;
    [ObservableProperty] private int _overdueMembers;
    [ObservableProperty] private decimal _revenueThisMonth;
    [ObservableProperty] private bool _isLoading;

    // Sprint 1: company/site resolved from settings — stub with well-known seed GUID
    private static readonly Guid DefaultCompanyId = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid DefaultSiteId    = new("22222222-0000-0000-0000-000000000001");

    public DashboardViewModel(IMemberRepository members)
    {
        _members = members;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            // Sprint 1: real active count from DB; other KPIs wired in Sprint 2
            TotalActiveMembers = await _members.CountAsync(
                DefaultCompanyId, DefaultSiteId, MemberStatus.Active);
            OverdueMembers = await _members.CountAsync(
                DefaultCompanyId, DefaultSiteId, MemberStatus.Overdue);
            CheckInsToday    = 0;   // AccessLog aggregation — Sprint 2
            RevenueThisMonth = 0m;  // Payment aggregation — Sprint 2
        }
        finally
        {
            IsLoading = false;
        }
    }
}
