# ADR-002 · Addendum — Estado del socio en el Gatekeeper (NEW-25)

**Status:** Accepted
**Date:** 2026-11-10
**Deciders:** arqui (planner) · implementa `dev` (TDD)
**Extiende:** `ADR-002-multi-tenant-y-correctitud-core.md` §3.4 (Gatekeeper)
**Origen:** NEW-25 (barrido de deuda, pasada 1) — bug confirmado por dev

---

## Context

`ValidateSwipeUseCase` resuelve el socio pero **nunca mira `Member.Status`**. Las 8 validaciones
en secuencia (membresía activa, vencida, deuda, horario, puerta, anti-passback, visitas) solo
consultan la membresía. La única puerta de membresía es `GetCurrentActiveAsync`, que filtra por
`Membership.Status == Active`, **no** por el estado del socio.

**Consecuencia (alcanzable):** un socio `Suspended`/`Frozen` con una membresía `Active` **entra**.
El vector está abierto por D14: `SellMembershipCommand` vende a un socio `Suspended`/`Frozen` sin
tocar su estado (D14 cerrado: "Frozen/Suspended no se tocan"), creando una membresía `Active` sobre
un socio no habilitado.

Hechos del código verificados (para que el fix sea exacto):

- `AccessDenialReason` (`src/GymForge.Domain/Enums/AccessEnums.cs`) **ya tiene** el valor `Suspended`
  (ordinal 9, último) y `AccessMessages.Denial` (`AccessQueries.cs:27`) **ya mapea** su texto:
  "Acceso suspendido — ver recepción". Es decir: el motivo existe y está cableado, pero **nadie lo
  produce** hoy.
- `MemberStatus`: `Prospect, Active, Suspended, Frozen, Overdue, Expired, Cancelled`.
- En **producción** nadie llama `Member.Suspend()`, `Member.Freeze()`, `Member.Unfreeze()` ni
  `Member.Cancel()` (el "Cancelar" de la ficha cancela la **membresía**, no al socio). Estados
  realmente alcanzables hoy: `Prospect` (alta), `Active` (`Activate`), `Overdue` (`MarkOverdue` del
  job nocturno) y vuelta a `Active` (`ClearArrears`). `Suspended`/`Frozen`/`Expired`/`Cancelled` a
  nivel socio son **latentes** (solo tests), coherente con Q3 ("nadie llama `Member.Freeze()`").
- Por lo tanto el fix es **defensa en profundidad**: cierra el agujero el día que exista un caller
  (UI de sanción, importador, job), sin esperar a que la vulnerabilidad sea explotada en campo.
- `AccessLog` es `ISiteScoped` (ADR-002 §5) y `AccessLogRepository.AppendAsync` ya hace
  `SaveChangesAsync`. Registrar un denegado es un **INSERT** (append-only, regla 3 del CLAUDE.md).
- Todos los `GatekeeperTests` arman el socio con `MakeActiveMember()` (`.Activate()` ⇒ `Active`), así
  que el gate propuesto **no rompe** ningún test existente (ni `NoActiveMembership_...`: socio Active
  ⇒ pasa el gate de estado ⇒ sigue cayendo en `IncompleteMembership`).

**No confundir con Q5.** Q5 pide `AccessDenialReason.Frozen` con mensaje "Membresía congelada hasta
DD/MM" para la **membresía** congelada (socio Active, membresía Frozen). Es **otro caso** y otra capa.
Este addendum es sobre el **estado del socio**. Los nombres elegidos evitan la colisión.

---

## Decisión

### 1. Qué estados deniegan — allowlist fail-closed `{Active, Overdue}`

El socio entra **solo** si `Member.Status ∈ { Active, Overdue }`. Cualquier otro estado **deniega**.
Esto incluye `Suspended` **y** `Frozen` (ambos deniegan), más `Prospect`, `Expired`, `Cancelled` y
cualquier estado futuro.

Fundamento por estado:

