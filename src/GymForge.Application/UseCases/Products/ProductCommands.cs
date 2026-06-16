using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using MediatR;

namespace GymForge.Application.UseCases.Products;

// ── Catálogo con stock de la sede activa (administración) ─────────────────────

public record GetProductsWithStockQuery(Guid CompanyId, Guid SiteId)
    : IRequest<IReadOnlyList<ProductRowDto>>;

public class GetProductsWithStockQueryHandler
    : IRequestHandler<GetProductsWithStockQuery, IReadOnlyList<ProductRowDto>>
{
    private readonly IProductRepository _repo;
    public GetProductsWithStockQueryHandler(IProductRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ProductRowDto>> Handle(GetProductsWithStockQuery q, CancellationToken ct) =>
        (await _repo.GetAllByCompanyAsync(q.CompanyId, ct))
            .Select(p => ProductRowDto.FromEntity(p, p.Stock.FirstOrDefault(s => s.SiteId == q.SiteId)))
            .ToList();
}

// ── Crear producto ────────────────────────────────────────────────────────────

public record CreateProductCommand(
    Guid CompanyId, string Sku, string Name,
    decimal SalePrice, decimal CostPrice, string? Barcode) : IRequest<ProductRowDto>;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(40).WithMessage("El SKU es obligatorio.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre es obligatorio.");
        RuleFor(x => x.SalePrice).GreaterThan(0).WithMessage("El precio de venta debe ser positivo.");
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).WithMessage("El costo no puede ser negativo.");
    }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductRowDto>
{
    private readonly IProductRepository _repo;
    public CreateProductCommandHandler(IProductRepository repo) => _repo = repo;

    public async Task<ProductRowDto> Handle(CreateProductCommand cmd, CancellationToken ct)
    {
        var sku = cmd.Sku.Trim();
        if (await _repo.GetBySkuAsync(cmd.CompanyId, sku, ct) is not null)
            throw new InvalidOperationException($"Ya existe un producto con el SKU «{sku}».");

        var product = Product.Create(cmd.CompanyId, sku, cmd.Name.Trim(), cmd.SalePrice, cmd.CostPrice);
        if (!string.IsNullOrWhiteSpace(cmd.Barcode))
            product.Update(product.Name, product.SalePrice, product.CostPrice, cmd.Barcode.Trim());

        await _repo.AddAsync(product, ct);
        await _repo.SaveChangesAsync(ct);
        return ProductRowDto.FromEntity(product, null);
    }
}

// ── Modificar producto ────────────────────────────────────────────────────────

public record UpdateProductCommand(
    Guid CompanyId, Guid ProductId, string Name,
    decimal SalePrice, decimal CostPrice, string? Barcode) : IRequest<ProductRowDto>;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre es obligatorio.");
        RuleFor(x => x.SalePrice).GreaterThan(0).WithMessage("El precio de venta debe ser positivo.");
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).WithMessage("El costo no puede ser negativo.");
    }
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, ProductRowDto>
{
    private readonly IProductRepository _repo;
    public UpdateProductCommandHandler(IProductRepository repo) => _repo = repo;

    public async Task<ProductRowDto> Handle(UpdateProductCommand cmd, CancellationToken ct)
    {
        var product = await _repo.GetByIdAsync(cmd.ProductId, ct);
        if (product is null || product.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("El producto no existe.");

        product.Update(cmd.Name.Trim(), cmd.SalePrice, cmd.CostPrice,
            string.IsNullOrWhiteSpace(cmd.Barcode) ? null : cmd.Barcode.Trim());
        await _repo.SaveChangesAsync(ct);
        return ProductRowDto.FromEntity(product, null);
    }
}

// ── Activar / desactivar producto ─────────────────────────────────────────────

public record SetProductActiveCommand(Guid CompanyId, Guid ProductId, bool Active) : IRequest;

public class SetProductActiveCommandHandler : IRequestHandler<SetProductActiveCommand>
{
    private readonly IProductRepository _repo;
    public SetProductActiveCommandHandler(IProductRepository repo) => _repo = repo;

    public async Task Handle(SetProductActiveCommand cmd, CancellationToken ct)
    {
        var product = await _repo.GetByIdAsync(cmd.ProductId, ct);
        if (product is null || product.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("El producto no existe.");

        if (cmd.Active) product.Activate();
        else product.Deactivate();
        await _repo.SaveChangesAsync(ct);
    }
}

// ── Ajustar stock de la sede ──────────────────────────────────────────────────

/// <summary>
/// Suma (o resta) unidades al stock del producto en la sede; crea el registro
/// de stock si aún no existe. Devuelve la cantidad resultante.
/// </summary>
public record AdjustStockCommand(
    Guid CompanyId, Guid SiteId, Guid ProductId,
    decimal Delta, decimal? ReorderPoint = null) : IRequest<decimal>;

public class AdjustStockCommandHandler : IRequestHandler<AdjustStockCommand, decimal>
{
    private readonly IProductRepository _repo;
    public AdjustStockCommandHandler(IProductRepository repo) => _repo = repo;

    public async Task<decimal> Handle(AdjustStockCommand cmd, CancellationToken ct)
    {
        var product = await _repo.GetByIdAsync(cmd.ProductId, ct);
        if (product is null || product.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("El producto no existe.");

        var stock = await _repo.GetStockAsync(cmd.ProductId, cmd.SiteId, ct);
        if (stock is null)
        {
            stock = StockBySite.Create(cmd.CompanyId, cmd.ProductId, cmd.SiteId);
            await _repo.AddStockAsync(stock, ct);
        }

        if (cmd.Delta != 0) stock.AdjustStock(cmd.Delta);
        if (cmd.ReorderPoint is { } reorder) stock.SetReorderPoint(reorder);

        await _repo.SaveChangesAsync(ct);
        return stock.Qty;
    }
}
