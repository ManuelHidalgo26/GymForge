# ADR-016 — Auditoría de activación/renovación de licencia (F1-D10): AuditLog explícito, sin hacer `Company` auditable

**Status:** Accepted
**Date:** 2026-11-12
**Deciders:** arqui (planner) · implementa `dev` · veta `qa`
**Cierra:** F1-D10 (`docs/TASKS.md`)
**Extiende:** ADR-003 (§2 AuditLog / `IAuditable`; **reafirma** "Company fuera de AuditLog") · ADR-013 (minimización de PII en AuditLog)

---

## Context

**F1-D10 (BAJA).** Activar o renovar una licencia no deja rastro en AuditLog: `Company` no es `IAuditable`,
y el intento **rechazado por CUIT** tampoco se registra. Es un cambio de plan (facturable) sin "quién/cuándo".

### Hechos del código (verificados al 2026-11-12)

- **AuditLog se escribe SOLO vía `AuditSaveChangesInterceptor`**, para entidades marcadas `IAuditable`
  (allowlist explícita: `Member, Membership, Charge, Payment, MembershipType, Product, Sale, Shift,
  CashMovement, Staff, Site, Booking, StockBySite`). `AuditLog.Create(...)` **no se invoca en ningún handler**:
  no existe hoy el patrón "audit explícito".
- **ADR-003 dejó `Company` fuera a propósito:** *"Company queda fuera (raíz, no `ITenantScoped`, baja
  frecuencia) — anotar como límite."* El interceptor resuelve el `CompanyId` de la fila auditada del **propio
  entity** asumiendo que es `ITenantScoped` (`CompanyIdOf`); `Company` **no lo es** → caería al **tenant
  ambiente**, que en el **Insert de onboarding** está vacío.
- **Handler** `ActivateLicenseCommandHandler` (`src/GymForge.Application/UseCases/Licensing/ActivateLicenseCommand.cs`):
  **un solo comando para activar Y renovar** (renovar = pegar la clave encadenada; mismo flujo). Valida la clave
  (vencida / inválida → lanza antes de cargar la empresa), carga `Company`, chequea CUIT (`EnsureKeyBelongsTo`),
  muta `company.ActivateLicense(key)` y persiste con `_repo.SaveChangesAsync(ct)`. La rama **CUIT distinto lanza
  ANTES de persistir** (nada queda en la base).
- **AuditLog** es `ITenantScoped` → lecturas filtradas por `CompanyId` (query filter global). **Append-only**
  por regla de CLAUDE.md (nunca UPDATE/DELETE).
- **Actor:** `ICurrentUser.StaffId` (en Desktop = cajero logueado; la activación se hace desde Config con sesión
  abierta). **Timestamp:** `IClock.Now`. Ambos ya registrados en DI (los usa el interceptor).

---

## Decisión: Opción B — AuditLog explícito y acotado en el handler, SIN hacer `Company` `IAuditable`

### Por qué NO la Opción A (`Company : IAuditable`)

1. **Revierte una decisión deliberada de ADR-003** y empezaría a auditar **todo** cambio de `Company` (nombre,
   CUIT, timezone, dunning, settings, branding, freeze policy), mucho más alcance que esta deuda.
2. **`Company` no es `ITenantScoped`:** el interceptor no sabría resolver su `CompanyId` (caería al tenant
   ambiente, **vacío en el Insert de onboarding**) → habría que **parchear `CompanyIdOf`**. Efecto colateral real.
3. **Volcaría CUIT y clave de licencia enteros** al log append-only en cada edición, contra la minimización de
   ADR-013 (y en un log no purgable).
4. **Churn de tests** (`OnlyTheAgreedEntitiesAreMarkedAuditable` + cualquier conteo de auditoría tras tocar
   `Company` en onboarding/branding/config).

### Qué se audita (B)

- **Activación / renovación OK** → una fila AuditLog en la **misma `SaveChanges`** que la mutación de `Company`:
  `EntityType = "Company"`, `EntityId = cmd.CompanyId`, `Action = "LicenseActivated"`, `UserId = actor`,
  `CompanyId = cmd.CompanyId`, `Timestamp = clock.Now`. `DiffJson` = resumen **mínimo**: `tier`, `expiresOn`,
  `status`, `renewal` (bool: había `LicenseKey` previa) y `machineMismatch`. **Nunca** la clave cruda ni el CUIT.
- **Intento rechazado por CUIT → SÍ se audita:** `Action = "LicenseActivationRejected"`, `reason = "CuitMismatch"`,
  detalles mínimos (`keyTier`, `keyExpiresOn`), **sin el CUIT de contraparte** (es el identificador de un tercero,
  en un log no purgable). Se persiste **antes** de lanzar. **Best-effort:** si la escritura del audit falla, se
  loguea `Warning` (Serilog) y **se lanza igual** el mensaje de dominio (no se enmascara el error de negocio).
