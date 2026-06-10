using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Cash;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Cash;

/// <summary>
/// Caja registradora: login de cajero por PIN, apertura de turno, movimientos
/// de efectivo (ingreso/egreso) y cierre con arqueo (diferencia declarado vs. sistema).
/// </summary>
public partial class CashViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IShiftRepository _shiftRepo;
    private readonly IMemberRepository _memberRepo;
    private readonly SessionContext _session;

    public SessionContext Session => _session;

    [ObservableProperty] private RegisterSaleViewModel? _saleModal;
    [ObservableProperty] private bool _isSaleModalOpen;

    [ObservableProperty] private string _pin = string.Empty;
    [ObservableProperty] private decimal _openingCash;
    [ObservableProperty] private decimal _movementAmount;
    [ObservableProperty] private string _movementNotes = string.Empty;
    [ObservableProperty] private decimal _declaredCash;
    [ObservableProperty] private string _closeNotes = string.Empty;
    [ObservableProperty] private ShiftDto? _currentShift;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;

    public bool HasOpenShift => CurrentShift is { Status: ShiftStatus.Open };
    public bool IsShiftClosed => CurrentShift is { Status: ShiftStatus.Closed };
    public bool CanOpenShift => _session.IsSignedIn && !HasOpenShift;
    public bool HasNoMovements => CurrentShift is { Movements.Count: 0 };

    public CashViewModel(
        IMediator mediator, IShiftRepository shiftRepo, IMemberRepository memberRepo, SessionContext session)
    {
        _mediator = mediator;
        _shiftRepo = shiftRepo;
        _memberRepo = memberRepo;
        _session = session;

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SessionContext.IsSignedIn) or nameof(SessionContext.CashierId))
                OnPropertyChanged(nameof(CanOpenShift));
        };
    }

    [RelayCommand]
    private async Task OpenSaleModalAsync()
    {
        var modal = new RegisterSaleViewModel(_mediator, _memberRepo, _session);
        modal.Registered += async () =>
        {
            IsSaleModalOpen = false;
            SaleModal = null;
            await LoadAsync();   // refresca el turno: el movimiento de caja ya impactó
        };
        modal.Cancelled += () =>
        {
            IsSaleModalOpen = false;
            SaleModal = null;
        };

        await modal.LoadAsync();
        SaleModal = modal;
        IsSaleModalOpen = true;
    }

    partial void OnCurrentShiftChanged(ShiftDto? value)
    {
        OnPropertyChanged(nameof(HasOpenShift));
        OnPropertyChanged(nameof(IsShiftClosed));
        OnPropertyChanged(nameof(CanOpenShift));
        OnPropertyChanged(nameof(HasNoMovements));
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        StatusMessage = null;
        if (_session.SiteId == Guid.Empty) return;

        var open = await _shiftRepo.GetOpenForSiteAsync(_session.SiteId, ct);
        CurrentShift = open is not null ? ShiftDto.FromEntity(open) : null;
        _session.SetOpenShift(open?.Id);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        StatusMessage = null;
        var staff = await _mediator.Send(new AuthenticateStaffCommand(_session.CompanyId, Pin));
        if (staff is null) { StatusMessage = "PIN incorrecto."; return; }
        _session.SignIn(staff);
        Pin = string.Empty;
    }

    [RelayCommand]
    private void SignOut()
    {
        _session.SignOut();
        CurrentShift = null;
    }

    [RelayCommand]
    private Task OpenShiftAsync() => RunAsync(async () =>
    {
        if (!_session.IsSignedIn || _session.CashierId is null)
        {
            StatusMessage = "Iniciá sesión de cajero primero.";
            return;
        }

        CurrentShift = await _mediator.Send(new OpenShiftCommand(
            _session.CompanyId, _session.SiteId, _session.CashierId.Value, OpeningCash));
        _session.SetOpenShift(CurrentShift.Id);
        OpeningCash = 0;
    });

    [RelayCommand]
    private Task AddIncomeAsync() => AddMovementAsync(CashMovementType.Income);

    [RelayCommand]
    private Task AddExpenseAsync() => AddMovementAsync(CashMovementType.Expense);

    private Task AddMovementAsync(CashMovementType type) => RunAsync(async () =>
    {
        if (CurrentShift is null) return;

        CurrentShift = await _mediator.Send(new AddCashMovementCommand(
            CurrentShift.Id, type, CashMovementCategory.PettyCash, MovementAmount,
            string.IsNullOrWhiteSpace(MovementNotes) ? null : MovementNotes));
        MovementAmount = 0;
        MovementNotes = string.Empty;
    });

    [RelayCommand]
    private Task CloseShiftAsync() => RunAsync(async () =>
    {
        if (CurrentShift is null) return;

        CurrentShift = await _mediator.Send(new CloseShiftCommand(
            CurrentShift.Id, DeclaredCash,
            string.IsNullOrWhiteSpace(CloseNotes) ? null : CloseNotes));
        _session.SetOpenShift(null);
        DeclaredCash = 0;
        CloseNotes = string.Empty;
    });

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        StatusMessage = null;
        try { await action(); }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
