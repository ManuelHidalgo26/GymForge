using FluentAssertions;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GymForge.Integration.Tests;

/// <summary>
/// Aplica las migraciones reales (lo que hace el arranque de la app) en vez de EnsureCreated,
/// para detectar un schema migrado distinto del modelo.
/// </summary>
public class MigrationsIntegrationTests
{
    private const string MigrationBeforeFreezePolicy = "20261027171441_AccessLogAllowUnknownMember";
    private const string MigrationBeforeDunning = "20261027212030_AddMemberCreditLedger";
    private const string MigrationBeforeSaleLineCommission = "20261029180013_AddMemberTagSerialUniqueIndex";
    private const string MigrationBeforeSiteScopedEntities = "20261104142027_AddSaleLineCommissionAmount";

    private static async Task<GymForgeDbContext> NewDbAsync()
    {
        var options = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var db = new GymForgeDbContext(options, new AmbientTenantProvider());
        await db.Database.OpenConnectionAsync();
        return db;
    }

    private static async Task<GymForgeDbContext> NewMigratedDbAsync()
    {
        var db = await NewDbAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task Migrate_LeavesNoPendingModelChanges()
    {
        await using var db = await NewMigratedDbAsync();

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "el snapshot de migraciones debe reflejar el modelo; si no, MigrateAsync falla en el arranque");
    }

    [Fact]
    public async Task Migrate_AccessLogsAcceptSwipesWithoutMember()
    {
        await using var db = await NewMigratedDbAsync();
        var log = AccessLog.DeniedUnknown(
            Guid.NewGuid(), Guid.NewGuid(), 1, AccessMethod.RfidCard, AccessDenialReason.TagUnknown, "TAG-404");

        await db.AccessLogs.AddAsync(log);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.AccessLogs.IgnoreQueryFilters().SingleAsync();
        persisted.MemberId.Should().BeNull();
    }

    [Fact]
    public async Task Migrate_CompaniesCreatedBeforeTheFreezePolicyGetTheDefaultCap()
    {
        await using var db = await NewDbAsync();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeFreezePolicy);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Companies (Id, LegalName, TaxId, PrimaryLanguage, Currency, Timezone, BrandColorHex, IsActive, CreatedAt, UpdatedAt)
            VALUES ('11111111-1111-1111-1111-111111111111', 'Gym Previo', '30-11111111-1', 'es-AR', 'ARS',
                    'Argentina Standard Time', '#6366F1', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')
            """);

        await db.Database.MigrateAsync();

        var cap = await db.Companies.Select(c => c.MaxFreezeDaysPerYear).SingleAsync();
        cap.Should().Be(90, "un gimnasio ya instalado conserva la política de 90 días, no queda en 0");
    }

    [Fact]
    public async Task Migrate_CompaniesCreatedBeforeTheDunningConfigKeepNullSoTheyFallToFactoryDefaults()
    {
        await using var db = await NewDbAsync();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeDunning);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Companies (Id, LegalName, TaxId, PrimaryLanguage, Currency, Timezone, BrandColorHex, IsActive, CreatedAt, UpdatedAt)
            VALUES ('11111111-1111-1111-1111-111111111111', 'Gym Previo', '30-11111111-1', 'es-AR', 'ARS',
                    'Argentina Standard Time', '#6366F1', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')
            """);

        await db.Database.MigrateAsync();

