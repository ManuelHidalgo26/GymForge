# ADR-003 — Estado automático, auditoría (AuditLog) y cuenta corriente del socio

**Status:** Accepted
**Date:** 2026-10-27
**Deciders:** arqui (planner) · implementa `dev`
**Sesión:** 2 — Fase 0 parte 2 ("estado automático, auditoría y cuenta corriente")

---

## Context

Fase 0 parte 2. Cuatro agujeros de correctitud/negocio detectados sobre el core ya saneado en la
Sesión 1 (ADR-002). Alcance acotado a estos 4 puntos; nada más entra. Esta es la guía de
implementación para `dev`: no inventar arquitectura, seguir las firmas y el orden de abajo.

Hechos del código verificados al 2026-10-27 (para que el enfoque sea exacto):

- **Scheduler actual.** `DunningStartup.RunDailyAsync` (`src/GymForge.Desktop/Services/DunningStartup.cs`)
  corre una vez al abrir la app, con marcador `%LOCALAPPDATA%\GymForge\dunning-last-run.txt`. **No hace
  catch-up** de días perdidos y **solo** dispara dunning: nadie vence membresías ni marca mora. Se invoca
  desde `src/GymForge.Desktop/Program.cs:64`. La app **no usa Generic Host**: es
  `new ServiceCollection().BuildServiceProvider()` + Avalonia `StartWithClassicDesktopLifetime` (no hay
  runtime de `IHostedService`).
- **Métodos de dominio que YA existen:** `Membership.Expire()` (`Membership.cs:123`),
  `Charge.MarkOverdue()` (`Charge.cs:72`), `Member.MarkOverdue()` (`Member.cs:150`),
  `Membership.Freeze(start,end,reason,maxFreezeDaysPerYear=90)` (`Membership.cs:81`) y
  `Membership.Unfreeze()` (`Membership.cs:103`). Los tres primeros son idempotentes (guardas de estado).
- **Freeze roto.** `Membership.Freeze` recibe `maxFreezeDaysPerYear` pero **nunca lo valida**: suma
  `FreezeCountUsed += requestedDays` y extiende `EndDate`, sin tope y sin ventana anual.
  `Company` **no** tiene campo de tope. No existe `UnfreezeMembershipCommand`.
- **AuditLog muerto.** `AuditLog.Create(...)` (`src/GymForge.Domain/Entities/AuditLog.cs:18`) **no tiene
  ningún call site**. La tabla `AuditLogs` y su config (`AccessLogConfiguration.cs:33`) ya existen en el
  schema (migración `InitialCreate`), así que **el punto 2 no necesita migración**. `WorkoutLog` está
  declarado en el mismo archivo `AuditLog.cs` (lo movemos: ver más abajo, es deuda D3 anotada).
- **Excedente de pago perdido.** `ProcessPaymentCommandHandler` (`ProcessPaymentCommand.cs:83`) asigna
  oldest-first y, si `remaining > 0`, **lo descarta**. No hay saldo a favor.
- **Infra de tenant (Sesión 1, reutilizar tal cual):** `ICurrentTenant`
  (`src/GymForge.Application/Interfaces/ICurrentTenant.cs`) con `BeginScope(company, site)` sobre
  `AsyncLocal` (`ScopedTenantProvider`); `SessionTenantProvider` (Desktop, Singleton),
  `AmbientTenantProvider` (Infra/API/tests). Query filters globales por marker `ITenantScoped`/`ISiteScoped`
  en `GymForgeDbContext.ApplyTenantFilters`. Bypass legítimo = `IgnoreQueryFilters()` + CompanyId explícito
  (así ya funcionan `ChargeRepository.GetOverdueAsync` y `MembershipRepository.GetExpiringAsync`).
- **Staff logueado.** No hay `ICurrentUser`. El cajero vivo está en
  `SessionContext.CashierId` (Guid?, Desktop Singleton). `DefaultStaffId`/`EffectiveCashierId` son el
  fallback admin. `IClock`/`SystemClock` (`Now`/`Today` en **UTC**) ya existen.
- **DbContext scoped**, repos scoped, comparten el mismo `GymForgeDbContext` por scope → un `SaveChanges`
  persiste todo lo trackeado en una transacción (igual que en ADR-002). `AddDbContext` se configura en
  `InfrastructureServiceExtensions.AddInfrastructure`.

### Resumen: dominio que ya existe vs. lo nuevo

| Punto | Ya existe | Hay que crear |
|---|---|---|
| 1 Scheduler | `Membership.Expire`, `Charge.MarkOverdue`, `Member.MarkOverdue`, `DunningService.RunAsync`, `ChargeRepository.GetOverdueAsync` | `IMembershipRepository.GetActiveExpiredAsync`, `IDailyMaintenanceRunner` + `DailyMaintenanceRunner`, `DailyMaintenanceService`, `IMaintenanceCheckpoint` + `FileMaintenanceCheckpoint`, `DailyMaintenanceHost` (Desktop). Borrar `DunningStartup`. |
| 2 AuditLog | `AuditLog.Create`, `AuditLogConfiguration`, tabla `AuditLogs` | `IAuditable` (marker Domain), `ICurrentUser` + `AmbientCurrentUser`/`SessionCurrentUser`, `AuditSaveChangesInterceptor` + registro en `AddDbContext`. **Sin migración.** |
| 3 Freeze | `Membership.Freeze` (param ignorado), `Membership.Unfreeze()`, `FreezeMembershipCommand` | Validación real del tope en `Membership.Freeze`; campos `Membership.FreezeDaysUsedYear`/`FreezeYear`; `Company.MaxFreezeDaysPerYear`; cambiar `Unfreeze()` → `Unfreeze(DateOnly today)`; `UnfreezeMembershipCommand` + handler. **1 migración.** |
| 4 Cuenta corriente | `Charge.ApplyPayment`, `ProcessPaymentCommand` | `MemberCreditMovement` (entidad + enum `CreditMovementType`), `IMemberCreditRepository` + repo + config, enganche en `ProcessPaymentCommand`, `ApplyMemberCreditCommand`, `GetMemberCreditBalanceQuery`. **1 migración.** |

