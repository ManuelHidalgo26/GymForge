# ADR-002 — Multi-tenant (query filters) y correctitud del core

**Status:** Accepted
**Date:** 2026-10-27
**Deciders:** arqui (planner) · implementa `dev`
**Sesión:** 1 — "correctitud del core"

---

## Context

Auditoría del core detectó 5 defectos que violan reglas absolutas del CLAUDE.md
(offline-first atómico, aislamiento multi-tenant, append-only de accesos). Este ADR es la
guía de implementación para `dev`. Alcance acotado a estos 5 puntos; nada más entra en la sesión.

Hechos del código verificados (para que el enfoque sea exacto):

- Los repos se registran **`Scoped`** y comparten el **mismo `GymForgeDbContext`** dentro de un scope
  (`InfrastructureServiceExtensions.cs`). Un `SaveChanges` de cualquier repo del scope persiste todo lo
  trackeado en una sola transacción SQLite. Esto habilita la atomicidad del punto 1.
- `AccessLogRepository.AppendAsync` **ya hace `SaveChangesAsync`** por dentro.
- `TenantEntity` (`BaseEntity.cs`) existe pero **ningún** entity lo hereda; cada uno declara sus
  propios `CompanyId`/`SiteId` con `private set`. Es código muerto.
- `GymForgeDbContext` **no tiene** ningún `HasQueryFilter`.
- `ChargeRepository.GetByIdAsync` usa `FindAsync([id])`, que **ignora los query filters** (busca por PK).
- `AccessLog.MemberId` es `Guid` (no-nullable) con FK `OnDelete(Restrict)`; `AccessLog.Denied(...)` exige `memberId`.
- `SessionContext` (Desktop, Singleton) ya tiene `CompanyId`/`SiteId`; es la fuente del tenant vivo.
- `MembershipType` ya tiene `DurationUnit`/`DurationValue`: es el lugar natural del cálculo de vencimiento.

---

## 1. Bug de persistencia de visitas en el Gatekeeper

**Problema.** En `ValidateSwipeUseCase.cs` (camino ALLOW, ~L150) se llama `AppendAsync(log)` (que ya
persiste) y **recién después** `membership.DecrementVisit()` sin `SaveChanges` posterior → el decremento
queda en el ChangeTracker y nunca llega a la DB.

**Decisión.** Decrementar y marcar la membresía **antes** del append, para que el `SaveChanges` interno de
`AppendAsync` persista log + decremento en la misma unidad de trabajo (ambos repos comparten el DbContext scoped).

```csharp
// ALLOW — decrementar la visita ANTES de persistir: el SaveChanges de AppendAsync
// confirma el log y el decremento en una sola transacción.
if (membership.VisitsRemaining > 0)
{
    membership.DecrementVisit();
    _membershipRepo.Update(membership);
}

var log = AccessLog.Granted(/* ... */);
await _accessLogRepo.AppendAsync(log, ct);

await _eventBus.PublishAsync(new MemberCheckedIn(member.Id, warn, owing), ct);
```

**Archivos.** `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs`.

**Tests (`dev`).**
- Unit (Application.Tests/GatekeeperTests): `VisitPack_GrantedSwipe_DecrementsVisitOnce` → verificar
  `_membershipRepo.Received(1).Update(Arg.Is<Membership>(m => m.VisitsRemaining == N-1))` y que el `Update`
  ocurre **antes** del `AppendAsync` (usar `Received.InOrder`).
- Integration (Integration.Tests): swipe real sobre SQLite con pack de N visitas → recargar la membresía del
  contexto y assert `VisitsRemaining == N-1` (prueba que persistió, no solo que mutó en memoria).

---

## 2. Membresía que no vence (endDate null)

**Problema.** `CreateMembershipCommand` (~L48) crea con `endDate: null`. La lógica correcta ya existe
duplicada como `CalculateEnd` privado en `SellMembershipCommand.cs` (~L109).

