# ADR-002 · Addendum — Reconciliación de estado de socio en bases legadas (NEW-31 / O-3)

**Status:** Accepted
**Date:** 2026-11-10
**Deciders:** arqui (planner) · implementa `dev` (TDD)
**Extiende:** `ADR-002-multi-tenant-y-correctitud-core.md` §3.4 (Gatekeeper)
**Depende de:** `ADR-002-addendum-estado-socio-gatekeeper.md` (NEW-25) · D14 (activación del socio al vender)
**Origen:** NEW-31 (O-3, barrido de deuda, pasada 3) — efecto colateral de NEW-25 sobre pilotos legados
**Implementado:** commit `d7852bf` (`feat: [db] reconciliar socios Prospect con membresia activa en el arranque (NEW-31)`), 2026-11-10. qa: APROBADO CON OBSERVACIONES, sin bloqueantes. Deuda derivada: NEW-37, NEW-38, NEW-39 (ver sección "Implementación" al final).

---

## Context

NEW-25 convirtió el gate de estado del socio en un **allowlist fail-closed `{Active, Overdue}`**
(`ValidateSwipeUseCase.CanEnter`). Efecto colateral sobre **bases de pilotos legadas** (anteriores al
commit D14, que activa al socio al vender una membresía — `SellMembershipCommand.ApplyArrearsRelief`,
`Prospect/Expired/Cancelled → Member.Activate()`):

- Pueden existir socios con `Member.Status == Prospect` que ya tienen una `Membership` con
  `Status == Active`. Son **socios legítimos** cuyo estado quedó con el valor viejo porque se vendió el
  plan antes de que existiera D14.
- **Antes de NEW-25** entraban (el Gatekeeper no miraba `Member.Status`). **Después** el kiosk los
  deniega con `MemberInactive` ("Cuenta inactiva — ver recepción"). Regresión funcional para datos reales.

Hechos del código verificados (para que el fix sea exacto):

- **Estados de socio alcanzables en producción** (NEW-25 addendum, Q3): `Prospect` (alta), `Active`
  (`Activate`), `Overdue` (`MarkOverdue`), vuelta a `Active` (`ClearArrears`). `Suspended/Frozen/
  Expired/Cancelled` a nivel socio son **latentes** (solo tests). De los alcanzables, el **único** que
  el allowlist deja afuera teniendo una membresía `Active` es **`Prospect`**. No hay otro combo legado.
- `Member.Status` tiene **private setter**: desde Infrastructure no se puede setear por la API de la
  entidad sin un método de dominio. Las dos únicas vías son (a) un método de dominio o (b) SQL directo.
- `Member.Activate(DateOnly joinDate)` hace **tres** cosas: `Status = Active`, `JoinDate ??= joinDate` y
  `AddDomainEvent(MemberActivatedEvent(...))`. El evento afirma "activado ahora" — **falso** para un socio
  activado hace meses. Y exige fabricar una `joinDate`.
- **Los domain events hoy no se despachan**: nadie lee `BaseEntity.DomainEvents` en el DbContext ni hay
  outbox; `IEventBus` se publica **explícito** en los use cases (p. ej. `MemberCheckedIn` en el
  Gatekeeper). O sea: `MemberActivatedEvent` es inerte hoy, pero apoyarse en eso es frágil (si mañana se
  agrega un dispatcher, un fix de boot dispararía N eventos).
- `AuditSaveChangesInterceptor` atribuye cada fila a `ICurrentUser.StaffId`. **En el arranque no hay
  sesión de cajero** → la atribución sería vacía/sin sentido. El interceptor solo dispara sobre
  `SaveChanges` de entidades trackeadas; **SQL directo no lo atraviesa** (consistente con "no auditar").
- Precedente del patrón "tarea de datos en el boot": `MemberDocumentMigrationGuard` (NEW-16) —
  `internal static`, SQL crudo, cross-tenant (sin filtro), `LogCritical` con **conteos, sin PII**. La
  diferencia: aquel es **solo-lectura**; este **muta**.

---

## Decisión

### 1. Dónde corre — automático en el arranque, después de `MigrateAsync`

Reconciliación **automática en `InitialiseDatabaseAsync`**, no herramienta aparte. Para el piloto,
recepción **no debe correr nada**: la reparación es determinista, idempotente y acotada. Una herramienta
manual quedaría sin ejecutar y la regresión persistiría.

Posición exacta en `InfrastructureServiceExtensions.InitialiseDatabaseAsync`, **inmediatamente después**
de `await db.Database.MigrateAsync();` y **antes** de `await seeder.SeedAsync();`:

