using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Catalog;

/// <summary>
/// Resuelve el producto a la venta cuyo código de barras es el que leyó el scanner. <c>null</c> si no hay
/// ninguno (la caja prueba entonces si el código es de un socio).
/// </summary>
public record FindProductByBarcodeQuery(Guid CompanyId, string Barcode) : IRequest<ProductDto?>;

public class FindProductByBarcodeQueryHandler : IRequestHandler<FindProductByBarcodeQuery, ProductDto?>
{
    private readonly IProductRepository _repo;
    public FindProductByBarcodeQueryHandler(IProductRepository repo) => _repo = repo;

    public async Task<ProductDto?> Handle(FindProductByBarcodeQuery q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.Barcode)) return null;

        var product = await _repo.GetByBarcodeAsync(q.CompanyId, q.Barcode.Trim(), ct);
        return product is null ? null : ProductDto.FromEntity(product);
    }
}
