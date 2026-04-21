using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Charges;
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
    private readonly Guid _companyId;
    private readonly Guid _siteId;
    private Guid _memberId;

    // Cajero por defecto — Sprint 2 resuelve desde la sesión activa
    private static readonly Guid StubCashierId = new("33333333-0000-0000-0000-000000000001");

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

    public PaymentModalViewModel(IMediator mediator, Guid companyId, Guid siteId)
    {
        _mediator  = mediator;
        _companyId = companyId;
        _siteId    = siteId;
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
                _companyId, _siteId, _memberId,
                StubCashierId, Amount, Method,
                ShiftId: null,
                ChargeIds: chargeIds,
                CardLast4: IsCardRequired ? CardLast4 : null,
                CardBrand: IsCardRequired ? CardBrand : null), ct);

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