**Total: 2 migraciones nuevas** (punto 3 y punto 4). Puntos 1 y 2 no tocan schema.

---

## 1. Scheduler diario con catch-up (estado automático)

**Problema.** El estado no cambia solo. Membresías vencidas quedan `Active`, cargos vencidos no pasan a
`Overdue`, socios morosos no quedan `Overdue`, y el dunning solo corre si la app abre justo el día exacto de
una etapa. Siendo desktop offline-first, si el gym estuvo cerrado 3 días hay que **ponerse al día** esos días
al reabrir (sobre todo el dunning, que dispara en el día exacto de cada etapa).

**Decisión.**

**No usar `IHostedService`/`BackgroundService`.** La app no tiene Generic Host; meterlo sería deuda/complejidad
evitable (Simplicidad > cleverness). En su lugar: **catch-up al arrancar + un `PeriodicTimer`** liviano que
re-chequea cada pocas horas mientras la app está abierta (cubre la PC que queda prendida y cruza la medianoche
sin reiniciar). Todo **best-effort**: envuelto en try/catch + Serilog, **nunca** tira la recepción.

**Idempotencia y catch-up.** Un único **checkpoint** con la última fecha **local** procesada. La ventana a
procesar es `[ultimoProcesado + 1 día .. hoyLocal]`, acotada a `MaxCatchUpDays = 35` (evita backfill patológico
y resends de dunning sin fin). **Primer arranque** (sin checkpoint): se procesa **solo `hoyLocal`** (no se
retro-procesa el historial). Correr dos veces el mismo día: la ventana queda vacía → no-op. Los tres pasos son
idempotentes por guardas de estado, así que reprocesar un día no duplica.

**Zona horaria.** El "día" es el **local del gimnasio**, no UTC. `IClock.Now` es UTC; el runner convierte con
`TimeZoneInfo.FindSystemTimeZoneById(company.Timezone)` (`Company.Timezone` = "Argentina Standard Time").
Importa: usar la fecha UTC haría rodar el día a las 21:00 AR y el dunning dispararía etapas en el día equivocado.
Si la TZ no resuelve en la máquina, fallback a `_clock.Today` y log de warning.

**Multi-tenant.** El runner recorre **todas las empresas** (`ISiteRepository.GetCompaniesAsync`, que ya usa
`IgnoreQueryFilters`). Por empresa/día abre `_tenant.BeginScope(company.Id, Guid.Empty)` de forma defensiva; las
lecturas de mora/vencimiento son company-wide vía `IgnoreQueryFilters` (patrón de ADR-002), y el `DunningService`
ya abre scope por la sede del cargo internamente. Desktop es efectivamente 1 empresa; el checkpoint es global
(se documenta como límite para multi-empresa: usa la misma ventana para todas).

**Orden por empresa/día (en `DailyMaintenanceService.RunForCompanyDayAsync`):**
  a. **Vencer membresías**: `GetActiveExpiredAsync(companyId, day)` → `ms.Expire()` + `Update`. (No se toca el
     estado del socio: puede tener otra membresía activa. Queda fuera de alcance.)
  b. **Marcar mora**: `GetOverdueAsync(companyId, day)` (ya incluye `Member`) → por cada cargo con
     `AmountOutstanding > 0`: `charge.MarkOverdue()` + `charge.Member?.MarkOverdue()` + `Update`.
  c. **Dunning**: `_dunning.RunAsync(companyId, day, company.LegalName, ct)`.

**Firmas / archivos nuevos.**

```csharp
// src/GymForge.Application/Interfaces/IMaintenanceCheckpoint.cs
namespace GymForge.Application.Interfaces;
/// <summary>Última fecha LOCAL en que corrió el mantenimiento diario. Persistente entre arranques.</summary>
public interface IMaintenanceCheckpoint
{
    DateOnly? ReadLastRun();
    void WriteLastRun(DateOnly localDay);
}

// src/GymForge.Application/Interfaces/IDailyMaintenanceRunner.cs
namespace GymForge.Application.Interfaces;
/// <summary>Orquesta el catch-up: recorre empresas y días pendientes. Best-effort, idempotente.</summary>
public interface IDailyMaintenanceRunner
{
    Task RunPendingAsync(CancellationToken ct = default);
}
```

```csharp
// src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceService.cs
namespace GymForge.Application.UseCases.Maintenance;

/// <summary>Mantenimiento de UNA empresa en UN día local: vence, marca mora y dispara dunning.</summary>
public sealed class DailyMaintenanceService
{
    // ctor: IMembershipRepository, IChargeRepository, DunningService, ICurrentTenant
    public async Task<MaintenanceDayResult> RunForCompanyDayAsync(Company company, DateOnly localDay, CancellationToken ct = default);
}

public readonly record struct MaintenanceDayResult(int Expired, int MarkedOverdue, int DunningSent);
```

```csharp
// src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceRunner.cs
namespace GymForge.Application.UseCases.Maintenance;

public sealed class DailyMaintenanceRunner : IDailyMaintenanceRunner
{
    private const int MaxCatchUpDays = 35;
    // ctor: ISiteRepository sites, DailyMaintenanceService maintenance,
    //       IMaintenanceCheckpoint checkpoint, IClock clock, ILogger<DailyMaintenanceRunner> log
    public async Task RunPendingAsync(CancellationToken ct = default)
    {
        // last = checkpoint.ReadLastRun();
        // foreach company in await sites.GetCompaniesAsync(ct):
        //     tz = ResolveTimeZone(company.Timezone)  // try/catch → fallback clock.Today, log warning
        //     localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.Now, tz))
        //     start = last is null ? localToday : last.Value.AddDays(1);
        //     if (start > localToday) continue;
        //     if (start < localToday.AddDays(-MaxCatchUpDays)) start = localToday.AddDays(-MaxCatchUpDays);
        //     for (var day = start; day <= localToday; day = day.AddDays(1))
        //         await maintenance.RunForCompanyDayAsync(company, day, ct);  // try/catch por empresa
        //     maxLocalToday = Max(maxLocalToday, localToday);
        // if (maxLocalToday != default) checkpoint.WriteLastRun(maxLocalToday);
    }
}
```

