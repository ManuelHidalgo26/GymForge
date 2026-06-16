using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Cash;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Ciclo de caja completo contra SQLite real (handlers + EF). Regresión del
/// DbUpdateConcurrencyException: Update(shift) marcaba el movimiento nuevo
/// (Id pre-asignado por BaseEntity) como Modified y el UPDATE afectaba 0 filas.
/// </summary>
public class CashFlowIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private ShiftRepository _shiftRepo = null!;
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
        _shiftRepo = new ShiftRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task FullCashCycle_OpenAddMovementsClose_PersistsEverything()
    {
        // Abrir caja
        var shift = await new OpenShiftCommandHandler(_shiftRepo).Handle(
            new OpenShiftCommand(_companyId, _siteId, Guid.NewGuid(), 5_000m), CancellationToken.None);

        // Agregar ingreso y egreso (acá explotaba el DbUpdateConcurrencyException)
        var addHandler = new AddCashMovementCommandHandler(_shiftRepo);
        await addHandler.Handle(new AddCashMovementCommand(
            shift.Id, CashMovementType.Income, CashMovementCategory.Sale, 12_000m, "Venta"), CancellationToken.None);
        var afterExpense = await addHandler.Handle(new AddCashMovementCommand(
            shift.Id, CashMovementType.Expense, CashMovementCategory.PettyCash, 3_000m, "Limpieza"), CancellationToken.None);

        afterExpense.ExpectedCash.Should().Be(14_000m); // 5000 + 12000 - 3000

        // Cobro en efectivo vía CashRegister (mismo camino que un pago real)
        var cashRegister = new CashRegister(_shiftRepo);
        await cashRegister.PostIfCashAsync(
            PaymentMethod.Cash, shift.Id, CashMovementCategory.Membership, 35_000m, null);
        await _shiftRepo.SaveChangesAsync();

        // Cerrar con arqueo
        var closed = await new CloseShiftCommandHandler(_shiftRepo, Substitute.For<IDatabaseBackup>()).Handle(
            new CloseShiftCommand(shift.Id, DeclaredCash: 48_500m, "Arqueo"), CancellationToken.None);

        closed.Status.Should().Be(ShiftStatus.Closed);
        closed.ExpectedCash.Should().Be(49_000m);
        closed.Difference.Should().Be(-500m);

        // Releer desde la DB: todo persistido de verdad.
        _db.ChangeTracker.Clear();
        (await _db.CashMovements.CountAsync()).Should().Be(3);
        var reloaded = await _db.Shifts.Include(s => s.Movements).SingleAsync();
        reloaded.Status.Should().Be(ShiftStatus.Closed);
        reloaded.Movements.Should().HaveCount(3);
    }
}
