using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Memberships;
using GymForge.Desktop.ViewModels.Charges;
using MediatR;
using System.Collections.ObjectModel;

namespace GymForge.Desktop.ViewModels.Members;

public enum MemberDetailTab { Data, Memberships, Charges, Access, Routines }

public partial class MemberDetailViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IAccessLogRepository _accessLogRepo;
    private readonly IChargeRepository _chargeRepo;

    [ObservableProperty] private MemberDto _member = null!;
    [ObservableProperty] private MemberDetailTab _activeTab = MemberDetailTab.Data;
    [ObservableProperty] private bool _isLoading;

    // Tab: Membresías
    [ObservableProperty] private ObservableCollection<MembershipDto> _memberships = [];

    // Tab: Cobros — delegado a ChargesViewModel reutilizable
    [ObservableProperty] private ChargesViewModel _chargesVm = null!;

    // Tab: Accesos
    [ObservableProperty] private ObservableCollection<AccessLogRowVm> _accessLogs = [];

    // Tabs list for ComboBox / TabStrip binding
    public IReadOnlyList<MemberDetailTab> Tabs { get; } =
        Enum.GetValues<MemberDetailTab>().ToList();

    public event Action? BackRequested;

    public MemberDetailViewModel(
        IMediator mediator,
        IMembershipRepository membershipRepo,
        IAccessLogRepository accessLogRepo,
        IChargeRepository chargeRepo,
        ChargesViewModel chargesVm)
    {
        _mediator       = mediator;
        _membershipRepo = membershipRepo;
        _accessLogRepo  = accessLogRepo;
        _chargeRepo     = chargeRepo;
        ChargesVm       = chargesVm;
    }

    public async Task LoadAsync(MemberDto member, CancellationToken ct = default)
    {
        Member = member;
        ChargesVm.FilterByMember(member.Id);
        await LoadActiveTabAsync(ct);
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SwitchTabAsync(MemberDetailTab tab)
    {
        ActiveTab = tab;
        await LoadActiveTabAsync();
    }

    private async Task LoadActiveTabAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            switch (ActiveTab)
            {
                case MemberDetailTab.Memberships:
                    await LoadMembershipsAsync(ct);
                    break;
                case MemberDetailTab.Charges:
                    await ChargesVm.LoadAsync(ct);
                    break;
                case MemberDetailTab.Access:
                    await LoadAccessLogsAsync(ct);
                    break;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Membresías ────────────────────────────────────────────────────────────

    private async Task LoadMembershipsAsync(CancellationToken ct = default)
    {
        var list = await _membershipRepo.GetByMemberAsync(Member.Id, ct);
        Memberships = new ObservableCollection<MembershipDto>(
            list.OrderByDescending(m => m.StartDate).Select(MembershipDto.FromEntity));
    }

    [RelayCommand]
    private async Task FreezeMembershipAsync(MembershipDto ms)
    {
        // Abre modal → Sprint 2; por ahora congela 30 días directo
        var today = DateOnly.FromDateTime(DateTime.Today);
        await _mediator.Send(new FreezeMembershipCommand(
            ms.Id, today, today.AddDays(30), "Congelamiento manual"));
        await LoadMembershipsAsync();
    }

    [RelayCommand]
    private async Task CancelMembershipAsync(MembershipDto ms)
    {
        await _mediator.Send(new CancelMembershipCommand(ms.Id, "Cancelación manual"));
        await LoadMembershipsAsync();
    }

    // ── Accesos ───────────────────────────────────────────────────────────────

    private async Task LoadAccessLogsAsync(CancellationToken ct)
    {
        var logs = await _accessLogRepo.GetByMemberAsync(Member.Id, 50, ct);
        AccessLogs = new ObservableCollection<AccessLogRowVm>(
            logs.Select(l => new AccessLogRowVm(
                l.SwipedAt,
                l.AccessGranted ? "Concedido" : "Denegado",
                l.DenialReason?.ToString(),
                l.AccessGranted ? "#2E7D32" : "#C62828")));
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke();
}

// ── Row VMs ────────────────────────────────────────────────────────────────────

public record AccessLogRowVm(
    DateTime SwipedAt,
    string Result,
    string? DenialReason,
    string Color);