**Decisión.** Mover el cálculo a un **método de dominio** en `MembershipType` (donde viven `DurationUnit`/
`DurationValue`) y que **ambos** comandos lo usen. Elimina la duplicación y pone la regla en el dominio.

```csharp
// MembershipType.cs — comportamiento idéntico al CalculateEnd actual.
/// <summary>Vencimiento = inicio + duración del plan. Preserva el mapeo actual de DurationUnit.</summary>
public DateOnly? CalculateEndDate(DateOnly start) => DurationUnit switch
{
    "Day"  => start.AddDays(DurationValue),
    "Year" => start.AddYears(DurationValue),
    _      => start.AddMonths(DurationValue),
};
```

- `SellMembershipCommand.cs`: borrar `CalculateEnd` privado y usar `plan.CalculateEndDate(today)`.
- `CreateMembershipCommandHandler`: **inyectar `IMembershipTypeRepository`**, cargar el plan y pasar
  `endDate: plan.CalculateEndDate(cmd.StartDate)` a `Membership.Create(...)`.

**Archivos.** `src/GymForge.Domain/Entities/MembershipType.cs` ·
`src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs` ·
`src/GymForge.Application/UseCases/Sales/SellMembershipCommand.cs`.

**Nota fuera de alcance (registrar, no implementar ahora):** `CreateMembershipCommand` tampoco siembra
`VisitsRemaining` para planes `VisitPack`. Dejar TODO; se trata en otra sesión.

**Tests (`dev`).**
- Domain.Tests: `MembershipType.CalculateEndDate` para `Month`/`Day`/`Year` (casos borde: 1 mes, 12 meses).
- Application.Tests/CreateMembershipCommand: plan mensual → `endDate == start.AddMonths(1)` (hoy daría null).

---

## 3. Multi-tenant con EF Core global query filters

**Problema.** No hay `HasQueryFilter`. El aislamiento es manual e inconsistente: varios `GetByIdAsync`
filtran solo por `Id`; el Gatekeeper resuelve socio por `CompanyId` pero no por `SiteId`. `TenantEntity`
está sin usar.

### 3.1 Fuente del tenant: `ICurrentTenant`

Nueva interfaz en **Application** (el DbContext de Infrastructure la consume; Desktop/API/tests la implementan):

```csharp
// src/GymForge.Application/Interfaces/ICurrentTenant.cs
namespace GymForge.Application.Interfaces;

/// <summary>Tenant activo (empresa + sede) para los filtros globales de EF Core.</summary>
public interface ICurrentTenant
{
    Guid CompanyId { get; }
    Guid SiteId { get; }

    /// <summary>Override temporal del tenant (Gatekeeper = sede de la puerta; API = tenant del request).
    /// Al disponer, restaura el anterior. Implementar con AsyncLocal para aislar por flujo async.</summary>
    IDisposable BeginScope(Guid companyId, Guid siteId);
}
```

Implementaciones:

| Proyecto | Clase | Fuente / registro |
|----------|-------|-------------------|
| Desktop | `SessionTenantProvider : ICurrentTenant` | Lee `SessionContext.CompanyId/SiteId`; `BeginScope` apila en `AsyncLocal`. Registrar **Singleton** en `ServiceCollectionExtensions`. |
| Infrastructure | `AmbientTenantProvider : ICurrentTenant` | Mutable (`Set(company, site)` + `BeginScope` vía `AsyncLocal`). Reutilizable por API y tests. Registrar en API. |
| Tests | `AmbientTenantProvider` | Se construye a mano y se pasa al DbContext. |

> **Capas:** la interfaz vive en Application; `SessionTenantProvider` en Desktop evita que Infrastructure
> dependa de Desktop. `SessionContext` no cambia.

### 3.2 Qué entidad lleva qué filtro

Resolución del wart de `TenantEntity`: **borrarlo** (código muerto) e introducir markers en Domain que los
entities ya satisfacen con sus getters actuales (solo se agrega la interfaz a la declaración, **sin** tocar
propiedades ni setters):