- **NO se audita:** paste de clave **inválida o vencida** (ruido, sin valor forense ni contexto de tenant claro),
  ni el caso **CUIT no comparable** (ahí se activa igual → cae en `"LicenseActivated"`).

### Minimización (coherente con ADR-013) y append-only

- Los `DiffJson` solo describen el **plan** (tier / vencimiento / estado); nunca la clave firmada completa ni un
  CUIT de terceros. Solo **INSERT** de AuditLog, jamás UPDATE/DELETE. Multi-tenant: `CompanyId = cmd.CompanyId`.

### Seam de persistencia

- **Recomendado:** agregar `Task AppendAuditLogAsync(AuditLog log, CancellationToken ct = default)` a
  `ISiteRepository` (que el handler ya usa) para **stagear** la fila; una sola `SaveChangesAsync` persiste
  `Company` + audit **atómicamente** (misma garantía que el interceptor).
- **Alternativa** (consistente con los append-only `IAccessLogRepository` / `INotificationLogRepository`): un
  `IAuditLogRepository` dedicado. Se deja a criterio de dev/qa; si se elige, la atomicidad del happy-path pasa a
  depender del **DbContext compartido** del Desktop (acoplamiento implícito) — por eso se prefiere la primera.

---

## Qué NO se toca (límites)

- `Company` **no** pasa a `IAuditable`. `AuditSaveChangesInterceptor` y `CompanyIdOf` **intactos**. La allowlist y
  su test (`OnlyTheAgreedEntitiesAreMarkedAuditable`) **intactos** (`Company` sigue fuera).
- `LicenseService` / `LicenseCodec` / `CurrentLicense` / formato de clave / `scripts/licencia.ps1` **intactos**.
- El **mensaje y el comportamiento de rechazo** al usuario no cambian (el audit es **aditivo** y best-effort).
- **Sin migración de esquema:** `AuditLog` ya tiene todas las columnas.

---

## Consecuencias

- Desde esta versión, cada activación/renovación deja "quién/cuándo/qué plan", y un intento con clave de otro CUIT
  deja rastro, sin exponer la clave ni CUIT de terceros.
- **Un test existente cambia de intención (esperado):**
  `ActivateLicenseCommandTests.Handle_CuitDistinto_RechazaConMensajeClaro_YNoPersisteNada` (renombrado a `Handle_CuitDistinto_RechazaConMensajeClaro_YAuditaElRechazoSinPii`) afirmaba
  `_repo.DidNotReceive().SaveChangesAsync(...)`. Con B, la rama de rechazo **persiste la fila de auditoría** → el
  test se **reescribió**: la `Company` **no** se muta (`LicenseKey == null`, `_current` sigue `Free`) y se escribió
  **una** fila `"LicenseActivationRejected"`; `SaveChangesAsync` **ahora sí** se invoca (para el audit). No es
  regresión: es el nuevo contrato.

## Fuera de alcance

- Hacer `Company` auditable de forma general (si alguna vez se quiere, es otro ADR que extienda ADR-003 y parchee
  `CompanyIdOf`), y auditar otras ediciones de `Company` (CUIT/branding/timezone/dunning).
- Un `IAuditTrail` genérico reutilizable: no se construye un framework para un único caller (simplicidad > cleverness).
- Enlazar F1-D8 (PIN para cambiar el CUIT): es otra deuda/decisión del usuario; este ADR solo registra el evento.

## Referencias

- Handler: `src/GymForge.Application/UseCases/Licensing/ActivateLicenseCommand.cs` (`Handle`, `EnsureKeyBelongsTo`)
- Modelos de licencia: `src/GymForge.Application/UseCases/Licensing/LicenseModels.cs` (`LicenseState`) · `LicenseService.cs`
- Interceptor y marker: `src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` ·
  `src/GymForge.Domain/Entities/IAuditable.cs` · `AuditLog.cs`
- Seam de persistencia: `src/GymForge.Application/Interfaces/IRepositories.cs` (`ISiteRepository`) ·
  `src/GymForge.Infrastructure/Repositories/SiteRepository.cs`
- Actor/clock: `src/GymForge.Application/Interfaces/ICurrentUser.cs` · `IRepositories.cs` (`IClock`)
- Tests: `tests/GymForge.Application.Tests/Handlers/ActivateLicenseCommandTests.cs` ·
  `tests/GymForge.Integration.Tests/AuditInterceptorIntegrationTests.cs`
- ADRs relacionados: `docs/adr/ADR-003-...md` (§AuditLog / `IAuditable`, "Company fuera") ·
  `docs/adr/ADR-013-...md` (minimización de PII) · estado: `docs/PROGRESS.md` · `docs/TASKS.md`
