using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

public record MembershipTypeDto(
    Guid Id, string Name, decimal Price, string DurationLabel,
    MembershipBasis Basis, bool IsActive)
{
    public static MembershipTypeDto FromEntity(MembershipType m) =>
        new(m.Id, m.Name, m.Price, $"{m.DurationValue} {UnitEs(m.DurationUnit, m.DurationValue)}",
            m.Basis, m.IsActive);

    private static string UnitEs(string unit, int v) => unit switch
    {
        "Day" => v == 1 ? "día" : "días",
        "Year" => v == 1 ? "año" : "años",
        _ => v == 1 ? "mes" : "meses",
    };
}

public record ProductDto(Guid Id, string Sku, string Name, decimal SalePrice)
{
    public static ProductDto FromEntity(Product p) => new(p.Id, p.Sku, p.Name, p.SalePrice);
}

/// <summary>Fila del catálogo de productos con el stock de la sede activa.</summary>
public record ProductRowDto(
    Guid Id, string Sku, string Name, string? Barcode,
    decimal SalePrice, decimal CostPrice,
    decimal StockQty, decimal ReorderPoint, bool IsActive)
{
    public bool IsLowStock => ReorderPoint > 0 && StockQty <= ReorderPoint;

    public static ProductRowDto FromEntity(Product p, StockBySite? stock) =>
        new(p.Id, p.Sku, p.Name, p.Barcode, p.SalePrice, p.CostPrice,
            stock?.Qty ?? 0, stock?.ReorderPoint ?? 0, p.IsActive);
}
