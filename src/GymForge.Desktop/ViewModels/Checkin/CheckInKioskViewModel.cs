using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.ViewModels.Checkin;

public enum KioskState { Idle, Checking, Granted, Denied }

public partial class CheckInKioskViewModel : ObservableObject
{
    private readonly ValidateSwipeUseCase _gatekeeper;
    private Guid _companyId;
    private Guid _siteId;
    private int _defaultDoorId = 1;
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

    public bool IsGranted => State == KioskState.Granted;
    public bool IsDenied  => State == KioskState.Denied;
    public bool IsIdle    => State == KioskState.Idle;

    public CheckInKioskViewModel(ValidateSwipeUseCase gatekeeper) => _gatekeeper = gatekeeper;

    public void Initialize(Guid companyId, Guid siteId, int doorId = 1)
    {
        _companyId = companyId;
        _siteId = siteId;
        _defaultDoorId = doorId;
    }

    /// <summary>Called by hardware reader (RFID/fingerprint) or manual PIN entry.</summary>
    public async Task ProcessCredentialAsync(string credential, AccessMethod method = AccessMethod.RfidCard)
    {
        if (State == KioskState.Checking) return;

        State = KioskState.Checking;
        _autoCloseTimer?.Dispose();

        try
        {
            var decision = await _gatekeeper.ValidateSwipeAsync(
                new ValidateSwipeRequest(credential, method, _defaultDoorId, _siteId, _companyId));

            MemberName = decision.MemberFullName;
            MemberPhotoUrl = decision.PhotoUrl;
            HasDebtWarning = decision.HasDebtWarning;
            OutstandingAmount = decision.OutstandingAmount;
            LastAccessTime = DateTime.Now.ToString("HH:mm:ss");

            if (decision.Granted)
            {
                State = KioskState.Granted;
                StartCountdown();
            }
            else
            {
                DenialMessage = GetDenialMessage(decision.DenialReason);
                State = KioskState.Denied;
                StartCountdown();
            }
        }
        catch
        {
            DenialMessage = "Error de sistema. Llame a recepción.";
            State = KioskState.Denied;
            StartCountdown();
        }
    }

    [RelayCommand]
    private async Task ManualCheckInAsync()
    {
        if (!string.IsNullOrWhiteSpace(CredentialInput))
            await ProcessCredentialAsync(CredentialInput.Trim(), AccessMethod.Manual);
    }

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
                // Post back to UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(Reset);
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private static string GetDenialMessage(AccessDenialReason? reason) => reason switch
    {
        AccessDenialReason.TagUnknown           => "Credencial no reconocida",
        AccessDenialReason.IncompleteMembership => "Sin membresía activa",
        AccessDenialReason.Expired              => "Membresía vencida",
        AccessDenialReason.Owing                => "Deuda pendiente — ver recepción",
        AccessDenialReason.OutOfHours           => "Fuera del horario permitido",
        AccessDenialReason.DoorNotAllowed       => "Sin acceso a esta zona",
        AccessDenialReason.GenderRestricted     => "Área restringida",
        AccessDenialReason.AlreadyInside        => "Ya registrado como presente",
        AccessDenialReason.NoVisitsLeft         => "Pack de visitas agotado",
        AccessDenialReason.Suspended            => "Acceso suspendido — ver recepción",
        _                                       => "Acceso denegado"
    };
}