```csharp
// src/GymForge.Infrastructure/Services/FileMaintenanceCheckpoint.cs
// Singleton. Reemplaza el marcador de DunningStartup.
// Archivo: %LOCALAPPDATA%\GymForge\maintenance-last-run.txt  (formato yyyy-MM-dd, InvariantCulture).
public sealed class FileMaintenanceCheckpoint : IMaintenanceCheckpoint { /* try/catch best-effort */ }
```

```csharp
// src/GymForge.Desktop/Services/DailyMaintenanceHost.cs  (reemplaza DunningStartup)
// Singleton. Arranca el catch-up + PeriodicTimer. NUNCA lanza.
public sealed class DailyMaintenanceHost
{
    // ctor: IServiceScopeFactory scopeFactory, SessionContext session
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public void Start();  // fire-and-forget: 1 corrida ya + loop PeriodicTimer(Interval)
    private async Task RunOnceAsync()
    {
        try
        {
            if (session.CompanyId == Guid.Empty) return;     // onboarding pendiente
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDailyMaintenanceRunner>().RunPendingAsync();
        }
        catch (Exception ex) { Log.Warning(ex, "Mantenimiento diario: corrida best-effort falló"); }
    }
}
```

**Repo nuevo (Infrastructure).**
```csharp
// IMembershipRepository (src/GymForge.Application/Interfaces/IRepositories.cs)
/// <summary>Membresías activas de la empresa (todas las sedes) ya vencidas a la fecha. Company-wide.</summary>
Task<IReadOnlyList<Membership>> GetActiveExpiredAsync(Guid companyId, DateOnly asOf, CancellationToken ct = default);

// MembershipRepository: IgnoreQueryFilters() + CompanyId explícito (igual que GetExpiringAsync).
//   .Where(m => m.CompanyId == companyId && m.Status == MembershipStatus.Active
//            && m.EndDate != null && m.EndDate < asOf)
```

**Defensa chica en dominio (incluir acá):** `Charge.MarkOverdue` hoy guarda solo `Paid`/`WrittenOff`.
Agregar `Refunded`: `if (Status is ChargeStatus.Paid or ChargeStatus.WrittenOff or ChargeStatus.Refunded) return;`
(un cargo reembolsado no debe pasar a mora).

**DI.**
- `AddInfrastructure`: `services.AddScoped<DailyMaintenanceService>();`
  `services.AddScoped<IDailyMaintenanceRunner, DailyMaintenanceRunner>();`
  `services.AddSingleton<IMaintenanceCheckpoint, FileMaintenanceCheckpoint>();`
- `ServiceCollectionExtensions` (Desktop): `services.AddSingleton<DailyMaintenanceHost>();`
- `Program.cs`: borrar `await DunningStartup.RunDailyAsync(App.Services);` (L64) →
  `App.Services.GetRequiredService<DailyMaintenanceHost>().Start();` tras `SessionContext.InitializeAsync()`.
- **Borrar** `src/GymForge.Desktop/Services/DunningStartup.cs`. El checkpoint nuevo usa un archivo distinto
  (`maintenance-last-run.txt`); el viejo `dunning-last-run.txt` queda huérfano (inofensivo; no migrar).

**Archivos.** Nuevos: `IMaintenanceCheckpoint.cs`, `IDailyMaintenanceRunner.cs`,
`UseCases/Maintenance/DailyMaintenanceService.cs`, `UseCases/Maintenance/DailyMaintenanceRunner.cs`,
`Infrastructure/Services/FileMaintenanceCheckpoint.cs`, `Desktop/Services/DailyMaintenanceHost.cs`.
Tocar: `IRepositories.cs`, `MembershipRepository.cs`, `Charge.cs`, `InfrastructureServiceExtensions.cs`,
`Desktop/ServiceCollectionExtensions.cs`, `Desktop/Program.cs`. Borrar: `DunningStartup.cs`.
**UI:** ninguna (backend-only).

---

## 2. AuditLog real vía `SaveChangesInterceptor`

**Problema.** `AuditLog.Create` no se llama en ningún lado → regla #3 del CLAUDE.md incumplida.

**Decisión.** Un **`SaveChangesInterceptor`** que, al persistir, inserta una fila de `AuditLog` por cada entidad
de negocio modificada, en la **misma transacción** del `SaveChanges`. El patrón oficial de EF (inspeccionar
`ChangeTracker.Entries()` en `SavingChangesAsync` y agregar las filas de auditoría al contexto) no re-dispara el
interceptor para esas inserciones → sin recursión.

**Qué se audita.** Lista explícita por **marker `IAuditable`** (consistente con `ITenantScoped` de ADR-002), no
"todo". `dev` agrega `IAuditable` a: `Member`, `Membership`, `Charge`, `Payment`, `MembershipType`, `Product`,
`Sale`, `Shift`, `CashMovement`, `Staff`, `Site`, `Booking`, `StockBySite`. **NUNCA** implementan `IAuditable`:
`AuditLog` (no auto-auditar), `AccessLog` (ya append-only), `MemberCreditMovement` (ledger propio, punto 4),
`WorkoutLog`/`BodyMeasurement` (ruido). Todas las auditadas son `ITenantScoped` → el `CompanyId` sale del propio
entity. `Company` queda fuera (raíz, no `ITenantScoped`, baja frecuencia) — anotar como límite.

**Qué se guarda** (en `AuditLog`, que ya tiene las columnas):
- `Action`: `"Insert"` / `"Update"` / `"Delete"` (de `entry.State`).
- `EntityType`: `entry.Entity.GetType().Name`. `EntityId`: `((BaseEntity)entry.Entity).Id` (PK Guid cliente,
  estable incluso en Insert).
