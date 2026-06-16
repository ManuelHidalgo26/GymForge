using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Products;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Products;

/// <summary>
/// Catálogo de productos con stock por sede: ABM + ajuste de stock con punto
/// de reposición. La venta se hace desde Caja (Registrar cobro / venta).
/// </summary>
public partial class ProductsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private Guid? _editingId;       // null = creando
    private ProductRowDto? _stockTarget;

    [ObservableProperty] private ObservableCollection<ProductRowDto> _products = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Formulario alta/edición de producto
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private string _formTitle = "Nuevo producto";
    [ObservableProperty] private string _sku = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private decimal? _salePrice;
    [ObservableProperty] private decimal? _costPrice;
    [ObservableProperty] private string? _barcode;
    [ObservableProperty] private bool _isSkuEditable = true;

    // Formulario de ajuste de stock
    [ObservableProperty] private bool _isStockFormOpen;
    [ObservableProperty] private string _stockFormTitle = string.Empty;
    [ObservableProperty] private decimal _stockDelta;
    [ObservableProperty] private decimal _reorderPoint;

    public bool IsEmpty => !IsLoading && Products.Count == 0;

    public ProductsViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

    partial void OnProductsChanged(ObservableCollection<ProductRowDto> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            Products = new ObservableCollection<ProductRowDto>(
                await _mediator.Send(new GetProductsWithStockQuery(_session.CompanyId, _session.SiteId), ct));
        }
        finally { IsLoading = false; }
    }

    // ── ABM de producto ─────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenCreateForm()
    {
        _editingId = null;
        FormTitle = "Nuevo producto";
        Sku = string.Empty;
        Name = string.Empty;
        SalePrice = null;
        CostPrice = null;
        Barcode = null;
        IsSkuEditable = true;
        ErrorMessage = null;
        IsStockFormOpen = false;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void EditProduct(ProductRowDto? dto)
    {
        if (dto is null) return;
        _editingId = dto.Id;
        FormTitle = $"Modificar: {dto.Name}";
        Sku = dto.Sku;
        Name = dto.Name;
        SalePrice = dto.SalePrice;
        CostPrice = dto.CostPrice;
        Barcode = dto.Barcode;
        IsSkuEditable = false;  // el SKU identifica al producto; no se cambia
        ErrorMessage = null;
        IsStockFormOpen = false;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelForm() => IsFormOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        try
        {
            if (_editingId is { } id)
                await _mediator.Send(new UpdateProductCommand(
                    _session.CompanyId, id, Name, SalePrice ?? 0, CostPrice ?? 0, Barcode));
            else
                await _mediator.Send(new CreateProductCommand(
                    _session.CompanyId, Sku, Name, SalePrice ?? 0, CostPrice ?? 0, Barcode));

            IsFormOpen = false;
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(ProductRowDto? dto)
    {
        if (dto is null) return;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new SetProductActiveCommand(_session.CompanyId, dto.Id, !dto.IsActive));
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ── Ajuste de stock ─────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenStockForm(ProductRowDto? dto)
    {
        if (dto is null) return;
        _stockTarget = dto;
        StockFormTitle = $"Stock de {dto.Name} — hoy: {dto.StockQty:0.##}";
        StockDelta = 0;
        ReorderPoint = dto.ReorderPoint;
        ErrorMessage = null;
        IsFormOpen = false;
        IsStockFormOpen = true;
    }

    [RelayCommand]
    private void CancelStockForm() => IsStockFormOpen = false;

    [RelayCommand]
    private async Task ApplyStockAsync()
    {
        if (_stockTarget is null) return;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new AdjustStockCommand(
                _session.CompanyId, _session.SiteId, _stockTarget.Id, StockDelta, ReorderPoint));
            IsStockFormOpen = false;
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
