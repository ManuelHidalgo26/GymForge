using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace GymForge.Desktop.ViewModels.Charges;

/// <summary>
/// Listado de cobros pendientes/vencidos.
/// Usado tanto desde la sección Caja (todos los socios) como desde la
/// ficha de socio (filtrado a un solo MemberId).
/// </summary>
public partial class ChargesViewModel : ObservableObject
{
    private readonly IChargeRepository _chargeRepo;
    private readonly IMediator _mediator;

    // Injected at construction; null = mostrar todos los cobros del site
    private Guid? _filterMemberId;

    private static readonly Guid DefaultCompanyId = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid DefaultSiteId    = new("22222222-0000-0000-0000-000000000001");

    [ObservableProperty] private ObservableCollection<ChargeRowVm> _charges = [];
    [ObservableProperty] private ChargeRowVm? _selectedCharge;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPaymentModalOpen;
    [ObservableProperty] private PaymentModalViewModel? _paymentModal;
    [ObservableProperty] private ChargeStatusFilter _statusFilter = ChargeStatusFilter.Pending;
    [ObservableProperty] private decimal _totalOutstanding;

    public IReadOnlyList<ChargeStatusFilter> StatusFilters { get; } =
        Enum.GetValues<ChargeStatusFilter>().ToList();

    public ChargesViewModel(IChargeRepository chargeRepo, IMediator mediator)
    {
        _chargeRepo = chargeRepo;
        _mediator   = mediator;
    }

    /// <summary>Limita la vista a los cobros de un socio concreto.</summary>
    public void FilterByMember(Guid memberId) => _filterMemberId = memberId;

    // ── Load ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            IReadOnlyList<Domain.Entities.Charge> raw;

            if (_filterMemberId.HasValue)
            {
                raw = StatusFilter == ChargeStatusFilter.Overdue
                    ? (await _chargeRepo.GetOverdueAsync(DefaultCompanyId, DateOnly.FromDateTime(DateTime.Today), ct))
                        .Where(c => c.MemberId == _filterMemberId).ToList()
                    : await _chargeRepo.GetPendingAsync(_filterMemberId.Value, ct);
            }
            else
            {
                raw = StatusFilter == ChargeStatusFilter.Overdue
                    ? await _chargeRepo.GetOverdueAsync(DefaultCompanyId, DateOnly.FromDateTime(DateTime.Today), ct)
                    : await _chargeRepo.GetOverdueAsync(DefaultCompanyId, DateOnly.MaxValue, ct);
            }

            Charges = new ObservableCollection<ChargeRowVm>(
                raw.OrderByDescending(c => c.DueDate)
                   .Select(c => new ChargeRowVm(ChargeDto.FromEntity(c))));

            TotalOutstanding = Charges.Sum(c => c.Dto.AmountOutstanding);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Payment modal ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenPaymentModal(ChargeRowVm? row = null)
    {
        var modal = new PaymentModalViewModel(_mediator, DefaultCompanyId, DefaultSiteId);

        if (row is not null)
            modal.PreSelectCharge(row.Dto);
        else if (SelectedCharge is not null)
            modal.PreSelectCharge(SelectedCharge.Dto);

        modal.PaymentRegistered += async _ =>
        {
            IsPaymentModalOpen = false;
            PaymentModal = null;
            await LoadAsync();
        };
        modal.Cancelled += () =>
        {
            IsPaymentModalOpen = false;
            PaymentModal = null;
        };

        PaymentModal = modal;
        IsPaymentModalOpen = true;
    }

    partial void OnStatusFilterChanged(ChargeStatusFilter value)
        => _ = LoadAsync();
}

// ── Filter enum ────────────────────────────────────────────────────────────────

public enum ChargeStatusFilter { Pending, Overdue, All }

// ── Row wrapper ────────────────────────────────────────────────────────────────

public sealed class ChargeRowVm(ChargeDto dto) : ObservableObject
{
    public ChargeDto Dto { get; } = dto;
    public string StatusLabel => Dto.Status switch
    {
        ChargeStatus.Paid         => "Pagado",
        ChargeStatus.PartiallyPaid=> "Parcial",
        ChargeStatus.Overdue      => "Vencido",
        ChargeStatus.Pending      => "Pendiente",
        ChargeStatus.Billed       => "Facturado",
        ChargeStatus.WrittenOff   => "Incobrable",
        _                         => Dto.Status.ToString()
    };
    public string ConceptLabel => Dto.ConceptType switch
    {
        ConceptType.MembershipFee    => "Cuota",
        ConceptType.SignupFee        => "Inscripción",
        ConceptType.LateFee          => "Mora",
        ConceptType.ProductSale      => "Producto",
        ConceptType.PersonalTraining => "Entrenamiento",
        ConceptType.ClassDropIn      => "Clase suelta",
        ConceptType.Adjustment       => "Ajuste",
        _                            => Dto.ConceptType.ToString()
    };
    public string StatusColor => Dto.Status switch
    {
        ChargeStatus.Paid          => "#2E7D32",
        ChargeStatus.PartiallyPaid => "#F57C00",
        ChargeStatus.Overdue       => "#C62828",
        ChargeStatus.Pending       => "#1565C0",
        _                          => "#757575"
    };
}
