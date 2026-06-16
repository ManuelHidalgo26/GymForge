using GymForge.Application.UseCases.Classes;
using GymForge.Application.UseCases.Exercises;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Routines;
using GymForge.Application.UseCases.Sales;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Seed;

/// <summary>
/// Datos de demostración para probar la app a mano (socios con membresía activa,
/// una clase con horario + reserva y una rutina armada). Solo corre con la variable
/// de entorno GYMFORGE_SEED_DEMO=1; el DNI propio se pasa por GYMFORGE_DEMO_DNI
/// (no se versiona en el repo). Idempotente: no hace nada si ya hay socios.
/// </summary>
public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var db = sp.GetRequiredService<GymForgeDbContext>();
        var mediator = sp.GetRequiredService<IMediator>();
        var logger = sp.GetRequiredService<ILogger<DatabaseSeeder>>();

        // Idempotente: el seed base no crea socios, así que si ya hay alguno, no re-sembrar.
        if (await db.Members.AnyAsync(ct)) return;

        var company = await db.Companies.FirstOrDefaultAsync(ct);
        var site = await db.Sites.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        var admin = await db.Staff.FirstOrDefaultAsync(ct);
        // SQLite no soporta ORDER BY decimal: ordenar en memoria (plan más barato).
        var plan = (await db.MembershipTypes.Where(m => m.Price > 0).ToListAsync(ct))
            .OrderBy(m => m.Price).FirstOrDefault();
        if (company is null || site is null || admin is null || plan is null) return;

        logger.LogInformation("Seeding demo data (socios, clase, rutina)...");

        // Socios de ejemplo (DNIs ficticios) + el propio si está GYMFORGE_DEMO_DNI.
        var people = new List<(string First, string Last, string Doc, Gender G)>
        {
            ("Lucas", "Fernández", "41222333", Gender.Male),
            ("Sofía", "Martínez", "42333444", Gender.Female),
        };
        var ownDni = Environment.GetEnvironmentVariable("GYMFORGE_DEMO_DNI");
        if (!string.IsNullOrWhiteSpace(ownDni))
            people.Insert(0, ("Socio", "De Prueba", ownDni.Trim(), Gender.Male));

        Guid? firstMemberId = null;
        foreach (var (first, last, doc, g) in people)
        {
            var dto = await mediator.Send(new CreateMemberCommand(
                company.Id, site.Id, first, last, DocumentType.DNI, doc, g,
                $"{first.ToLowerInvariant()}@demo.com", "+54 9 11 5555-0000",
                new DateOnly(1995, 6, 15)), ct);
            await mediator.Send(new SellMembershipCommand(
                company.Id, site.Id, admin.Id, null, dto.Id, plan.Id, PaymentMethod.Cash, null), ct);
            firstMemberId ??= dto.Id;
        }

        if (firstMemberId is not { } memberId) { logger.LogInformation("Demo data: sin socios."); return; }

        // Clase "Funcional" con un horario hoy 18:00 y el primer socio reservado.
        var cls = await mediator.Send(new CreateClassCommand(company.Id, "Funcional", 60, 12), ct);
        var start = DateOnly.FromDateTime(DateTime.Today).ToDateTime(new TimeOnly(18, 0));
        var schedule = await mediator.Send(new CreateScheduleCommand(
            company.Id, site.Id, cls.Id, start, 12), ct);
        await mediator.Send(new BookMemberCommand(company.Id, schedule.Id, memberId), ct);

        // Rutina "Full Body" para el primer socio, con un día y un par de ejercicios.
        var routine = await mediator.Send(new CreateRoutineCommand(
            company.Id, memberId, "Full Body", WorkoutGoal.Hypertrophy, 3), ct);
        await mediator.Send(new AddRoutineDayCommand(company.Id, routine.Id, "Tren superior"), ct);
        var detail = await mediator.Send(new GetRoutineDetailQuery(company.Id, routine.Id), ct);
        var exercises = await mediator.Send(new SearchExercisesQuery(null, null), ct);
        if (detail is { Days.Count: > 0 } && exercises.Count >= 2)
        {
            var dayId = detail.Days[0].Id;
            await mediator.Send(new AddRoutineItemCommand(company.Id, dayId, exercises[0].Id, 4, 8, 12), ct);
            await mediator.Send(new AddRoutineItemCommand(company.Id, dayId, exercises[1].Id, 3, 10, 12), ct);
        }

        logger.LogInformation("Demo data seeded: {Count} socios + clase + rutina", people.Count);
    }
}