- `UserId`: `ICurrentUser.StaffId` (null si nadie logueado o si es el mantenimiento del punto 1 → operación de
  sistema). `Timestamp`: `IClock.Now`. `CompanyId`: `((ITenantScoped)entry.Entity).CompanyId`
  (fallback `_tenant.CompanyId`).
- `DiffJson` (nivel de detalle para no inflar la base):
  - **Insert** → `null` (la creación es autoevidente; `EntityId` permite ubicar la fila).
  - **Update** → JSON de **solo las props escalares modificadas**: `{"Status":{"old":"Active","new":"Overdue"}}`.
    **Excluir** `UpdatedAt` y `CreatedAt` (si el único cambio es el timestamp, **no** se escribe fila).
  - **Delete** → snapshot JSON de `OriginalValues` escalares (los deletes son raros y peligrosos: conviene
    conservar el dato borrado).

**Append-only.** El interceptor **solo inserta** `AuditLog`; jamás lo modifica/borra. Y nunca audita sus propias
inserciones (AuditLog no es `IAuditable`).

**Firmas / archivos nuevos.**

```csharp
// src/GymForge.Domain/Entities/IAuditable.cs
namespace GymForge.Domain.Entities;
/// <summary>Marker: el AuditSaveChangesInterceptor registra inserts/updates/deletes de esta entidad.</summary>
public interface IAuditable { }

// src/GymForge.Application/Interfaces/ICurrentUser.cs
namespace GymForge.Application.Interfaces;
/// <summary>Staff responsable de la operación en curso. Null = sistema (p. ej. mantenimiento nocturno).</summary>
public interface ICurrentUser { Guid? StaffId { get; } }

// src/GymForge.Infrastructure/Persistence/AmbientCurrentUser.cs  (API / tests / seed)
public sealed class AmbientCurrentUser : ICurrentUser
{
    public Guid? StaffId { get; private set; }
    public void Set(Guid? staffId) => StaffId = staffId;
}

// src/GymForge.Desktop/Services/SessionCurrentUser.cs  (lee el cajero vivo)
public sealed class SessionCurrentUser(SessionContext session) : ICurrentUser
{
    public Guid? StaffId => session.CashierId;   // honesto: null si nadie firmó sesión
}

// src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    // ctor: ICurrentUser user, IClock clock, ICurrentTenant tenant
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        // var ctx = eventData.Context; if (ctx is null) return base...;
        // var logs = new List<AuditLog>();
        // foreach (var e in ctx.ChangeTracker.Entries())
        //     if (e.Entity is IAuditable && e.State is Added or Modified or Deleted)
        //        { if (TryBuildAudit(e, out var log)) logs.Add(log); }   // Modified sin cambios reales → null
        // if (logs.Count > 0) ctx.AddRange(logs);   // van en la misma transacción; AuditLog no es IAuditable → sin recursión
        // return base.SavingChangesAsync(eventData, result, ct);
    }
    // Implementar también SavingChanges (sync) delegando a la misma lógica.
}
```

**Registro (clave).** El interceptor se resuelve del DI scoped (depende de `ICurrentUser`/`ICurrentTenant`), así
que hay que usar el overload `(sp, opts)` de `AddDbContext` en `InfrastructureServiceExtensions`:

```csharp
services.AddScoped<AuditSaveChangesInterceptor>();
services.AddDbContext<GymForgeDbContext>((sp, opts) =>
    opts.UseSqlite(connectionString, sql => sql.MigrationsAssembly(/* ...igual que hoy... */))
        .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
```
`ICurrentUser` debe estar registrado **antes** de `AddInfrastructure` (igual que `ICurrentTenant` hoy):
- Desktop (`ServiceCollectionExtensions`): `services.AddSingleton<ICurrentUser, SessionCurrentUser>();`
- API (`Program.cs`): `services.AddSingleton<AmbientCurrentUser>(); services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<AmbientCurrentUser>());`

**Performance / no romper los 216.** El interceptor recorre `ChangeTracker.Entries()` una vez por `SaveChanges`
(costo despreciable). **`TestDb.Create` NO registra el interceptor** (construye el `DbContext` con opciones a
mano), así que los tests de integración actuales **no** generan filas de AuditLog y sus `CountAsync` no cambian.
El seed (Exercises no es `IAuditable`; Company/Site/Staff/planes sí) genera pocas filas con `UserId` null — es
aceptable. Si algún test de integración que corre por DI completo contara filas de tablas auditadas, revisar;
no se espera (los flujos cuentan `Payments`/`Charges`/`Members`, no `AuditLogs`).

**Deuda relacionada (no bloqueante):** al tocar `AuditLog.cs`, `dev` puede mover `WorkoutLog` a su propio archivo
(deuda D3) — opcional en esta sesión; si no, dejarlo anotado.

**Archivos.** Nuevos: `IAuditable.cs`, `ICurrentUser.cs`, `AmbientCurrentUser.cs`,
`Desktop/Services/SessionCurrentUser.cs`, `Interceptors/AuditSaveChangesInterceptor.cs`.
Tocar: markers en los entities listados, `InfrastructureServiceExtensions.cs`,
`Desktop/ServiceCollectionExtensions.cs`, `Api/Program.cs`. **Sin migración. UI:** ninguna.

---

## 3. Tope de freeze 90 días/año + `UnfreezeMembershipCommand`

**Problema.** `Membership.Freeze` ignora `maxFreezeDaysPerYear`; no hay ventana anual, ni campo configurable en
`Company`, ni `UnfreezeMembershipCommand`.

**Decisión.**

**Ventana = año calendario del `freezeStart`.** Es lo que dice el CLAUDE.md ("90 días/año") y evita una tabla de
historial (una ventana móvil de 365 días la exigiría). Se acumula **por membresía** (los campos viven en
`Membership`); límite conocido: una membresía nueva reinicia el conteo — aceptable para el piloto, anotar.

