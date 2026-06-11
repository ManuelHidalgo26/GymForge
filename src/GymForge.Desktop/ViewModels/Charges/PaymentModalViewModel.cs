using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Charges;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Charges;

/// <summary>
/// ViewModel del modal "Registrar pago".
/// Puede pre-cargar un cobro específico o dejar que el cajero ingrese el monto libre.
/// </summary>
public partial class PaymentModalViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private readonly ReceiptService _receipts;
    private Guid _memberId;

    [ObservableProperty] private ChargeDto? _targetCharge;
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private decimal _amount;
    [ObservableProperty] private PaymentMethod _method = PaymentMethod.Cash;
    [ObservableProperty] private string? _cardLast4;
    [ObservableProperty] private string? _cardBrand;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;

    public IReadOnlyList<PaymentMethod> Methods { get; } =
        Enum.GetValues<PaymentMethod>().ToList();

    public bool IsCardRequired =>
        Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard;

    public event Action<PaymentDto>? PaymentRegistered;
    public event Action? Cancelled;

    public PaymentModalViewModel(IMediator mediator, SessionContext session, ReceiptService receipts)
    {
        _mediator = mediator;
        _session  = session;
        _receipts = receipts;
    }

    public void PreSelectCharge(ChargeDto charge)
    {
        TargetCharge = charge;
        _memberId    = charge.MemberId;
        Amount       = charge.AmountOutstanding;
    }

    partial void OnMethodChanged(PaymentMethod value) =>
        OnPropertyChanged(nameof(IsCardRequired));

    // ── Commands ──────────────────────────────────────────────────────────────

    private bool CanConfirm => Amount > 0 && !IsSaving
        && (!IsCardRequired || (CardLast4?.Length == 4));

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken ct = default)
    {
        IsSaving     = true;
        ErrorMessage = null;
        try
        {
            var chargeIds = TargetCharge is not null
                ? new List<Guid> { TargetCharge.Id }
                : null;

            var dto = await _mediator.Send(new ProcessPaymentCommand(
                _session.CompanyId, _session.SiteId, _memberId,
                _session.EffectiveCashierId, Amount, Method,
                ShiftId: _session.OpenShiftId,
                ChargeIds: chargeIds,
                CardLast4: IsCardRequired ? CardLast4 : null,
                CardBrand: IsCardRequired ? CardBrand : null), ct);

            await _receipts.TryGenerateAndOpenAsync(dto.Id, _session.CompanyId, ct);
            PaymentRegistered?.Invoke(dto);
        }
        catch (FluentValidation.ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error al registrar pago: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