```
backup.EnsureHealthy();
await BaselineLegacyDatabaseAsync(db);
await MemberDocumentMigrationGuard.EnsureNoDuplicatesAsync(db, logger);
await db.Database.MigrateAsync();
await MemberStatusReconciliation.ReconcileAsync(db, backup, logger);   // ← NEW-31
await seeder.SeedAsync();
...
backup.BackupIfDue();
```

Va **después** de `MigrateAsync` (no antes, a diferencia del guard de DNIs): el guard de DNIs es una
**precondición de una migración**; esto es una **reparación de semántica de datos** que necesita el
schema ya vigente. Antes del seeder porque el seeder solo crea datos nuevos (con estado correcto) y no
interactúa con socios existentes; correr antes deja la base sana cuanto antes.

### 2. Condición de detección — `Prospect` con ≥1 membresía `Active`, y nada más

Se promueve un socio **si y solo si**:

- `Member.Status == MemberStatus.Prospect`, **y**
- existe ≥1 `Membership` suya con `Status == MembershipStatus.Active`.

Confirmado que **no hay otros combos**: de los estados alcanzables, solo `Prospect` queda fuera del
allowlist; `Active`/`Overdue` ya entran. **Nunca** se tocan `Suspended/Frozen/Expired/Cancelled` aunque
tuvieran una membresía activa — promoverlos pisaría una sanción/baja explícita (y además son latentes).
Un **`Prospect` sin membresía activa es un lead legítimo** y **no se toca**. No se filtra por `EndDate`
de la membresía: el patrón legado es "tiene plan vendido activo"; la vigencia la sigue resolviendo el
gate `Expired` del Gatekeeper (ortogonal). Mantener la condición mínima = mismo criterio que D14.

### 3. Cómo promueve — `UPDATE` SQL directo a `Status`, **no** `Member.Activate()`

Mutación mínima correcta: **un `UPDATE` SQL** que setea solo `Status` (+ `UpdatedAt`), **no** el método
de dominio. Fundamento:

- `Activate()` emitiría **N `MemberActivatedEvent`** ("activado ahora", semánticamente falso para una
  activación histórica) y forzaría a fabricar una `JoinDate`. No queremos ninguna de las dos en un fix
  masivo de boot, ni acoplar la reparación a un futuro dispatcher de eventos.
- El `UPDATE` set-based es el enfoque idiomático de migración de datos: **una sentencia**, naturalmente
  **idempotente** y **cross-tenant**, que toca **solo** `Status`. Es la mutación mínima.

```sql
UPDATE Members
SET Status = {"Active"},                              -- MemberStatus.Active.ToString()
    UpdatedAt = {DateTime.UtcNow}                     -- trazabilidad: marca "tocado por la reconciliación"
WHERE Status = {"Prospect"}                           -- MemberStatus.Prospect.ToString()
  AND EXISTS (SELECT 1 FROM Memberships ms
              WHERE ms.MemberId = Members.Id
                AND ms.Status = {"Active"});        -- MembershipStatus.Active.ToString()
```

> **Guardrail para dev (corregido 2026-11-10):** los enums se persisten como **texto** (nombre del enum),
> no como `int`. `MemberConfiguration` y `MembershipConfiguration` usan `.HasConversion<string>()` sobre
> `Status` (y el resto de los enums de la base también). En la base el estado de un socio es `'Prospect'`,
> `'Active'`, etc. El addendum original proponía `(int)MemberStatus.Active` (0/1/3): **era incorrecto**. Con
> `int` el UPDATE habría sido un no-op silencioso (el `WHERE Status = 0` nunca matchea texto) y la
> reconciliación no habría reparado nada. **No** hardcodear literales: los valores salen de
> `MemberStatus.X.ToString()` / `MembershipStatus.X.ToString()` y van parametrizados
> (`ExecuteSqlInterpolatedAsync` / `SqlQuery` interpolados). Un test ancla el formato (nombre del enum) para
> que un cambio de conversión en EF no pase inadvertido. `UpdatedAt` se setea con `DateTime.UtcNow` (así está
> implementado; no pasa por `ICompanyCalendar` ni por un reloj inyectado).

### 4. Idempotencia + multi-tenant

- **Idempotente:** el `WHERE Status = Prospect` deja de matchear una vez promovidos → re-correr = no-op.
- **Alcance = toda la base, todos los tenants**, como una migración de datos. El SQL directo **ignora los
  query filters** a propósito (igual que el guard de DNIs). **No viola el aislamiento**: es reparación de
  datos de boot, no una operación de sesión; en el arranque no hay tenant activo, y cada `Prospect` se
  promueve **por sus propias** membresías (nunca se mezclan datos entre tenants). El `EXISTS` corre por
  `ms.MemberId = Members.Id`, que es intrínsecamente intra-socio → intra-tenant.

### 5. Log / auditoría — conteos sin PII, **sin AuditLog**