**Campos nuevos en `Membership`** (se suman a `FreezeCountUsed`, que queda como acumulado histórico informativo):
```csharp
public int FreezeDaysUsedYear { get; private set; }   // días usados en el año FreezeYear
public int FreezeYear { get; private set; }            // año calendario de referencia (0 = sin freeze aún)
```

**`Membership.Freeze` con validación real** (reemplaza el cuerpo actual):
```csharp
public void Freeze(DateOnly freezeStart, DateOnly freezeEnd, string reason, int maxFreezeDaysPerYear = 90)
{
    if (Status != MembershipStatus.Active)
        throw new InvalidOperationException("Solo se pueden congelar membresías activas.");
    int requestedDays = freezeEnd.DayNumber - freezeStart.DayNumber;
    if (requestedDays <= 0)
        throw new ArgumentException("La fecha de fin del congelamiento debe ser posterior al inicio.");

    int year = freezeStart.Year;
    int usedThisYear = FreezeYear == year ? FreezeDaysUsedYear : 0;
    if (usedThisYear + requestedDays > maxFreezeDaysPerYear)
        throw new InvalidOperationException(
            $"Supera el tope de {maxFreezeDaysPerYear} días de congelamiento por año " +
            $"(usados {usedThisYear}, solicitados {requestedDays}).");

    Status = MembershipStatus.Frozen;
    FreezeStart = freezeStart; FreezeEnd = freezeEnd; FreezeReason = reason;
    FreezeYear = year; FreezeDaysUsedYear = usedThisYear + requestedDays;
    FreezeCountUsed += requestedDays;
    if (EndDate.HasValue) EndDate = EndDate.Value.AddDays(requestedDays);  // extiende por el total planificado
    UpdatedAt = DateTime.UtcNow;
    AddDomainEvent(new MembershipFrozenEvent(Id, MemberId, freezeStart, freezeEnd));
}
```

**`Unfreeze` recalcula ante descongelamiento anticipado** (cambia la firma a `Unfreeze(DateOnly today)`):
```csharp
public void Unfreeze(DateOnly today)
{
    if (Status != MembershipStatus.Frozen)
        throw new InvalidOperationException("La membresía no está congelada.");
    if (FreezeEnd.HasValue && today < FreezeEnd.Value)
    {
        int unusedDays = FreezeEnd.Value.DayNumber - today.DayNumber;  // días de freeze no consumidos
        if (EndDate.HasValue) EndDate = EndDate.Value.AddDays(-unusedDays);  // deshace la extensión sobrante
        FreezeDaysUsedYear = Math.Max(0, FreezeDaysUsedYear - unusedDays);   // devuelve cupo
        FreezeCountUsed    = Math.Max(0, FreezeCountUsed - unusedDays);
    }
    Status = MembershipStatus.Active;
    FreezeStart = null; FreezeEnd = null; FreezeReason = null;
    UpdatedAt = DateTime.UtcNow;
}
```

**`Company.MaxFreezeDaysPerYear`** (configurable por empresa, default 90):
```csharp
public int MaxFreezeDaysPerYear { get; private set; } = 90;
// En Create(...): dejar el default 90. Nuevo método:
public void SetFreezePolicy(int maxFreezeDaysPerYear)
{
    if (maxFreezeDaysPerYear < 0) throw new ArgumentException("El tope no puede ser negativo.");
    MaxFreezeDaysPerYear = maxFreezeDaysPerYear; UpdatedAt = DateTime.UtcNow;
}
```

**`FreezeMembershipCommandHandler`** (inyectar `ISiteRepository` para leer el tope de la empresa de la membresía):
```csharp
var ms = await _repo.GetByIdAsync(cmd.MembershipId, ct) ?? throw new KeyNotFoundException(...);
var company = await _sites.GetCompanyAsync(ms.CompanyId, ct) ?? throw new KeyNotFoundException(...);
ms.Freeze(cmd.FreezeStart, cmd.FreezeEnd, cmd.Reason, company.MaxFreezeDaysPerYear);
_repo.Update(ms); await _repo.SaveChangesAsync(ct);
```
(La `InvalidOperationException` del tope burbujea igual que el resto de los handlers.)

**`UnfreezeMembershipCommand`** (nuevo, en `CreateMembershipCommand.cs`, al lado de Freeze/Cancel):
```csharp
public record UnfreezeMembershipCommand(Guid MembershipId) : IRequest<MembershipDto>;

public class UnfreezeMembershipCommandHandler : IRequestHandler<UnfreezeMembershipCommand, MembershipDto>
{
    private readonly IMembershipRepository _repo;
    private readonly IClock _clock;
    public UnfreezeMembershipCommandHandler(IMembershipRepository repo, IClock clock) { _repo = repo; _clock = clock; }

    public async Task<MembershipDto> Handle(UnfreezeMembershipCommand cmd, CancellationToken ct)
    {
        var ms = await _repo.GetByIdAsync(cmd.MembershipId, ct)
            ?? throw new KeyNotFoundException($"Membresía {cmd.MembershipId} no encontrada.");
        ms.Unfreeze(_clock.Today);
        _repo.Update(ms); await _repo.SaveChangesAsync(ct);
        return MembershipDto.FromEntity(ms);
    }
}
```

**Migración (1):** `AddFreezePolicy` → `Company.MaxFreezeDaysPerYear` (int, default 90),
`Membership.FreezeDaysUsedYear` (int, default 0), `Membership.FreezeYear` (int, default 0).
`dotnet ef migrations add AddFreezePolicy --project src/GymForge.Infrastructure --startup-project src/GymForge.Infrastructure`.

**Archivos.** `Membership.cs`, `Company.cs`, `UseCases/Memberships/CreateMembershipCommand.cs`,
`Infrastructure/Migrations/` (nueva). `MembershipConfiguration`/`CompanySiteStaffConfiguration` no requieren cambios
salvo que `dev` quiera precisión explícita (ints default los mapea EF por convención).
**UI (flag para `ux`/preview):** la pestaña **Membresías** de `MemberDetailView` ya tiene Freeze
(`MemberDetailViewModel`). Falta: botón **"Reactivar / Descongelar"** (dispara `UnfreezeMembershipCommand`),
mostrar **días usados / restantes** del año y **surfacear el mensaje de error** del tope al congelar. Backend
de esta sesión; UI a decidir por el orquestador.

