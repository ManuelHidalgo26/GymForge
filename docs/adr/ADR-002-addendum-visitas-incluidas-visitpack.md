# ADR-002 · Addendum — Visitas incluidas en planes VisitPack (NEW-41)

**Status:** Accepted
**Date:** 2026-11-11
**Deciders:** arqui (planner) · implementa `dev` (TDD)
**Extiende:** `ADR-002-multi-tenant-y-correctitud-core.md` §1 (decremento de visitas) y §2 (nota "fuera de alcance")
**Origen:** NEW-41 (D8 parte 2) — diagnóstico del barrido de deuda baja (2026-11-11). Resuelve el `TODO(ADR-002)` en `CreateMembershipCommand.cs:56`.

> **Nota de flujo.** Status `Accepted` siguiendo el patrón de los addenda de ADR-002 (todos entran `Accepted`
> al commitear, en el ciclo arqui → dev → qa). Este documento es la **guía de implementación** que `dev`
> ejecuta al pie; qa puede vetar. Si al implementar aparece una desviación necesaria (como pasó con la
> reconciliación de estado de socio), se corrige acá y se anota, sin cambiar el Status.

---

## Context

El pack de visitas (`MembershipBasis.VisitPack`) es una modalidad de plan: el socio compra N entradas
y cada check-in descuenta una. El mecanismo de **consumo** ya existe y es correcto desde la Sesión 1
(ADR-002 §1):

- `Membership.VisitsRemaining` es `int?` (`Membership.cs:36`). `null` = ilimitado dentro de la vigencia;
  un número = visitas que quedan.
- `Membership.DecrementVisit()` (`Membership.cs:183`): `null` → no-op; `<= 0` → excepción; si no, resta 1.
- El Gatekeeper (`ValidateSwipeUseCase.cs:157-170`) deniega con `NoVisitsLeft` cuando `VisitsRemaining is 0`
  y decrementa + `Update` cuando `VisitsRemaining > 0`, **antes** del `AppendAsync` (una sola transacción).
- `AccessDenialReason.NoVisitsLeft` existe (`AccessEnums.cs:31`) y su texto está cableado
  ("Pack de visitas agotado", `AccessQueries.cs:26`).

**El defecto (NEW-41).** Nadie **siembra** `VisitsRemaining` al crear la membresía, así que una membresía
de pack nace con `VisitsRemaining == null` y el Gatekeeper la trata como **ilimitada**: nunca deniega,
nunca decrementa. El camino `NoVisitsLeft` y el decremento son, hoy, **inalcanzables en producción**.

**Causa raíz verificada.** `MembershipType` **no tiene ningún campo con el tamaño del pack**
(`MembershipType.cs`): tiene `ClassCreditsIncluded` (créditos de clases, otra cosa), `PtSessionsIncluded`
(sesiones de PT) y `DurationValue`/`DurationUnit` (vigencia temporal). El seed crea "Pack 10 Visitas"
(`DatabaseSeeder.cs:74`) con `MembershipBasis.VisitPack` y duración de 1 mes por default, pero **el 10 no
existe en ningún lado**: es solo parte del nombre. Entonces, aunque `CreateMembershipCommand` quisiera
sembrar, no tiene de dónde leer la cantidad.

Hechos del código verificados (para que el fix sea exacto):

- Dos —y solo dos— handlers de producción construyen membresías, ambos vía `Membership.Create(...)`:
  - `CreateMembershipCommand.cs:57` (alta de membresía; lo usa la API `POST /api/memberships` y flujos de alta).
  - `SellMembershipCommand.cs:87` (venta + cobro; es el **camino real de la UI** de Caja/POS y del
    "alta + cobro en un paso"). Tiene un guard `if (plan.Price <= 0)`: los packs tienen precio, así que pasan.
- `Membership.Create(...)` **ya acepta** `int? visitsRemaining = null` (`Membership.cs:59`): no hay que
  tocar la entidad `Membership` ni su factory. Falta pasarle el valor del plan.
- `MembershipType.Create(...)` (`MembershipType.cs:41`) valida nombre y precio; es el patrón de factory del repo.
- La config EF de `MembershipType` (`MembershipConfiguration.cs:35-50`) mapea `Basis` como texto; las
  columnas `int?` (`ClassCreditsIncluded`, `AgeMin`) se mapean por convención sin config explícita.