```csharp
// src/GymForge.Domain/Entities/ITenantScoped.cs
namespace GymForge.Domain.Entities;
public interface ITenantScoped { Guid CompanyId { get; } }
public interface ISiteScoped : ITenantScoped { Guid SiteId { get; } }
```

| Tier | Filtro | Entidades |
|------|--------|-----------|
| Site-scoped (`ISiteScoped`) | `CompanyId == t.CompanyId && SiteId == t.SiteId` | Member, Membership, Charge, Payment, AccessLog, Shift, CashMovement, Sale, SaleLine, StockBySite, ClassSchedule, Booking, Routine (+ días/items/sets), WorkoutLog, BodyMeasurement — **cualquiera que exponga ambos** |
| Company-scoped (`ITenantScoped`) | `CompanyId == t.CompanyId` | MembershipType, Product, ClassDescription, Site, Staff, AuditLog — **las que solo tengan CompanyId** |
| Global / especial | filtro propio | `Exercise`: `TenantId == null \|\| TenantId == t.CompanyId` (biblioteca global + propios) |
| Sin filtro | — | `Company` (raíz del tenant; se lista en bootstrap antes de conocer el tenant). `PaymentAllocation` y demás hijos **sin** `CompanyId` (se alcanzan por su padre ya filtrado). |

`dev` agrega el marker a cada clase según sus propiedades reales (no forzar props nuevas). El mecanismo
(abajo) aplica el filtro por tipo, así que la lista es orientativa: lo autoritativo es "¿qué interfaz implementa?".

### 3.3 Aplicación en el DbContext

```csharp
// GymForgeDbContext: inyectar el tenant y guardarlo en campo.
private readonly ICurrentTenant _tenant;
public GymForgeDbContext(DbContextOptions<GymForgeDbContext> options, ICurrentTenant tenant)
    : base(options) => _tenant = tenant;

// En OnModelCreating, DESPUÉS de ApplyConfigurationsFromAssembly:
foreach (var et in modelBuilder.Model.GetEntityTypes())
{
    var clr = et.ClrType;
    if (clr == typeof(Exercise)) continue; // filtro especial, abajo
    if (typeof(ISiteScoped).IsAssignableFrom(clr))
        typeof(GymForgeDbContext).GetMethod(nameof(ApplySiteFilter), /* NonPublic|Instance */)!
            .MakeGenericMethod(clr).Invoke(this, [modelBuilder]);
    else if (typeof(ITenantScoped).IsAssignableFrom(clr))
        typeof(GymForgeDbContext).GetMethod(nameof(ApplyCompanyFilter), /* ... */)!
            .MakeGenericMethod(clr).Invoke(this, [modelBuilder]);
}
modelBuilder.Entity<Exercise>()
    .HasQueryFilter(e => e.TenantId == null || e.TenantId == _tenant.CompanyId);

private void ApplyCompanyFilter<T>(ModelBuilder mb) where T : class, ITenantScoped =>
    mb.Entity<T>().HasQueryFilter(e => e.CompanyId == _tenant.CompanyId);
private void ApplySiteFilter<T>(ModelBuilder mb) where T : class, ISiteScoped =>
    mb.Entity<T>().HasQueryFilter(e => e.CompanyId == _tenant.CompanyId && e.SiteId == _tenant.SiteId);
```

Notas clave para `dev`:
- Referenciar **`_tenant`** (miembro de instancia) dentro del lambda: EF lo **parametriza** y cachea un solo
  modelo. No capturar un `Guid` literal (obligaría a `IModelCacheKeyFactory`).
- **`FindAsync` ignora los query filters.** `ChargeRepository.GetByIdAsync` usa `FindAsync([id])` → cambiarlo a
  `FirstOrDefaultAsync(c => c.Id == id, ct)` para que el filtro aplique. Revisar cualquier otro `FindAsync`.
- Posible warning `RequiredNavigationWithQueryFilterInteractionWarning` en navegaciones entre tiers distintos
  (p. ej. `Member`→`Site`). Preferir filtros coincidentes; si aparece y es benigno, documentar. No silenciar a ciegas.