---

## 4. Cuenta corriente / saldo a favor (crédito del socio)

**Problema.** El excedente de un pago (`remaining > 0` en `ProcessPaymentCommand`) se descarta.

**Decisión.** **Libro mayor append-only** `MemberCreditMovement` (no un saldo mutable). Justificación: coherente
con el estilo del dominio (AccessLog/AuditLog append-only; allocations N:M; factories inmutables) y auditable
(cada +/- de crédito dice de dónde salió). El **saldo = `SUM(Amount)`** de los movimientos, calculado on-demand
(sin cachear ahora). Multi-tenant: `ISiteScoped` (CompanyId + SiteId), con sus markers → filtro global automático.

**Entidad + enum (nuevos).**
```csharp
// src/GymForge.Domain/Enums/CreditMovementType.cs
namespace GymForge.Domain.Enums;
public enum CreditMovementType { Overpayment, AppliedToCharge, Adjustment, Refund }

// src/GymForge.Domain/Entities/MemberCreditMovement.cs
namespace GymForge.Domain.Entities;
/// <summary>Movimiento de cuenta corriente del socio. Append-only (nunca UPDATE/DELETE). +saldo / -saldo.</summary>
public class MemberCreditMovement : BaseEntity, ISiteScoped   // NO IAuditable (es su propio ledger)
{
    public Guid MemberId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }
    public decimal Amount { get; private set; }            // + agrega crédito, - lo consume
    public CreditMovementType Type { get; private set; }
    public Guid? PaymentId { get; private set; }           // pago de origen (overpayment) o pago que aplica crédito
    public Guid? ChargeId { get; private set; }            // cargo saldado al consumir
    public string? Note { get; private set; }

    private MemberCreditMovement() { }

    public static MemberCreditMovement CreditFromOverpayment(
        Guid companyId, Guid siteId, Guid memberId, decimal amount, Guid paymentId)
    {
        if (amount <= 0) throw new ArgumentException("El crédito debe ser positivo.");
        return new() { CompanyId = companyId, SiteId = siteId, MemberId = memberId,
            Amount = amount, Type = CreditMovementType.Overpayment, PaymentId = paymentId };
    }

    public static MemberCreditMovement DebitForCharge(
        Guid companyId, Guid siteId, Guid memberId, decimal amount, Guid chargeId)
    {
        if (amount <= 0) throw new ArgumentException("El consumo debe ser positivo.");
        return new() { CompanyId = companyId, SiteId = siteId, MemberId = memberId,
            Amount = -amount, Type = CreditMovementType.AppliedToCharge, ChargeId = chargeId };
    }

    public static MemberCreditMovement Adjustment(
        Guid companyId, Guid siteId, Guid memberId, decimal signedAmount, string note)
    {
        if (signedAmount == 0) throw new ArgumentException("El ajuste no puede ser cero.");
        return new() { CompanyId = companyId, SiteId = siteId, MemberId = memberId,
            Amount = signedAmount, Type = CreditMovementType.Adjustment, Note = note };
    }
}
```

**EF config + DbSet.**
```csharp
// GymForgeDbContext: public DbSet<MemberCreditMovement> MemberCreditMovements => Set<MemberCreditMovement>();
// Nueva IEntityTypeConfiguration<MemberCreditMovement> (en ChargePaymentConfiguration.cs o archivo propio):
//   HasKey(Id); Property(Amount).HasPrecision(18,2); Property(Type).HasConversion<string>().HasMaxLength(20);
//   Property(Note).HasMaxLength(300);
//   HasIndex(x => new { x.MemberId, x.CompanyId }).HasDatabaseName("IX_MemberCredit_Member");
//   ToTable("MemberCreditMovements");
// (Append-only: no se expone Update/Remove en el repo.)
```

**Repo (nuevo).**
```csharp
// src/GymForge.Application/Interfaces/IRepositories.cs
public interface IMemberCreditRepository
{
    Task AddAsync(MemberCreditMovement movement, CancellationToken ct = default);
    /// <summary>Saldo a favor del socio = suma de movimientos (filtrado por tenant). >= 0 esperado.</summary>
    Task<decimal> GetBalanceAsync(Guid memberId, CancellationToken ct = default);
    Task<IReadOnlyList<MemberCreditMovement>> GetByMemberAsync(Guid memberId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
// Infrastructure/Repositories/MemberCreditRepository.cs: Scoped, comparte GymForgeDbContext.
//   GetBalanceAsync → _db.MemberCreditMovements.Where(m => m.MemberId == memberId).SumAsync(m => m.Amount, ct)
```

**Generación del crédito — enganche en `ProcessPaymentCommandHandler`:** inyectar `IMemberCreditRepository`;
tras el loop de allocation, si `remaining > 0`:
```csharp
if (remaining > 0)
{
    var credit = MemberCreditMovement.CreditFromOverpayment(
        cmd.CompanyId, cmd.SiteId, cmd.MemberId, remaining, payment.Id);
    await _memberCreditRepo.AddAsync(credit, ct);   // mismo DbContext scoped → misma transacción que el pago
}
```
(El comando siempre tiene `MemberId` no-null; el excedente de una venta a consumidor final no pasa por acá.)