- El CRUD de Planes es **Crear + Activar/Desactivar**: no hay comando de edición
  (`PlanCommands.cs`, `PlansViewModel.cs`, `PlansView.axaml`). No se agrega edición en esta tanda.
- Estados persistidos como texto (`HasConversion<string>()`): irrelevante acá (no tocamos enums de estado),
  pero se anota por la lección de la reconciliación (addendum anterior).

---

## Decision

### 1. Campo nuevo en `MembershipType`: `VisitsIncluded` (`int?`)

```csharp
// MembershipType.cs
public int? VisitsIncluded { get; private set; }
```

- **Tipo:** `int?` nullable. Semántica: **solo tiene sentido para `VisitPack`**. Para cualquier otra
  modalidad (Renewal, OpenEnded, Trial, ClassPack, etc.) queda `null` e ignorado.
- **Por qué `int?` y no `int` default 0:** coherencia con los otros "incluidos" opcionales del mismo entity
  (`ClassCreditsIncluded`, `AgeMin`/`AgeMax` son `int?`), y porque `null` expresa "no aplica" para los planes
  de tiempo sin ensuciar la columna con un `0` engañoso. (Decisión explícita: no replicar el `int` default 0
  de `PtSessionsIncluded`.)
- **Invariante normalizada:** el valor solo se guarda para `VisitPack`; para el resto se fuerza a `null`
  aunque el llamador pase algo.

### 2. Validación: fail-closed, en dos capas

Un `VisitPack` **sin** una cantidad positiva es una **misconfiguración**, no un "pack ilimitado". Se rechaza.

**Capa dominio (belt) — factory `MembershipType.Create`.** Agregar un parámetro opcional y validar:

```csharp
public static MembershipType Create(
    Guid companyId,
    string name,
    MembershipBasis basis,
    decimal price,
    int durationValue = 1,
    string durationUnit = "Month",
    int? visitsIncluded = null)          // ← nuevo, al final (no rompe llamadas posicionales existentes)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    if (price < 0) throw new ArgumentException("Price cannot be negative.");
    if (basis == MembershipBasis.VisitPack && visitsIncluded is not > 0)
        throw new ArgumentException("Un pack de visitas requiere una cantidad de visitas mayor a cero.");

    return new MembershipType
    {
        // ... lo actual ...
        VisitsIncluded = basis == MembershipBasis.VisitPack ? visitsIncluded : null,
    };
}
```

**Capa comando (suspenders) — `CreatePlanCommandValidator`.** Para que el error llegue limpio a la UI antes
de tocar el dominio:

```csharp
When(x => x.Basis == MembershipBasis.VisitPack, () =>
    RuleFor(x => x.VisitsIncluded)
        .NotNull().GreaterThan(0)
        .WithMessage("Indicá cuántas visitas incluye el pack."));
```

`CreatePlanCommand` gana `int? VisitsIncluded` (ver 5).

### 3. Siembra en los handlers: un helper de dominio, usado por los dos

Para no duplicar el `switch` por modalidad en cada handler (mismo criterio que ADR-002 §2 con `CalculateEndDate`),
la regla vive en `MembershipType`:

```csharp
// MembershipType.cs
/// <summary>Visitas con las que nace una membresía de este plan: el tamaño del pack para VisitPack;
/// null (ilimitado dentro de la vigencia) para el resto. Un VisitPack sin visitas configuradas
/// (fila legada, anterior a este campo) se rechaza fail-closed en vez de nacer "ilimitado".</summary>
public int? InitialVisits() => Basis == MembershipBasis.VisitPack
    ? (VisitsIncluded is > 0 ? VisitsIncluded
        : throw new InvalidOperationException(
            "El pack de visitas no tiene configurada la cantidad de visitas. Editá o recreá el plan."))
    : null;
```

- **`CreateMembershipCommand.cs:56-64`:** borrar el `TODO(ADR-002)` y pasar `visitsRemaining: plan.InitialVisits()`
  a `Membership.Create(...)`.
- **`SellMembershipCommand.cs:87-89`:** idem — pasar `visitsRemaining: plan.InitialVisits()` a `Membership.Create(...)`.