- **Active** → habilitado. Entra (sujeto a las demás validaciones).
- **Overdue** → "activo pero debe": **debe entrar**; la decisión por deuda es por **monto**
  (`StopOnOweAmount`/`WarnOnOweAmount`), no por estado. Por eso Overdue va en el allowlist: deja que
  el gate de deuda produzca el motivo preciso (`Owing`) con el importe. Bloquearlo acá perdería ese
  mensaje. Es la razón por la que **no** es un denylist crudo.
- **Suspended** → sanción. Denegar (semántica inequívoca).
- **Frozen** (nivel socio) → socio en pausa/baja temporal. Denegar es coherente: un socio congelado
  explícitamente no entrena. Latente hoy (Q3), pero el gate lo cubre sin costo.
- **Prospect / Expired / Cancelled** → no es un socio entrenando ahora (lead / lapso / ex-socio).
  Denegar y derivar a recepción es correcto.

**Por qué allowlist y no "denegar solo Suspended y Frozen" (denylist):** el denylist es exactamente
la causa de NEW-25 — un estado no contemplado **otorga por omisión**. Si mañana aparece un
`Banned`/`Expelled`, un denylist lo dejaría pasar en silencio hasta que alguien lo recuerde.
Fail-closed (allowlist) convierte el default en **denegar**, que es la postura correcta para control
de acceso. Trade-off asumido: si se agrega un estado legítimo de "puede entrenar" (p. ej. `Trial`),
hay que sumarlo al allowlist; es un fix-forward barato y en **un único lugar** frente al riesgo de un
grant silencioso.

### 2. Motivo de denegación — reusar `Suspended` + un genérico `MemberInactive`

- `Suspended` → reusar `AccessDenialReason.Suspended` (ya existe, texto ya cableado:
  "Acceso suspendido — ver recepción"). Cero texto nuevo para el caso reachable.
- Resto del catch-all (`Frozen`, `Prospect`, `Expired`, `Cancelled`, futuros) → **nuevo** valor
  `AccessDenialReason.MemberInactive` con texto **"Cuenta inactiva — ver recepción"**.

Total: **2 motivos, 1 reusado + 1 nuevo.** No se nombra `MemberFrozen` (evita chocar con el `Frozen`
de membresía de Q5). No se crea un motivo por estado (Frozen/Cancelled/Expired son latentes;
multiplicar enum + textos para rutas muertas viola "simplicidad > cleverness"). Si un estado se
vuelve reachable y pide su mensaje propio, se desdobla ahí.

> **Nota de persistencia para dev:** `AccessLog.DenialReason` guarda el enum como `int`. Agregar
> `MemberInactive` **al final** del enum (ordinal 10, después de `Suspended`) para no correr los
> ordinales ya persistidos. No insertar en el medio.

### 3. Posición en la secuencia — primer gate tras resolver el socio

El chequeo va **inmediatamente después** del bloque `if (member is null)` (ValidateSwipeUseCase L94)
y **antes** del gate "2. Membresía activa" (L96). Fundamento:

- El estado del socio es **ortogonal** a la membresía: un suspendido se deniega tenga o no membresía
  activa, deuda o visitas.
- Da el motivo más autoritativo ("ver recepción") y no filtra detalles de membresía/deuda a quien no
  corresponde evaluar.
- **Corta antes** de cualquier consulta posterior (membresía, cargos, anti-passback) y **antes** del
  decremento de visita → sin efectos colaterales para un denegado.
- Cubre el vector exacto de NEW-25 (socio Suspended/Frozen + membresía Active), que hoy pasa porque
  el gate de membresía otorga.

### 4. Append-only / AccessLog

El denegado se registra por el helper existente `AppendDenied(request, member.Id, reason, ct)` →
`AccessLog.Denied(...)` → `AppendAsync` (que ya hace `SaveChanges`). Es un **INSERT** idéntico a los
demás motivos: sin UPDATE ni DELETE (regla 3 del CLAUDE.md intacta). Como `member` es no-null acá, se
usa `AccessLog.Denied` (con `member.Id`), **no** `DeniedUnknown`. `AccessLog` es `ISiteScoped`, así
que queda filtrado por empresa+sede como el resto. El `AccessDecision` devuelto es
`Granted=false` + el motivo, misma forma que las otras denegaciones.

---

## Archivos (para dev)