**Consumo del crédito — `ApplyMemberCreditCommand` (nuevo, flujo mínimo de la sesión):**
```csharp
// src/GymForge.Application/UseCases/Charges/ApplyMemberCreditCommand.cs
public record ApplyMemberCreditCommand(
    Guid CompanyId, Guid SiteId, Guid MemberId,
    IReadOnlyList<Guid>? ChargeIds = null) : IRequest<ApplyCreditResult>;

public readonly record struct ApplyCreditResult(decimal Applied, decimal RemainingCredit);

// Handler: ctor IChargeRepository, IMemberCreditRepository.
//  1. balance = await _credit.GetBalanceAsync(MemberId); if (balance <= 0) return (0, 0);
//  2. charges = ChargeIds? validados (existencia + pertenencia socio/empresa, patrón de ProcessPaymentCommand)
//              : await _chargeRepo.GetPendingAsync(MemberId);   ordenar por DueDate.
//  3. remaining = balance;
//     foreach charge: toApply = Min(remaining, charge.AmountOutstanding); if (toApply<=0) continue;
//         charge.ApplyPayment(toApply); _chargeRepo.Update(charge);
//         await _credit.AddAsync(MemberCreditMovement.DebitForCharge(Company, Site, Member, toApply, charge.Id));
//         remaining -= toApply; if (remaining<=0) break;
//  4. await _chargeRepo.SaveChangesAsync();  // charges + movimientos en una transacción
//     return (balance - remaining, remaining);
```
Nota: consumir crédito **no** genera `Payment` (no es caja; es saldo a favor ya cobrado). Queda registrado como
`DebitForCharge` en el ledger y el cargo se salda vía `Charge.ApplyPayment`.

**Consulta para la ficha — `GetMemberCreditBalanceQuery` (nuevo):**
```csharp
public record GetMemberCreditBalanceQuery(Guid MemberId) : IRequest<decimal>;
// Handler: return await _credit.GetBalanceAsync(MemberId, ct);
```

**Migración (1):** `AddMemberCreditLedger` → tabla `MemberCreditMovements`.
`dotnet ef migrations add AddMemberCreditLedger --project src/GymForge.Infrastructure --startup-project src/GymForge.Infrastructure`.

**DI.** `AddInfrastructure`: `services.AddScoped<IMemberCreditRepository, MemberCreditRepository>();`

**Archivos.** Nuevos: `CreditMovementType.cs`, `MemberCreditMovement.cs`, `MemberCreditRepository.cs`,
`ApplyMemberCreditCommand.cs`, `GetMemberCreditBalanceQuery.cs`, config EF.
Tocar: `GymForgeDbContext.cs`, `IRepositories.cs`, `ProcessPaymentCommand.cs`,
`InfrastructureServiceExtensions.cs`, `Infrastructure/Migrations/`.
**UI (flag para `ux`/preview):** mostrar el **saldo a favor** en `MemberDetailView` (pestaña Datos o Cobros) vía
`GetMemberCreditBalanceQuery`; opcional botón **"Usar saldo a favor"** que dispare `ApplyMemberCreditCommand`.
Backend de esta sesión; UI a decidir.

---

## Plan de implementación ordenado (TDD: test rojo → implementación)

Regla #6 CLAUDE.md: `dotnet test GymForge.sln` verde entre pasos. Un commit por punto.

**Paso A — Punto 3 (dominio puro + 1 migración). `feat: [membresias] tope de freeze anual y descongelamiento`.**
1. Domain.Tests (rojo): tope 90 supera → `InvalidOperationException`; acumula en el año; reinicia al cambiar de
   año; `Unfreeze(today)` anticipado deshace extensión y devuelve cupo.
2. Implementar campos + `Freeze` validado + `Unfreeze(DateOnly)` + `Company.MaxFreezeDaysPerYear`/`SetFreezePolicy`.
3. Application.Tests (rojo→verde): `FreezeMembershipCommandHandler` lee el tope de la empresa;
   `UnfreezeMembershipCommandHandler` descongela. 4. Migración `AddFreezePolicy`. `dotnet test`.

**Paso B — Punto 4 (cuenta corriente + 1 migración). `feat: [cobros] cuenta corriente del socio (saldo a favor)`.**
1. Domain.Tests (rojo): factories de `MemberCreditMovement` (signos, validaciones).
2. Entidad + enum + config + DbSet + repo + migración `AddMemberCreditLedger`.
3. Application.Tests (rojo→verde): `ProcessPaymentCommand` con excedente crea crédito; `ApplyMemberCreditCommand`
   consume oldest-first; `GetMemberCreditBalanceQuery`. 4. Integration (SQLite real): pago con excedente → 1
   movimiento + balance correcto; aplicar crédito salda el cargo. `dotnet test`.

**Paso C — Punto 2 (interceptor, sin migración). `feat: [auditoria] registrar cambios vía interceptor de EF`.**
1. Marker `IAuditable` + `ICurrentUser`/impls + interceptor. 2. Registro `(sp,opts)` en `AddDbContext` +
   `ICurrentUser` en Desktop/API. 3. Integration (rojo→verde): construir `GymForgeDbContext` **con** el interceptor
   (stub `ICurrentUser`/`IClock`) → insert/update/delete de un `Member` escribe filas `AuditLog` correctas; que
   `AuditLog`/`AccessLog` **no** se auditan. 4. Confirmar que los 216 siguen verdes (TestDb sin interceptor).
   `dotnet test`.

**Paso D — Punto 1 (scheduler, sin migración). `feat: [mantenimiento] job diario con catch-up`.**
1. Repo `GetActiveExpiredAsync` + `Charge.MarkOverdue` con `Refunded`. 2. `DailyMaintenanceService` +
   `DailyMaintenanceRunner` + `IMaintenanceCheckpoint`/`FileMaintenanceCheckpoint` + `DailyMaintenanceHost`.
3. Borrar `DunningStartup` y recablear `Program.cs`. 4. Tests (abajo). `dotnet test`.

> El scheduler (D) va último porque consume el estado que arreglan B/C de forma indirecta y porque borra
> `DunningStartup`; así el árbol queda siempre compilando.

---

## Tests esperados (nuevos; los 216 no deben bajar)

**Punto 3 — Domain.Tests (`MembershipFreezeTests`) + Application.Tests:**
- `Freeze_ExceedsYearlyCap_Throws` (usado 80 + pide 20, tope 90).
- `Freeze_AccumulatesWithinYear` (dos freezes 40 + 40 ok; el tercero de 20 falla).
- `Freeze_ResetsCounterOnNewYear` (freeze en 2026 no cuenta contra 2027).
- `Unfreeze_EarlyReturnsEndDateAndQuota` (freeze 30 días, unfreeze a los 10 → EndDate -20, cupo +20).
- `Unfreeze_AfterFreezeEnd_NoAdjustment`.
- `FreezeMembershipCommand_UsesCompanyCap`; `UnfreezeMembershipCommand_Reactivates`.

