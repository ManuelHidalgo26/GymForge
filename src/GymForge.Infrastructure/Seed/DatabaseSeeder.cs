using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Seed;

public class DatabaseSeeder
{
    private readonly GymForgeDbContext _db;
    private readonly ILogger<DatabaseSeeder> _logger;
    private readonly IPinHasher _pinHasher;

    public DatabaseSeeder(GymForgeDbContext db, ILogger<DatabaseSeeder> logger, IPinHasher pinHasher)
    {
        _db = db;
        _logger = logger;
        _pinHasher = pinHasher;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedExercisesAsync(ct);
        await SeedDefaultCompanyAsync(ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedExercisesAsync(CancellationToken ct)
    {
        if (await _db.Exercises.AnyAsync(ct)) return;

        _logger.LogInformation("Seeding global exercise library...");
        var exercises = ExerciseSeed.GetGlobalExercises();
        await _db.Exercises.AddRangeAsync(exercises, ct);
        _logger.LogInformation("Seeded {Count} exercises", exercises.Count);
    }

    private async Task SeedDefaultCompanyAsync(CancellationToken ct)
    {
        if (await _db.Companies.AnyAsync(ct)) return;

        _logger.LogInformation("Seeding default company and site...");

        var company = Company.Create("Mi Gimnasio S.A.", "20-12345678-9");
        await _db.Companies.AddAsync(company, ct);

        var site = Site.Create(company.Id, "Sede Central", "Av. Corrientes 1234, CABA");
        var siteNorte = Site.Create(company.Id, "Sede Norte", "Av. Cabildo 2500, CABA");
        await _db.Sites.AddRangeAsync([site, siteNorte], ct);

        // Admin staff — PIN: 1234 (hash PBKDF2 real, verificable en el login)
        var admin = Staff.Create(
            company.Id,
            "Admin",
            "GymForge",
            StaffRole.Admin,
            _pinHasher.Hash("1234"));
        await _db.Staff.AddAsync(admin, ct);

        // Membership types
        var mensual = MembershipType.Create(company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var trimestral = MembershipType.Create(company.Id, "Trimestral", MembershipBasis.Renewal, 90_000m, 3, "Month");
        var anual = MembershipType.Create(company.Id, "Anual", MembershipBasis.Renewal, 320_000m, 12, "Month");
        var visitas10 = MembershipType.Create(company.Id, "Pack 10 Visitas", MembershipBasis.VisitPack, 28_000m);
        var trial = MembershipType.Create(company.Id, "Trial 7 días", MembershipBasis.Trial, 0m, 7, "Day");

        await _db.MembershipTypes.AddRangeAsync([mensual, trimestral, anual, visitas10, trial], ct);

        _logger.LogInformation("Default company, site, staff and 5 membership types seeded");
    }
}