        var company = await db.Companies.SingleAsync();
        company.DunningJson.Should().BeNull();
        DunningSettings.FromJson(company.DunningJson).Should().BeNull("el llamador conserva los defaults");
    }

    private async Task<string?> SaleLineCommissionColumnAsync(GymForgeDbContext db)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('SaleLines') WHERE name = 'CommissionAmount'";
        return (string?)await command.ExecuteScalarAsync();
    }

    [Fact]
    public async Task Migrate_SaleLinesCreatedBeforeTheCommissionColumnGetZero_AndTheMigrationGoesDownCleanly()
    {
        await using var db = await NewDbAsync();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeSaleLineCommission);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");   // la línea de prueba no necesita su venta
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO SaleLines (Id, SaleId, Description, Quantity, UnitPrice, Discount, TaxRate, CreatedAt, UpdatedAt)
            VALUES ('22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333',
                    'Agua', 2, 1500, 0, 0.21, '2026-01-01 00:00:00', '2026-01-01 00:00:00')
            """);
        (await SaleLineCommissionColumnAsync(db)).Should().BeNull("todavía no existe antes de la migración");

        await db.Database.MigrateAsync();

        (await SaleLineCommissionColumnAsync(db)).Should().Be("CommissionAmount");
        var amount = await db.Database.SqlQueryRaw<decimal>("SELECT CommissionAmount AS Value FROM SaleLines").SingleAsync();
        amount.Should().Be(0m, "las ventas anteriores no tenían comisión");

        // El Down de SiteId reconstruye tablas y reactiva las FK: se apaga de nuevo para la línea huérfana.
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeSiteScopedEntities);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeSaleLineCommission);

        (await SaleLineCommissionColumnAsync(db)).Should().BeNull("el Down quita la columna");
        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM SaleLines").SingleAsync())
            .Should().Be(1, "bajar la migración conserva las líneas");
    }

    [Fact]
    public async Task Migrate_BackfillsSiteIdFromTheMemberOrTheClassSchedule_AndTheMigrationGoesDownCleanly()
    {
        var siteId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
        var companyId = "CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC";
        var memberId = "11111111-1111-1111-1111-111111111111";
        var scheduleId = "22222222-2222-2222-2222-222222222222";
        await using var db = await NewDbAsync();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeSiteScopedEntities);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await SeedPreSiteIdRowsAsync(db, companyId, siteId, memberId, scheduleId);

        await db.Database.MigrateAsync();

        foreach (var table in new[] { "Routines", "WorkoutLogs", "BodyMeasurements", "Bookings" })
        {
            var sites = await db.Database
                .SqlQueryRaw<string>($"SELECT \"SiteId\" AS Value FROM \"{table}\"").ToListAsync();
            sites.Should().ContainSingle().Which.Should().Be(siteId, $"{table} hereda la sede de su padre");
        }

        await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeSiteScopedEntities);

        (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Routines").SingleAsync())
            .Should().Be(1, "bajar la migración conserva las filas");
    }

    private static async Task SeedPreSiteIdRowsAsync(
        GymForgeDbContext db, string companyId, string siteId, string memberId, string scheduleId)
    {
        const string audit = "'2026-01-01 00:00:00', '2026-01-01 00:00:00'";
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO Members (Id, CompanyId, SiteId, FirstName, LastName, DocumentType, DocumentNumber, Gender,
                                 Status, BloodType, Source, MarketingConsent, CreatedAt, UpdatedAt)
            VALUES ('{memberId}', '{companyId}', '{siteId}', 'Ana', 'Lopez', 'DNI', '30111222', 'Female',
                    'Active', 'Unknown', 'WalkIn', 0, {audit});
            INSERT INTO ClassSchedules (Id, CompanyId, SiteId, ClassDescriptionId, StartDatetime, EndDatetime, Capacity,
                                        WaitlistCapacity, IsRecurring, BookingCloseBeforeMin, CancelDeadlineMin,
                                        IsCancelled, CreatedAt, UpdatedAt)
            VALUES ('{scheduleId}', '{companyId}', '{siteId}', '33333333-3333-3333-3333-333333333333',
                    '2026-06-17 18:00:00', '2026-06-17 19:00:00', 10, 5, 0, 30, 120, 0, {audit});
            INSERT INTO Routines (Id, CompanyId, MemberId, Name, Goal, StartDate, FrequencyPerWeek, Difficulty, IsActive,
                                  CreatedAt, UpdatedAt)
            VALUES ('44444444-4444-4444-4444-444444444444', '{companyId}', '{memberId}', 'Full', 'Strength',
                    '2026-06-17', 3, 3, 1, {audit});
            INSERT INTO WorkoutLogs (Id, CompanyId, MemberId, RoutineItemId, PerformedAt, ActualSetsJson, ActualRepsJson,
                                     ActualWeightKgJson, ActualDurationSecJson, CreatedAt, UpdatedAt)
            VALUES ('55555555-5555-5555-5555-555555555555', '{companyId}', '{memberId}',
                    '66666666-6666-6666-6666-666666666666', '2026-06-17 18:00:00', '[]', '[]', '[]', '[]', {audit});
            INSERT INTO BodyMeasurements (Id, CompanyId, MemberId, MeasuredAt, Method, CreatedAt, UpdatedAt)
            VALUES ('77777777-7777-7777-7777-777777777777', '{companyId}', '{memberId}', '2026-06-17 18:00:00',
                    'Manual', {audit});
            INSERT INTO Bookings (Id, CompanyId, MemberId, ClassScheduleId, BookingType, Status, BookingChannel,
                                  BookedAt, CreatedAt, UpdatedAt)
            VALUES ('88888888-8888-8888-8888-888888888888', '{companyId}', '{memberId}', '{scheduleId}', 'Regular',
                    'Booked', 'FrontDesk', '2026-06-17 10:00:00', {audit});
            """);
    }

    [Fact]
    public async Task Migrate_DunningConfigAndNotificationLogsPersistAndReload()
    {
        await using var db = await NewMigratedDbAsync();
        var company = Company.Create("Gym", "30-11111111-1");
        var site = Site.Create(company.Id, "Centro", "Calle 1");
        var member = Member.Create(company.Id, site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        var settings = new DunningSettings(true, [new DunningStageSettings(5, "Propia", "Hola {nombre}")]);
        company.SetDunningConfig(settings.ToJson());
        var log = NotificationLog.Manual(
            company.Id, site.Id, member.Id, null, NotificationChannel.WhatsApp, null, "+5491122334455");
        await db.Companies.AddAsync(company);
        await db.Sites.AddAsync(site);
        await db.Members.AddAsync(member);
        await db.NotificationLogs.AddAsync(log);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var reloaded = await db.Companies.SingleAsync();
        DunningSettings.FromJson(reloaded.DunningJson)!.Stages.Should().Equal(settings.Stages);
        (await db.NotificationLogs.IgnoreQueryFilters().SingleAsync()).Id.Should().Be(log.Id);
    }
}
