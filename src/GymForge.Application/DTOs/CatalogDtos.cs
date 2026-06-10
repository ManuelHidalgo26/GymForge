using GymForge.Domain.Entities;

namespace GymForge.Application.DTOs;

public record MembershipTypeDto(Guid Id, string Name, decimal Price, string DurationLabel)
{
    public static MembershipTypeDto FromEntity(MembershipType m) =>
        new(m.Id, m.Name, m.Price, $"{m.DurationValue} {m.DurationUnit}");
}

public record ProductDto(Guid Id, string Sku, string Name, decimal SalePrice)
{
    public static ProductDto FromEntity(Product p) => new(p.Id, p.Sku, p.Name, p.SalePrice);
}
