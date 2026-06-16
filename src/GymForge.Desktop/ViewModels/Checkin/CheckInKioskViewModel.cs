using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.UseCases.Access;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Checkin;

public enum KioskState { Idle, Checking, Granted, Denied }

public partial class CheckInKioskViewModel : ObservableObject
{
    private readonly IValidateSwipeUseCase _gatekeeper;
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private const int DefaultDoorId = 1;
    private Timer? _autoCloseTimer;

    [ObservableProperty] private KioskState _state = KioskState.Idle;
    [ObservableProperty] private string _memberName = string.Empty;
    [ObservableProperty] private string? _memberPhotoUrl;
    [ObservableProperty] private string? _denialMessage;
    [ObservableProperty] private bool _hasDebtWarning;
    [ObservableProperty] private decimal _outstandingAmount;
    [ObservableProperty] private int _countdownSeconds = 3;
    [ObservableProperty] private string _lastAccessTime = string.Empty;
    [ObservableProperty] private string _credentialInput = string.Empty;
    [ObservableProperty] private ObservableCollection<AccessLogRowDto> _todaysAccess = [];

    public bool IsGranted => State == KioskState.Granted;
    public bool IsDenied  => State == KioskState.Denied;
    public bool IsIdle    => State == KioskState.Idle;
    public bool HasHistory => TodaysAccess.Count > 0;

    /// <summary>El code-behind enfoca el campo de DNI cuando se dispara (al cargar y al volver a Idle).</summary>
    public event Action? FocusRequested;

    public CheckInKioskViewModel(IValidateSwipeUseCase gatekeeper, IMediator mediator, SessionContext session)
    {
        _gatekeeper = gatekeeper;
        _mediator = mediator;
        _session = session;
    }

    partial void OnStateChanged(KioskState value)
    {
        ManualCheckInCommand.NotifyCanExecuteChanged();
    }

    partial void OnTodaysAccessChanged(ObservableCollection<AccessLogRowDto> value) =>
        OnPropertyChanged(nameof(HasHistory));

    /// <summary>Carga los accesos del día de la sede (para poblar el panel al abrir).</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var rows = await _mediator.Send(new GetTodaysAccessLogQuery(_session.CompanyId, _session.SiteId), ct);
        TodaysAccess = new ObservableCollection<AccessLogRowDto>(rows);
    }

    /// <summary>Lo llama el lector (RFID/huella) o la carga manual de DNI.</summary>
    public async Task ProcessCredentialAsync(string credential, AccessMethod method = AccessMethod.RfidCard)
    {
        if (State == KioskState.Checking) return;   // anti doble-submit

        State = KioskState.Checking;
        CredentialInput = string.Empty;             // limpiar ya: un segundo Enter no reprocesa
        _autoCloseTimer?.Dispose();

        try
        {
            var decision = await _gatekeeper.ValidateSwipeAsync(
                new ValidateSwipeRequest(credential, method, DefaultDoorId, _session.SiteId, _session.CompanyId));

            MemberName = decision.MemberFullName;
            MemberPhotoUrl = decision.PhotoUrl;
            HasDebtWarning = decision.HasDebtWarning;
            OutstandingAmount = decision.OutstandingAmount;
            LastAccessTime = DateTime.Now.ToString("HH:mm:ss");

            if (!decision.Granted)
                DenialMessage = AccessMessages.Denial(decision.DenialReason);

            AddHistoryRow(decision);
            State = decision.Granted ? KioskState.Granted : KioskState.Denied;
            StartCountdown();
        }
        catch
        {
            DenialMessage = "Error de sistema. Llame a recepción.";
            State = KioskState.Denied;
            StartCountdown();
        }
    }

    private void AddHistoryRow(AccessDecision decision)
    {
        var name = string.IsNullOrWhiteSpace(decision.MemberFullName) ? "Desconocido" : decision.MemberFullName;
        var status = AccessMessages.Status(decision.Granted, decision.DenialReason);
        TodaysAccess.Insert(0, new AccessLogRowDto(name, DateTime.Now, decision.Granted, status));
        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand(CanExecute = nameof(CanManualCheckIn))]
    private async Task ManualCheckInAsync()
    {
        if (!string.IsNullOrWhiteSpace(CredentialInput))
            await ProcessCredentialAsync(CredentialInput.Trim(), AccessMethod.Manual);
    }

    private bool CanManualCheckIn() => State != KioskState.Checking;

    [RelayCommand]
    private void Reset()
    {
        _autoCloseTimer?.Dispose();
        State = KioskState.Idle;
        MemberName = string.Empty;
        MemberPhotoUrl = null;
        DenialMessage = null;
        HasDebtWarning = false;
        OutstandingAmount = 0;
        CountdownSeconds = 3;
        CredentialInput = string.Empty;
        FocusRequested?.Invoke();   // listo para el próximo DNI sin tocar el mouse
    }

    private void StartCountdown()
    {
        CountdownSeconds = 3;
        _autoCloseTimer = new Timer(_ =>
        {
            if (CountdownSeconds > 0)
            {
                CountdownSeconds--;
            }
            else
            {
                _autoCloseTimer?.Dispose();
                Avalonia.Threading.Dispatcher.UIThread.Post(Reset);
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
}