- El `GymForgeDbContextFactory` (design-time) y toda construcción directa pasan un tenant: usar
  `new GymForgeDbContext(opts, new AmbientTenantProvider())`.

### 3.4 Gatekeeper scopeado por la sede de la puerta

Inyectar `ICurrentTenant` en `ValidateSwipeUseCase` y **abrir scope con el tenant del request** al inicio:

```csharp
public async Task<AccessDecision> ValidateSwipeAsync(ValidateSwipeRequest request, CancellationToken ct = default)
{
    // El tenant efectivo es el de la puerta: resuelve socio y membresía SOLO en esa empresa+sede.
    using var _ = _tenant.BeginScope(request.CompanyId, request.SiteId);
    // ... resto igual ...
}
```

Con el scope activo, `FindByTagSerialAsync`, `FindByDocumentAsync`, `GetCurrentActiveAsync`, `SumOutstandingAsync`
y `GetLastGrantedAsync` (anti-passback) quedan **automáticamente** filtrados por `CompanyId + SiteId` sin cambiar sus
firmas. Un socio de otra sede deja de resolver en la puerta. (No hace falta hilar `SiteId` a mano por repo.)

### 3.5 Bypass legítimo (`IgnoreQueryFilters`)

Los filtros aplican a **lecturas LINQ**, no a inserts/`SaveChanges`. Puntos de bypass:

- `ISiteRepository` **completo** (GetCompanies/GetByCompany/GetCompany/GetSite): es la **resolución** del tenant,
  corre antes de que el filtro esté poblado → `IgnoreQueryFilters()` en sus queries.
- `StaffRepository.GetActiveByCompanyAsync`: también participa del bootstrap/login → `IgnoreQueryFilters()`.
- `DatabaseSeeder`: los checks de existencia (`Companies.AnyAsync`, `Exercises.AnyAsync`) → `IgnoreQueryFilters()`
  (los inserts no necesitan nada).
- `CompleteOnboardingCommand`: crea company/site/staff (inserts, sin bypass). Si lee para validar existencia, bypass.
- Tool de screenshots y `DemoDataSeeder`: tras sembrar, **`AmbientTenantProvider.Set(company, site)`** del demo
  antes de leer/mostrar.
- `SessionContext.InitializeAsync`: usa `ISiteRepository` (ya con bypass) para descubrir el tenant; una vez
  seteado `CompanyId`, el resto de la app queda filtrada. OK sin cambios adicionales.
- `DatabaseBackupService` / `DataTransferService`: operan a nivel archivo/SQLite (connection string), **no** vía
  LINQ → **no** afectados.

**Archivos.** `src/GymForge.Application/Interfaces/ICurrentTenant.cs` (nuevo) ·
`src/GymForge.Domain/Entities/ITenantScoped.cs` (nuevo) · `BaseEntity.cs` (borrar `TenantEntity`) ·
markers en cada entity de `src/GymForge.Domain/Entities/*` · `GymForgeDbContext.cs` ·
`GymForgeDbContextFactory.cs` · `ChargeRepository.cs` (FindAsync→FirstOrDefault) ·
`SiteRepository.cs` + `StaffRepository.cs` (IgnoreQueryFilters) · `DatabaseSeeder.cs` ·
`src/GymForge.Desktop/Services/SessionTenantProvider.cs` (nuevo) + `ServiceCollectionExtensions.cs` ·
`src/GymForge.Infrastructure/Persistence/AmbientTenantProvider.cs` (nuevo) + registro en API `Program.cs` ·
`tools/GymForge.Screenshots/Program.cs`.

### 3.6 Tests — no romper los existentes

**Unit (NSubstitute, sin DbContext).** No los toca el filtro, pero `ValidateSwipeUseCase` gana una dependencia:
en `GatekeeperTests.CreateSut()` pasar `Substitute.For<ICurrentTenant>()`; stubear
`BeginScope(Arg.Any<Guid>(), Arg.Any<Guid>())` para devolver un `IDisposable` no-op.