**Comportamiento exacto y bordes:**
- Plan de tiempo (Renewal/OpenEnded/Trial/…): `InitialVisits()` → `null` → `VisitsRemaining = null`.
  **Idéntico a hoy** (el Gatekeeper saltea el gate de pack).
- Plan `VisitPack` bien configurado (N > 0): `VisitsRemaining = N`. El gate `NoVisitsLeft` (en 0) y el
  decremento (> 0) quedan **alcanzables**.
- Plan `VisitPack` **legado** con `VisitsIncluded == null` (fila anterior a la migración): `InitialVisits()`
  **lanza** `InvalidOperationException`. Ambos handlers ya propagan esa excepción y los VMs la muestran
  ("El pack de visitas no tiene configurada la cantidad de visitas…"). Se elige **fallar** antes que vender
  un pack silenciosamente ilimitado.

### 4. Migración: aditiva, sin backfill

- **Nombre:** `AddVisitsIncludedToMembershipType` (sigue el patrón `AddMemberTagSerialUniqueIndex`,
  `AddCompanyDunningConfig`). Generar con `dotnet ef migrations add AddVisitsIncludedToMembershipType
  --project src/GymForge.Infrastructure --startup-project src/GymForge.Infrastructure` (design-time factory).
- **Forma:** `ALTER TABLE "MembershipTypes" ADD "VisitsIncluded" INTEGER NULL`. Columna nullable, default
  `null`. **Aditiva**: no reescribe tabla en SQLite, portable a PostgreSQL cloud, no rompe bases existentes.
- **Datos legados — decisión: NO hay backfill.** La feature estuvo **latente** (los packs nunca
  decrementaron), así que ningún piloto pudo depender de que un pack se agotara. Poner un número por
  default (p. ej. 10) sería **adivinar**, y un valor silenciosamente incorrecto es peor que fallar. Un plan
  `VisitPack` preexistente queda en `VisitsIncluded = null` y:
  1. se corrige configurándolo (recrear el plan con visitas; ver 5), y
  2. si se intenta vender sin corregirlo, el guard fail-closed de `InitialVisits()` (punto 3) bloquea la
     venta con mensaje claro. No se vende un pack roto.
- El seed demo (`DatabaseSeeder.cs:74`) pasa a crear "Pack 10 Visitas" con `visitsIncluded: 10`.

### 5. UI de Planes: campo condicional "Visitas incluidas" (dev, sin ux)

Es un numeric field condicional: **alcanza `dev`** (ux puede pulir después; no bloquea).

- `CreatePlanCommand`: agregar `int? VisitsIncluded` (al final del record).
- `PlansViewModel`:
  - `[ObservableProperty] private int _visitsIncluded = 10;`
  - `public bool IsVisitPack => Basis == MembershipBasis.VisitPack;` y en el `partial void OnBasisChanged(...)`
    hacer `OnPropertyChanged(nameof(IsVisitPack))`.
  - En `CreateAsync`, pasar `IsVisitPack ? VisitsIncluded : (int?)null` al `CreatePlanCommand`.
- `PlansView.axaml`: un `NumericUpDown` "Visitas incluidas" (`Minimum="1"`) con
  `IsVisible="{Binding IsVisitPack}"`, junto a "Duración".
- `MembershipTypeDto` (`CatalogDtos.cs`): agregar `int? VisitsIncluded` y mapearlo en `FromEntity`
  (sirve para mostrar el pack en la grilla y para prefills futuros). **Churn:** rompe los 2 constructores
  posicionales de test (`CreateMemberViewModelPhotoTests.cs:138`, `RegisterSaleViewModelNoticeTests.cs:34`):
  agregarles el arg (`VisitsIncluded: null`). Columna "Visitas" en la grilla de Planes: opcional (nice-to-have).

**Fuera de alcance (anotar, no hacer):** comando de **edición** de plan. Hoy el CRUD es Crear + Activar/Desactivar.
Corregir un plan legado = recrearlo con visitas y desactivar el viejo. Un `UpdatePlanCommand` es una tarea aparte.

### 6. Gatekeeper: no se toca

