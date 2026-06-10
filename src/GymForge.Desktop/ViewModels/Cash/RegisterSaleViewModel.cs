using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Catalog;
using GymForge.Application.UseCases.Sales;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Cash;

public enum SaleConcept { Membership, Product }

/// <summary>
/// Modal de "Registrar cobro/venta" dentro de la caja: cobra una membresía
/// (socio + plan) o vende un producto (socio + producto + cantidad). Al confirmar
/// impacta la caja abierta de la sesión.
/// </summary>
public partial class RegisterSaleViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IMemberRepository _memberRepo;
    private readonly SessionContext _session;

    [ObservableProperty] private SaleConcept _concept = SaleConcept.Membership;

    [ObservableProperty] private ObservableCollection<MemberDto> _members = [];
    [ObservableProperty] private MemberDto? _selectedMember;

    [ObservableProperty] private ObservableCollection<MembershipTypeDto> _plans = [];
    [ObservableProperty] private MembershipTypeDto? _selectedPlan;

    [ObservableProperty] private ObservableCollection<ProductDto> _products = [];
    [ObservableProperty] private ProductDto? _selectedProduct;
    [ObservableProperty] private decimal _quantity = 1;

    [ObservableProperty] private PaymentMethod _method = PaymentMethod.Cash;
    [ObservableProperty] private string? _cardLast4;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSaving;

    public IReadOnlyList<PaymentMethod> Methods { get; } = Enum.GetValues<PaymentMethod>().ToList();

    public bool IsMembership
    {
        get => Concept == SaleConcept.Membership;
        set { if (value) Concept = SaleConcept.Membership; }
    }

    public bool IsProduct
    {
        get => Concept == SaleConcept.Product;
        set { if (value) Concept = SaleConcept.Product; }
    }

    public bool IsCardRequired => Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard;

    public decimal EstimatedTotal => Concept == SaleConcept.Membership
        ? SelectedPlan?.Price ?? 0m
        : (SelectedProduct?.SalePrice ?? 0m) * Quantity;

    public event Action? Registered;
    public event Action? Cancelled;

    public RegisterSaleViewModel(IMediator mediator, IMemberRepository memberRepo, SessionContext session)
    {
        _mediator = mediator;
        _memberRepo = memberRepo;
        _session = session;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var members = await _memberRepo.GetPagedAsync(_session.CompanyId, _session.SiteId, 1, 500, ct: ct);
        Members = new ObservableCollection<MemberDto>(members.Select(MemberDto.FromEntity));
        Plans = new ObservableCollection<MembershipTypeDto>(
            await _mediator.Send(new GetMembershipTypesQuery(_session.CompanyId), ct));
        Products = new ObservableCollection<ProductDto>(
            await _mediator.Send(new GetProductsQuery(_session.CompanyId), ct));
    }

    partial void OnConceptChanged(SaleConcept value)
    {
        OnPropertyChanged(nameof(IsMembership));
        OnPropertyChanged(nameof(IsProduct));
        OnPropertyChanged(nameof(EstimatedTotal));
    }

    partial void OnMethodChanged(PaymentMethod value) => OnPropertyChanged(nameof(IsCardRequired));
    partial void OnSelectedPlanChanged(MembershipTypeDto? value) => OnPropertyChanged(nameof(EstimatedTotal));
    partial void OnSelectedProductChanged(ProductDto? value) => OnPropertyChanged(nameof(EstimatedTotal));
    partial void OnQuantityChanged(decimal value) => OnPropertyChanged(nameof(EstimatedTotal));

    [RelayCommand]
    private async Task ConfirmAsync(CancellationToken ct = default)
    {
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            if (SelectedMember is null) { ErrorMessage = "Elegí un socio."; return; }
            var cashierId = _session.EffectiveCashierId;
            var last4 = IsCardRequired ? CardLast4 : null;

            if (Concept == SaleConcept.Membership)
            {
                if (SelectedPlan is null) { ErrorMessage = "Elegí un plan."; return; }
                await _mediator.Send(new SellMembershipCommand(
                    _session.CompanyId, _session.SiteId, cashierId, _session.OpenShiftId,
                    SelectedMember.Id, SelectedPlan.Id, Method, last4), ct);
            }
            else
            {
                if (SelectedProduct is null) { ErrorMessage = "Elegí un producto."; return; }
                await _mediator.Send(new SellProductCommand(
                    _session.CompanyId, _session.SiteId, cashierId, _session.OpenShiftId,
                    SelectedProduct.Id, Quantity, SelectedMember.Id, Method, last4), ct);
            }

            Registered?.Invoke();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