**Integration (SQLite real).** Los 7 archivos que hacen `new GymForgeDbContext(opts)` deben pasar el provider:
```csharp
var tenant = new AmbientTenantProvider();
_db = new GymForgeDbContext(opts, tenant);
// ... tras sembrar Company+Site:
tenant.Set(company.Id, site.Id);
```
Recomendado: un helper único `TestDb.Create(out AmbientTenantProvider tenant)` en Integration.Tests para
centralizar y evitar repetir.

**`MemberRepositoryIntegrationTests.Queries_IsolateByTenant` (el crítico).** Hoy consulta los dos tenants desde
el mismo `_repo` sin cambiar ambiente → con filtro eso rompe. Reescribir para **cambiar el scope por tenant**:
```csharp
Member f1, f2;
using (tenant.BeginScope(_companyId, _site.Id))
    f1 = await _repo.FindByTagSerialAsync("SHARED-TAG", _companyId);
using (tenant.BeginScope(otherCompany.Id, otherSite.Id))
    f2 = await _repo.FindByTagSerialAsync("SHARED-TAG", otherCompany.Id);
f1!.LastName.Should().Be("Uno");
f2!.LastName.Should().Be("Dos");
```
Esto mantiene la intención (aislamiento) y la **refuerza**: sin el scope correcto, la query no devuelve el socio.

**Test nuevo (prueba el filtro, no el `Where` manual).** Con ambiente = tenant1, una query **sin** `Where`
explícito ve solo a tenant1:
```csharp
using (tenant.BeginScope(_companyId, _site.Id))
    (await _db.Members.ToListAsync()).Should().OnlyContain(m => m.CompanyId == _companyId);
```

---

## 4. Validación de pertenencia en pagos

**Problema.** En `ProcessPaymentCommandHandler`, con `ChargeIds` explícitos, `GetByIdAsync(id)` no valida que el
cargo sea del socio/empresa. (Agravado porque hoy `GetByIdAsync` usa `FindAsync` y **saltea** el query filter —
ver 3.3.)

**Decisión.** Defensa explícita socio↔cargo, además del filtro de tenant. Reemplazar la carga silenciosa de
`ChargeIds` por una que exige existencia y pertenencia:

```csharp
List<Charge> pendingCharges;
if (cmd.ChargeIds?.Any() == true)
{
    pendingCharges = [];
    foreach (var id in cmd.ChargeIds)
    {
        var charge = await _chargeRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Cargo {id} no encontrado.");
        // El cargo debe pertenecer al socio y a la empresa del cobro.
        if (charge.MemberId != cmd.MemberId || charge.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("El cargo no pertenece al socio indicado.");
        pendingCharges.Add(charge);
    }
}
else
{
    pendingCharges = (await _chargeRepo.GetPendingAsync(cmd.MemberId, ct)).ToList();
}
```

**Archivos.** `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs` ·
`src/GymForge.Infrastructure/Repositories/ChargeRepository.cs` (GetByIdAsync → `FirstOrDefaultAsync`).

**Tests (`dev`).** Application.Tests/ProcessPaymentCommandTests:
- `Handle_ChargeOfAnotherMember_Throws`: `GetByIdAsync` devuelve un `Charge` con otro `MemberId` → espera
  `InvalidOperationException`.
- `Handle_UnknownChargeId_Throws`: `GetByIdAsync` devuelve null → `KeyNotFoundException`.
- Regresión: el happy path existente con cargos del mismo socio sigue verde.

---

## 5. Auditar accesos de credencial desconocida

**Problema.** En `ValidateSwipeUseCase.AppendDenied` (~L183) hay `if (!memberId.HasValue) return;` → los swipes
de tag/DNI desconocido (`TagUnknown`) **no dejan rastro**, violando el espíritu append-only de accesos.

**Decisión.** Permitir `AccessLog` denegado **sin socio**. Requiere:

