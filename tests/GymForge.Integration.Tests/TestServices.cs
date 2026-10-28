using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

/// <summary>Servicios de aplicación armados sobre un DbContext de prueba, como los resuelve el contenedor real.</summary>
internal static class TestServices
{
    public static CompanyCalendar Calendar(GymForgeDbContext db, IClock clock) =>
        new(clock, new SiteRepository(db), NullLogger<CompanyCalendar>.Instance);

    public static ArrearsRelief ArrearsRelief(GymForgeDbContext db, IClock clock) =>
        new(new MemberRepository(db), new ChargeRepository(db), Calendar(db, clock));
}