Igual que el guard de DNIs: un log estructurado con **conteos, sin PII** (nada de nombres/DNI). Con
filas afectadas > 0, `LogInformation` ("Reconciliación de estado de socio: {N} socio(s) Prospect con
membresía activa promovidos a Active"); con 0, silencio (o `LogDebug`). **No** se escribe `AuditLog`:

- El interceptor atribuye a `ICurrentUser.StaffId`, **vacío en el boot** → filas huérfanas, ruido.
- Es una **migración de sistema** (como las migraciones EF, el baseline o el guard de DNIs), no una
  acción de usuario. Las migraciones EF no auditan.
- El SQL directo **no atraviesa el interceptor** de todos modos → coherente.
- **Append-only intacto:** no se agrega ni se toca ninguna fila de `AuditLog` (no `UPDATE`/`DELETE`).

El registro durable es la línea de log (conteos) + la entrada en `PROGRESS.md`.

### 6. Backup previo — sí, **gated** (solo si hay algo que reparar)

Antes del `UPDATE`, **si la detección cuenta ≥1 fila**, llamar `backup.BackupNow("pre-reconciliacion-
estado-socio")`. Fundamento:

- Es la **primera mutación de boot de datos legados de producción**; un restore-point etiquetado es
  seguro barato y refleja el "hacé un backup antes" que el guard de DNIs pide al operador (allá manual;
  acá lo hacemos nosotros porque mutamos).
- **Gated** en "hay trabajo": los arranques sanos / ya reconciliados **no pagan** un snapshot extra
  (`BackupIfDue` al final ya cubre el diario). Como la operación es idempotente y angosta, el backup es
  cinturón-y-tiradores, no imprescindible — pero para datos de piloto el costo es un archivo y habilita
  el rollback del dueño si algo se ve raro.
- Encapsular el invariante "backup antes de mutar" dentro de `ReconcileAsync(db, backup, logger)`: contar
  → si >0 `BackupNow` → `UPDATE` → log. `IDatabaseBackup.BackupNow` es best-effort (nunca lanza), así que
  no bloquea el arranque si el backup falla.

### 7. ADR — este addendum

Amerita addendum (este archivo), hermano del de NEW-25: es la **primera mutación automática de datos de
producción en el boot**, cross-tenant y sin auditoría; esas tres cosas son decisiones arquitectónicas que
deben quedar registradas, no solo un comentario en la clase. Además de este ADR: comentario de cabecera en
`MemberStatusReconciliation` + entrada en `PROGRESS.md`/`TASKS.md` al cerrar.

---

## Archivos (para dev)

- **Nuevo:** `src/GymForge.Infrastructure/Persistence/MemberStatusReconciliation.cs` — `internal static`,
  `ReconcileAsync(GymForgeDbContext db, IDatabaseBackup backup, ILogger logger, CancellationToken ct = default)`.
  Espeja a `MemberDocumentMigrationGuard` (guard `MembersTableExistsAsync` antes de tocar nada, para no romper
  en una base nueva/sin schema). Flujo: `SqlQuery<int>` con COUNT de afectados → si >0 `backup.BackupNow("pre-reconciliacion-
  estado-socio")` → `ExecuteSqlInterpolatedAsync` con el `UPDATE` → `LogInformation` con el conteo.
- **Editar:** `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs` —
  `InitialiseDatabaseAsync`: llamar `await MemberStatusReconciliation.ReconcileAsync(db, backup, logger)`
  entre `MigrateAsync()` y `SeedAsync()`. Reutilizar el `backup` ya resuelto y el `ILogger` ya pedido para
  el guard de DNIs.

## Tests (TDD, `dev`)

**Integration — `tests/GymForge.Integration.Tests/MemberStatusReconciliationTests.cs`** (SQLite real; el
`UPDATE` set-based y el `EXISTS` se prueban contra la base, no en memoria):

1. `ProspectConMembresiaActiva_SePromueveAActive` — socio `Prospect` + `Membership.Active` ⇒ tras
   reconciliar `Member.Status == Active`.
2. `ProspectSinMembresiaActiva_QuedaProspect` — `Prospect` con membresía `Expired/Cancelled/Pending`
   (o sin membresías) ⇒ sigue `Prospect` (no se toca el lead legítimo).
3. `MiembroActiveYOverdue_SinCambios` — `Active` y `Overdue` (con o sin membresía activa) ⇒ estado intacto.
4. `EstadosSancionados_NoSePromuevenAunConMembresiaActiva` — `Suspended/Frozen/Cancelled/Expired` +
   `Membership.Active` ⇒ **no** cambian (no pisar sanción/baja).
5. `Idempotente_SegundaCorridaNoCambiaNada` — correr dos veces: la 2ª no modifica filas (y, si se chequea,
   no dispara un 2º backup porque el conteo es 0).
6. `AislamientoMultiTenant_PromuevePorSusPropiasMembresias` — Tenant A: `Prospect` **sin** membresía
   activa; Tenant B tiene membresías activas. El `Prospect` de A **no** se promueve por datos de B (el
   `EXISTS` es intra-socio). Y un `Prospect`+`Active` en cada tenant se promueve en ambos (alcance = toda
   la base).
7. `ConFilasAfectadas_HaceBackupPrevio` / `SinFilasAfectadas_NoHaceBackup` — con un `IDatabaseBackup`
   espía (NSubstitute): `BackupNow` recibido 1 vez cuando hay ≥1 afectado, 0 veces cuando no hay ninguno.
8. `NoEscribeAuditLog` — tras reconciliar, no aparecen filas nuevas en `AuditLogs` por esta operación
   (el SQL directo no pasa por el interceptor).

**Integration — `tests/GymForge.Integration.Tests/GatekeeperIntegrationTests.cs`** (cierre del loop):

9. `ProspectLegadoReconciliado_GatekeeperDejaEntrar` — sembrar `Prospect` + `Membership.Active`; el swipe
   **deniega** (`MemberInactive`); correr `ReconcileAsync`; el mismo swipe ahora **otorga** (`Granted`).
   Prueba que la reparación cierra exactamente la regresión de NEW-25.

`dotnet test GymForge.sln` verde es condición de cierre (regla 6 del CLAUDE.md).

---

## Consequences

- **+** Los pilotos legados dejan de denegar a socios legítimos: la regresión de NEW-25 se cierra sola, sin
  que recepción corra nada.
- **+** Idempotente, cross-tenant y auto-contenida; backup previo gated da rollback barato.
- **+** No fabrica eventos ni `JoinDate` falsos; no contamina `AuditLog` con filas sin autor.
- **−** Es la primera mutación automática de datos de producción en el boot: queda documentada acá y se
  cubre con backup previo + tests. Trade-off asumido (la alternativa — pedir que el operador corra algo —
  deja la regresión viva en campo).
- **Nota:** no cambia el Gatekeeper ni el allowlist de NEW-25; solo repara el **dato** para que el
  allowlist (correcto) no castigue a un socio legítimo.

---

## Implementación (2026-11-10)

- **Código:** commit `d7852bf` en `master` (sin push). Llamada en `InfrastructureServiceExtensions.cs:111`
  (`InitialiseDatabaseAsync`, entre `MigrateAsync()` y el seed). Sin migración EF, sin cambio de esquema.
- **Desviación respecto del diseño:** la conversión de enums a `int` del addendum original era incorrecta; se
  implementó con texto (ver guardrail de la sección 3). Es la única desviación de fondo.
- **Tests:** `dotnet test GymForge.sln` **1541 verdes** (Domain 107 · Application 624 · Desktop 431 · Integration 379), 0 fallos.
  Integración en `tests/GymForge.Integration.Tests/MemberStatusReconciliationTests.cs` y el cierre del loop en
  `GatekeeperIntegrationTests.cs`.
- **Deuda derivada (qa, todas BAJA):**
  - **NEW-37 (O-1):** `BackupNow` es best-effort y no expone resultado: si falla, el UPDATE corre igual sin respaldo.
    Aceptable (cambio reversible; la base ya pasó `EnsureHealthy`). Mejora: `BackupOffsiteNow()` (devuelve `LocalBackupFailed`) y abortar con warning.
  - **NEW-38 (O-3):** COUNT y UPDATE no van en una transacción; el `{Count}` del log podría diferir del real (despreciable en un boot de un proceso). Alternativa: usar el valor de retorno de `ExecuteSqlInterpolatedAsync` y evitar el COUNT previo.
  - **NEW-39 (O-4):** el nombre del archivo de backup no incluye el `reason`; el backup `pre-reconciliacion-estado-socio` solo se distingue por el log (`DatabaseBackupService.cs`).
- **Riesgo conocido compartido:** `BackupNow` hace `CopyOffsite` síncrono; en el boot de una base legada esto puede demorar la primera apertura si el destino externo es lento (ver F1-D1 en `docs/TASKS.md`).

---

## Referencias

- `docs/adr/ADR-002-addendum-estado-socio-gatekeeper.md` (NEW-25, allowlist fail-closed)
- `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md` §3.4 (Gatekeeper) · §5 (append-only)
- `src/GymForge.Infrastructure/Persistence/MemberDocumentMigrationGuard.cs` (NEW-16, patrón de boot)
- `src/GymForge.Application/UseCases/Sales/SellMembershipCommand.cs` (D14, activación al vender)
- CLAUDE.md — Reglas absolutas 2 (multi-tenant) y 3 (AccessLog/AuditLog append-only)
- `docs/TASKS.md` — NEW-31 (O-3), NEW-25, D14
