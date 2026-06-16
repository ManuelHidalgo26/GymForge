using FluentAssertions;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Cash;
using GymForge.Application.UseCases.Sales;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Venta de producto a consumidor final (sin socio) con caja abierta, contra SQLite
/// real: persiste la venta, el pago vinculado (SaleId), el movimiento de caja y
/// descuenta stock. Cubre el flujo POS de la Fase 3 de punta a punta.
/// </summary>
public class SellProductFlowIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private Guid _companyId;
    private Guid _siteId;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new GymForgeDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();

        var company = Company.Create("Test Gym", "30-12345678-9");
        await _db.Companies.AddAsync(company);
        var site = Site.Create(company.Id, "Sede", "Calle 1");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();

        _companyId = company.Id;
        _siteId = site.Id;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task SellProduct_ToWalkIn_WithOpenShift_PersistsSalePaymentAndCashMovement()
    {
        var product = Product.Create(_companyId, "AGUA-500", "Agua 500ml", 1_500m);
        await _db.Products.AddAsync(product);
        await _db.StockBySite.AddAsync(StockBySite.Create(_companyId, product.Id, _siteId, 10));
        await _db.SaveChangesAsync();

        var shiftRepo = new ShiftRepository(_db);
        var shift = await new OpenShiftCommandHandler(shiftRepo).Handle(
            new OpenShiftCommand(_companyId, _siteId, Guid.NewGuid(), 0m), CancellationToken.None);

        var handler = new SellProductCommandHandler(
            new ProductRepository(_db), new SaleRepository(_db),
            new PaymentRepository(_db), new CashRegister(shiftRepo));

        var dto = await handler.Handle(new SellProductCommand(
            _companyId, _siteId, Guid.NewGuid(), shift.Id,
            product.Id, Quantity: 2, MemberId: null, PaymentMethod.Cash), CancellationToken.None);

        // 1500 * 2 = 3000 + IVA 21% (630) = 3630
        dto.Amount.Should().Be(3_630m);
        dto.MemberId.Should().BeNull();

        // Releer desde la DB: todo persistido de verdad.
        _db.ChangeTracker.Clear();

        var sale = await _db.Sales.Include(s => s.Lines).SingleAsync();
        sale.MemberId.Should().BeNull();
        sale.Lines.Should().ContainSingle(l => l.Description == "Agua 500ml" && l.Quantity == 2);

        var payment = await _db.Payments.SingleAsync();
        payment.MemberId.Should().BeNull();
        payment.SaleId.Should().Be(sale.Id);
        payment.Amount.Should().Be(3_630m);

        var stock = await _db.StockBySite.SingleAsync(s => s.ProductId == product.Id);
        stock.Qty.Should().Be(8);

        var movements = await _db.CashMovements.ToListAsync();
        movements.Should().ContainSingle(m =>
            m.Type == CashMovementType.Income &&
            m.Category == CashMovementCategory.Sale &&
            m.Amount == 3_630m);
    }
}