Confirmado: con `VisitsRemaining` sembrado, los caminos ya existentes de `ValidateSwipeUseCase` quedan
alcanzables **sin cambios**:
- `VisitsRemaining is 0` → `NoVisitsLeft` (deniega, no decrementa, append-only).
- `VisitsRemaining > 0` → `DecrementVisit()` + `Update` + `AppendAsync` (una transacción, ADR-002 §1).
- `VisitsRemaining is null` (planes de tiempo) → ambos `if` son falsos → sin decremento. **Invariante
  preexistente respetado.**

**NO TOCAR:** el contrato del Gatekeeper (`ValidateSwipeUseCase`), la allowlist de estado de socio del
addendum NEW-25, el guard D5 de biblioteca global, `AccessLog` append-only, ni `Membership.DecrementVisit`.

---

## Tests (`dev`)

Nuevos / a cubrir:
- **Domain (`MembershipTypeTests`):** `Create` con `VisitPack` + `visitsIncluded: 10` → `VisitsIncluded == 10`;
  `Create` con `VisitPack` + `null`/`0` → `ArgumentException`; `Create` con `Renewal` + `visitsIncluded: 5`
  → `VisitsIncluded == null` (se normaliza); `InitialVisits()` para VisitPack → N, para Renewal → null,
  para VisitPack legado (null) → `InvalidOperationException`.
- **Application (`CreateMembershipCommandTests`):** plan `VisitPack` (10) → la membresía creada tiene
  `VisitsRemaining == 10`; plan de tiempo → `VisitsRemaining == null` (regresión del comportamiento actual).
- **Application (`SellMembershipCommandTests`):** vender un plan `VisitPack` (10) → membresía con
  `VisitsRemaining == 10`; plan de tiempo → `null`.
- **Application (`CreatePlanCommand`/validator):** `VisitPack` sin `VisitsIncluded` → falla la validación;
  con `VisitsIncluded > 0` → crea y persiste el valor.
- **Integration (end-to-end, opcional pero valioso):** vender/crear un `VisitPack` real sobre SQLite y luego
  un swipe → `VisitsRemaining` baja a N-1 persistido (encadena con `GatekeeperIntegrationTests`).

Regresión a mantener verde (no deberían cambiar de comportamiento, solo de firma de seed):
- `GatekeeperTests` de visitas (decremento/`NoVisitsLeft`) siguen armando la membresía con visitas
  explícitas: no pasan por los handlers, quedan verdes.
- `MembershipStateMachineTests.OpenEnded_DecrementVisit_IsNoOp` y `VisitPack_*`: sin cambios.

Churn mecánico por el nuevo arg del factory (solo call sites `VisitPack`):
- `DatabaseSeeder.cs:74` → `visitsIncluded: 10`.
- `GatekeeperIntegrationTests.cs:73` (plan "Pack de visitas") → `visitsIncluded: 10`.
- `TenantAdversarialIntegrationTests.cs:286` (plan "Pack B") → `visitsIncluded: 10`.
Los `Create` de modalidades NO-pack **no cambian** (el parámetro es opcional y va al final).

---

## Consequences

- **+** Los packs de visitas funcionan de punta a punta: se venden con N, cada check-in descuenta, y a 0
  se deniega con "Pack de visitas agotado". Se desbloquea una modalidad de plan que estaba muerta.
- **+** La regla ("cuántas visitas") vive en `MembershipType` (donde ya viven duración y créditos de clase),
  no duplicada en dos handlers.
- **+** Fail-closed: un pack mal configurado no se vende como "ilimitado"; obliga a configurarlo.
- **−** Un plan `VisitPack` legado (null) deja de venderse hasta recrearlo con visitas. Aceptable: la feature
  nunca funcionó, así que no hay packs reales en uso.
- **−** Churn menor: 3 call sites de seed/tests de `VisitPack` y 2 constructores de `MembershipTypeDto`.
- **Latente (no en alcance, ya registrado):** D9 — con el DbContext de vida larga del Desktop, el `Update`
  del decremento opera sobre la instancia trackeada; es el mismo riesgo que D9 documenta para cualquier
  `Update`. Este addendum no lo agrava más allá de volver alcanzable el camino; D9 se resuelve por su cuenta.

---

## Referencias

- `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md` §1 (decremento atómico) y §2 (nota "fuera de alcance").
- `docs/TASKS.md` — NEW-41 (D8 parte 2); D9 (DbContext de vida larga).
- CLAUDE.md — Reglas absolutas (offline-first, append-only de accesos, un commit por feature).