1. `AccessLog.MemberId` → `Guid?`; navegación `Member? Member`. Nuevo factory:
```csharp
/// <summary>Acceso denegado por credencial no reconocida (sin socio asociado). Append-only.</summary>
public static AccessLog DeniedUnknown(
    Guid companyId, Guid siteId, int doorId, AccessMethod method,
    AccessDenialReason reason, string? tagSerial = null) => new()
{
    CompanyId = companyId, SiteId = siteId, MemberId = null, DoorId = doorId,
    Method = method, Direction = AccessDirection.In, TagSerial = tagSerial,
    AccessGranted = false, DenialReason = reason, SwipedAt = DateTime.UtcNow
};
```
2. `AccessLogConfiguration`: FK opcional — `HasForeignKey(a => a.MemberId).IsRequired(false)` conservando
   `OnDelete(Restrict)`. Índices actuales siguen válidos (un `Guid?` null nunca matchea un id real).
3. `AppendDenied`: quitar el `return` temprano; si `memberId` es null, usar `DeniedUnknown`, si no, `Denied`.
4. **Migración EF** `AccessLogAllowUnknownMember` (único cambio de schema de la sesión): columna `MemberId`
   pasa a nullable. Generar con `dotnet ef migrations add AccessLogAllowUnknownMember` (ver CLAUDE.md).

> `AccessLog` es `ISiteScoped` (punto 3): estos registros quedan filtrados por empresa+sede como el resto.
> Reportes por socio (`GetByMemberAsync`) no los incluyen (MemberId null); los de sede (`GetBySiteAsync`) sí.

**Archivos.** `src/GymForge.Domain/Entities/AccessLog.cs` ·
`src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs` ·
`src/GymForge.Infrastructure/Persistence/Configurations/AccessLogConfiguration.cs` ·
`src/GymForge.Infrastructure/Migrations/` (nueva migración).

**Tests (`dev`).**
- Unit (GatekeeperTests): `UnknownTag_WritesDeniedLog` → `_accessLogRepo.Received(1).AppendAsync(Arg.Is<AccessLog>(
  a => !a.AccessGranted && a.MemberId == null && a.DenialReason == AccessDenialReason.TagUnknown), ...)`.
- Integration: swipe con tag inexistente sobre SQLite → existe 1 fila en `AccessLogs` con `MemberId null`
  (prueba que la FK nullable y el insert funcionan).

---

## Orden de implementación sugerido (1 commit por punto)

1. **Punto 2** (dominio puro, sin side-effects). `feat: [membresias] calcular vencimiento desde el plan`.
2. **Punto 1** (reorden Gatekeeper). `fix: [acceso] persistir el decremento de visitas`.
3. **Punto 5** (migración + nullable). `feat: [acceso] auditar swipes de credencial desconocida`.
4. **Punto 4** (pertenencia en pagos). `fix: [cobros] validar que el cargo sea del socio`.
5. **Punto 3** (query filters — el más invasivo, va último para absorber los ajustes de tests).
   `feat: [multi-tenant] filtros globales por empresa y sede`.

`dotnet test` verde es condición de avance entre pasos (regla 6 del CLAUDE.md). El paso 5 es el que mueve los
tests de integración; abordarlo con el helper `TestDb.Create` para acotar el blast radius.

---

## Consequences

- **+** Aislamiento multi-tenant deja de depender de que cada query recuerde filtrar a mano; se vuelve el default.
- **+** El Gatekeeper queda correcto por sede y persiste las visitas atómicamente.
- **+** Rastro de accesos completo (incluye credenciales desconocidas) → mejor diagnóstico en recepción.
- **−** Cuidado permanente con `FindAsync` y con cualquier operación cross-tenant legítima (usar bypass explícito).
- **−** Los tests de integración ahora deben declarar su tenant; el helper mitiga la fricción.
- **Riesgo EF:** warnings de navegación required↔filtro; resolver con filtros coincidentes antes que silenciar.

---

## Referencias

- CLAUDE.md — Reglas absolutas 1–7; contratos de capas.
- https://learn.microsoft.com/ef/core/querying/filters (global query filters; nota sobre `FindAsync`/`IgnoreQueryFilters`)
- `docs/adr/ADR-001-stack-decision.md`