- `src/GymForge.Domain/Enums/AccessEnums.cs` — agregar `MemberInactive` **al final** de
  `AccessDenialReason`.
- `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs` — nuevo gate de estado tras
  resolver el socio (antes del gate de membresía). Mapear `Member.Status` →
  `{ Active, Overdue } = continuar`; `Suspended = AccessDenialReason.Suspended`; cualquier otro
  `= AccessDenialReason.MemberInactive`; en denegado llamar `AppendDenied(..., member.Id, reason, ct)`
  y `Deny(member.Id, member.FullName, member.PhotoUrl, reason)`.
- `src/GymForge.Application/UseCases/Access/AccessQueries.cs` — agregar
  `AccessDenialReason.MemberInactive => "Cuenta inactiva — ver recepción"` al switch `Denial`
  (el de `Suspended` ya existe).

## Tests (TDD, `dev`)

**Unit — `tests/GymForge.Application.Tests/GatekeeperTests.cs`** (helper nuevo: partir de
`MakeActiveMember()` y luego `.Suspend()` / `.Freeze()`; `Freeze()` exige Active, que el helper ya da):

1. `SuspendedMember_ActiveMembership_Denied` — socio `Suspended` + membresía `Active` ⇒
   `Granted == false`, `DenialReason == Suspended`; `_accessLogRepo.Received(1).AppendAsync` con
   `!AccessGranted && MemberId == member.Id && DenialReason == Suspended`; y **no** se decrementa
   visita ni se llama `_membershipRepo.Update` ni se emite granted log.
2. `FrozenMember_ActiveMembership_Denied` — socio `Frozen` + membresía `Active` ⇒ `Granted == false`,
   `DenialReason == MemberInactive`; AccessLog denegado escrito.
3. `CancelledMember_ActiveMembership_Denied` — documenta el catch-all fail-closed ⇒ `MemberInactive`.
4. `SuspendedMember_NoActiveMembership_DeniedAsSuspendedNotIncomplete` — socio `Suspended`,
   `GetCurrentActiveAsync` devuelve null ⇒ `DenialReason == Suspended` (prueba que el gate corre
   **antes** del de membresía).
5. `OverdueMember_SmallDebt_Granted` — socio `Overdue` con `owing < StopOnOweAmount` ⇒ `Granted ==
   true` (prueba que el allowlist incluye Overdue y que no se rompió el flujo de deuda).
6. Regresión: `HappyPath_ValidRfidSwipe_GrantsAccess` (socio Active) sigue en verde.

**Integration — `tests/GymForge.Integration.Tests/GatekeeperIntegrationTests.cs`:**

7. `SuspendedMember_ActiveMembership_WritesDeniedAccessLog` — swipe real sobre SQLite ⇒ exactamente
   **1 fila** en `AccessLogs` con `MemberId == member`, `AccessGranted == false`,
   `DenialReason == Suspended`; si la membresía es pack, `VisitsRemaining` sin cambios (prueba
   persistencia append-only + ausencia de efectos colaterales).

`dotnet test GymForge.sln` verde es condición de cierre (regla 6 del CLAUDE.md).

---

## Consequences

- **+** El Gatekeeper deja de otorgar a socios no habilitados; queda **fail-closed** por estado.
- **+** Defensa en profundidad: el día que exista un caller de `Suspend()`/`Freeze()`/sanción, ya está
  cubierto, sin regresión silenciosa.
- **+** Recepción ve un motivo accionable ("ver recepción") y específico para suspendidos.
- **−** Un estado futuro legítimo de "puede entrenar" debe sumarse al allowlist (fix-forward en un
  único punto). Asumido: es más seguro que un grant por omisión.
- **Nota:** no resuelve Q5 (motivo propio para **membresía** congelada); son capas distintas.

---

## Referencias

- `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md` §3.4 (Gatekeeper scopeado por sede)
- CLAUDE.md — Reglas absolutas 2 (multi-tenant) y 3 (AccessLog append-only); Algoritmo Gatekeeper
- `docs/TASKS.md` — NEW-25 (este fix), D14 (vector), Q3 (reachability de Frozen), Q5 (Frozen de membresía)