**Punto 4 — Application.Tests + Integration:**
- `ProcessPayment_Overpayment_CreatesCredit` (paga 50k por un cargo de 35k → movimiento +15k).
- `ProcessPayment_ExactAmount_NoCredit`.
- `ApplyMemberCredit_SettlesOldestCharges`; `ApplyMemberCredit_NoBalance_NoOp`.
- `ApplyMemberCredit_ChargeOfAnotherMember_Throws` (pertenencia, patrón ProcessPayment).
- Integration `MemberCreditFlow`: pago con excedente persiste 1 movimiento; `GetBalanceAsync` correcto;
  aplicar crédito deja el cargo `Paid` y balance en 0.

**Punto 2 — Integration (`AuditInterceptorIntegrationTests`, DbContext construido con el interceptor):**
- `Insert_WritesAuditRow` (Action=Insert, EntityType/EntityId correctos, DiffJson null).
- `Update_WritesOnlyChangedProps` (cambiar `Member.Status` → DiffJson con esa prop; sin UpdatedAt).
- `Update_OnlyTimestamp_NoRow` (si solo cambia UpdatedAt, no se escribe fila).
- `Delete_SnapshotsOriginalValues`.
- `AuditLog_And_AccessLog_AreNeverAudited`.
- `UserId_FromCurrentUser` (stub `ICurrentUser.StaffId` → va al row; null cuando no hay).

**Punto 1 — Application.Tests + Integration:**
- `Runner_FirstRun_ProcessesOnlyToday` (sin checkpoint, no backfill).
- `Runner_CatchUp_ProcessesMissedDays` (checkpoint hace 3 días → corre 3 días; dunning por día).
- `Runner_SameDayTwice_NoOp` (idempotencia por checkpoint).
- `Runner_CapsCatchUpWindow` (checkpoint muy viejo → máx 35 días).
- `Runner_NeverThrows` (una empresa falla → se loguea y sigue con las demás).
- Domain/Integration: `GetActiveExpiredAsync` devuelve solo activas vencidas de la empresa (company-wide);
  `MaintenanceDay_ExpiresMembership_MarksChargeAndMemberOverdue` sobre SQLite real.
- Usar un `IMaintenanceCheckpoint` en memoria (stub) en los tests del runner; `IClock` fake para fijar "hoy".

---

## Migraciones

| # | Nombre | Punto | Cambios |
|---|--------|-------|---------|
| 1 | `AddFreezePolicy` | 3 | `Company.MaxFreezeDaysPerYear` (int, def 90); `Membership.FreezeDaysUsedYear` (int, def 0); `Membership.FreezeYear` (int, def 0) |
| 2 | `AddMemberCreditLedger` | 4 | Tabla `MemberCreditMovements` (+ índice `IX_MemberCredit_Member`) |

Puntos 1 y 2 (interceptor): **sin cambios de schema**. Generar con `dotnet ef migrations add <Nombre>
--project src/GymForge.Infrastructure --startup-project src/GymForge.Infrastructure` (design-time factory de
Infrastructure, CLAUDE.md). Verificar que `GymForgeDbContextModelSnapshot` quede consistente.

---

## Partes que tocan UI (para que el orquestador decida `ux`/`preview`)

- **Punto 3 (opcional, recomendado):** botón "Reactivar/Descongelar" + días usados/restantes + mensaje de tope en
  `MemberDetailView` → pestaña Membresías. El backend queda listo esta sesión.
- **Punto 4 (opcional, recomendado):** mostrar "Saldo a favor" en la ficha del socio + botón "Usar saldo a favor".
  El backend (entidad, query, caso de uso) queda listo esta sesión.
- **Puntos 1 y 2:** backend-only, sin UI.

Sugerencia: dejar los 4 puntos **backend-only** en esta sesión (qa valida correctitud) y abrir las dos piezas de
UI como tarea de `ux`/`preview` en la Sesión siguiente, para no mezclar lógica de negocio con pulido visual.

---

## Consequences

- **+** El estado se mantiene solo (vence/mora/dunning) con catch-up real para una app que no está always-on.
- **+** Se cumple la regla #3 del CLAUDE.md: todo cambio de negocio deja rastro en `AuditLog`, append-only, sin
  romper flujos ni tests (TestDb no engancha el interceptor).
- **+** El tope de freeze pasa a ser una regla de dominio verificada y configurable por empresa; el unfreeze
  anticipado deja de regalar días de vigencia.
- **+** El saldo a favor deja de perderse; queda auditable y consultable para la ficha del socio.
- **−** Nuevo marker `IAuditable` a mantener (igual que `ITenantScoped`): agregar entidades nuevas al criterio.
- **−** El checkpoint de mantenimiento es global (desktop = 1 empresa); multi-empresa comparte ventana — límite
  anotado. El conteo de freeze es por membresía (se reinicia con una membresía nueva) — límite anotado.
- **Riesgo EF:** el interceptor resuelto por DI exige el overload `(sp, opts)` de `AddDbContext`; verificar que no
  rompa el design-time factory (que construye el `DbContext` sin interceptor — correcto, no audita migraciones).

---

## Referencias

- CLAUDE.md — Reglas absolutas 1–7 (esp. #3 append-only de AuditLog/AccessLog; offline-first; multi-tenant).
- `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md` (infra de tenant reutilizada).
- `docs/PROGRESS.md` (Sesión 2, "Qué sigue") · `docs/TASKS.md` (S2-1..S2-4).
- https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors (SaveChanges interceptor / auditing).
- https://learn.microsoft.com/ef/core/querying/filters (nota: `IgnoreQueryFilters` para lecturas company-wide).
