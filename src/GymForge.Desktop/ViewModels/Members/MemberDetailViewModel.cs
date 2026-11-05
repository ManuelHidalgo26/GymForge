using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Memberships;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Charges;
using GymForge.Desktop.ViewModels.Routines;
using MediatR;
using System.Collections.ObjectModel;

namespace GymForge.Desktop.ViewModels.Members;

public enum MemberDetailTab { Data, Memberships, Charges, Access, Routines }

public partial class MemberDetailViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IAccessLogRepository _accessLogRepo;
    private readonly SessionContext _session;
    private readonly IMemberPhotoStore _photoStore;

    [ObservableProperty] private MemberDto _member = null!;
    [ObservableProperty] private MemberDetailTab _activeTab = MemberDetailTab.Data;
    [ObservableProperty] private bool _isLoading;

    // Tab: Datos — la foto se guarda apenas se elige; la credencial al asignarla; el resto, en modo edición
    public PhotoPickerViewModel Photo { get; }
    public MemberEditViewModel Edit { get; }
    [ObservableProperty] private string _newCredential = string.Empty;
    [ObservableProperty] private string? _credentialMessage;
    [ObservableProperty] private bool _credentialAssigned;

    // Tab: Membresías
    [ObservableProperty] private ObservableCollection<MembershipDto> _memberships = [];
    [ObservableProperty] private string? _membershipsMessage;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsFreezeModalOpen))]
    private FreezeModalViewModel? _freezeModal;

    public bool IsFreezeModalOpen => FreezeModal is not null;

    // Tab: Cobros — delegado a ChargesViewModel reutilizable
    [ObservableProperty] private ChargesViewModel _chargesVm = null!;

    // Tab: Accesos
    [ObservableProperty] private ObservableCollection<AccessLogRowVm> _accessLogs = [];

    // Tab: Rutinas — el armador del hub, embebido: el socio ya está dado, no hay buscador
    public RoutineBuilderViewModel RoutineBuilder { get; }

    // Tabs list for ComboBox / TabStrip binding
    public IReadOnlyList<MemberDetailTab> Tabs { get; } =
        Enum.GetValues<MemberDetailTab>().ToList();

    public event Action? BackRequested;

    public MemberDetailViewModel(
        IMediator mediator,
        IMembershipRepository membershipRepo,
        IAccessLogRepository accessLogRepo,
        ChargesViewModel chargesVm,
        RoutineBuilderViewModel routineBuilder,
        SessionContext session,
        IImagePicker imagePicker,
        IMemberPhotoStore photoStore)
    {
        _mediator       = mediator;
        _membershipRepo = membershipRepo;
        _accessLogRepo  = accessLogRepo;
        ChargesVm       = chargesVm;
        RoutineBuilder  = routineBuilder;
        _session        = session;
        _photoStore     = photoStore;

        Photo = new PhotoPickerViewModel(imagePicker);
        Photo.ImageChosen += image => _ = SavePhotoAsync(image);

        // Antes de que la vista se enlace: sin esto, al asignarle el DataContext el armador cargaría los socios del hub.
        RoutineBuilder.ShowMemberPicker = false;

        Edit = new MemberEditViewModel(mediator, session);
        Edit.Saved += saved => Member = saved;
    }

    public async Task LoadAsync(MemberDto member, CancellationToken ct = default)
    {
        Edit.CancelCommand.Execute(null);
        Member = member;
        Photo.ShowSaved(member.PhotoUrl);
        ChargesVm.FilterByMember(member.Id);
        await LoadActiveTabAsync(ct);
    }

    // ── Datos: edición, foto y credencial ─────────────────────────────────────

    [RelayCommand]
    private void StartEdit() => Edit.Begin(Member);

    /// <summary>Guarda en disco y en el socio la foto recién elegida. Nunca lanza: el motivo queda en el mensaje de la foto.</summary>
    private async Task SavePhotoAsync(byte[] image)
    {
        try
        {
            var path = _photoStore.Save(Member.Id, image);
            Member = await _mediator.Send(new UpdateMemberPhotoCommand(_session.CompanyId, Member.Id, path));
            Photo.MarkSaved();
        }
        catch (Exception ex)
        {
            Photo.Message = $"No se pudo guardar la foto: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveCredentialAsync()
    {
        CredentialMessage = null;
        CredentialAssigned = false;
        try
        {
            Member = await _mediator.Send(new AssignCredentialCommand(_session.CompanyId, Member.Id, NewCredential));
            NewCredential = string.Empty;
            CredentialAssigned = true;
            CredentialMessage = "Credencial asignada.";
        }
        catch (FluentValidation.ValidationException vex)
        {
            CredentialMessage = string.Join(" ", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex)
        {
            CredentialMessage = ex.Message;
        }
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
        MembershipsMessage = null;
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
                case MemberDetailTab.Routines:
                    await RoutineBuilder.InitForMemberAsync(Member, ct);
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
    private async Task OpenFreezeModalAsync(MembershipDto ms)
    {
        MembershipsMessage = null;
        FreezeQuotaDto quota;
        try
        {
            quota = await _mediator.Send(new GetFreezeQuotaQuery(ms.Id));
        }
        catch (Exception ex)
        {
            MembershipsMessage = ex.Message;
            return;
        }

        FreezeModal = CreateFreezeModal(ms, quota);
    }

    private FreezeModalViewModel CreateFreezeModal(MembershipDto ms, FreezeQuotaDto quota)
    {
        var modal = new FreezeModalViewModel(_mediator, ms, quota);
        modal.MembershipFrozen += () =>
        {
            FreezeModal = null;
            _ = LoadMembershipsAsync();
        };
        modal.Cancelled += () => FreezeModal = null;
        return modal;
    }

    [RelayCommand]
    private async Task ReactivateMembershipAsync(MembershipDto ms)
    {
        MembershipsMessage = null;
        try
        {
            await _mediator.Send(new UnfreezeMembershipCommand(ms.Id));
        }
        catch (Exception ex)
        {
            MembershipsMessage = ex.Message;
        }

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
                l.AccessGranted ? "#22C55E" : "#EF4444")));
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
