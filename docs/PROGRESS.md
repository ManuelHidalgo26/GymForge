# PROGRESS — GymForge

Bitácora del equipo entre sesiones. **Entradas nuevas arriba**; no se borra historial.
Deuda y tareas: `docs/TASKS.md`. Decisiones: `docs/adr/`. Historia por sprints y reglas del proyecto: `CLAUDE.md` (no duplicado acá).

> "Sesión N" = sesiones de este roadmap de mejora. No es lo mismo que los "Sprint" de CLAUDE.md.

---

## Contexto del roadmap

Objetivo: evaluar GymForge, mejorarlo y dejarlo "listo para un gimnasio real" con aspecto de sistema de US$100k.

- Plan: 6 fases en ~8 sesiones.
- Hito: **tras la sesión 3** se puede instalar y operar en el gimnasio piloto mientras se sigue puliendo. Condición: cerrar el bloqueador P-1 (freeze) al arrancar la sesión 3 (ver `docs/TASKS.md`). **Estado (2026-10-28): condición P-1 cumplida.** Para la puesta en marcha completa falta Fase 1.
- Flujo por sesión: arqui (ADR) → dev (implementa) → qa (aprueba o veta).
- Regla 6 de CLAUDE.md: `dotnet test` verde antes de cada commit.

## Estado general (al 2026-10-28)

- Sesiones 1, 2 y 3 (incluye P-1 y Q1): **commiteadas** en `feat/sesion-3` (11 commits desde `master`). Sin merge a `master`, sin push. ADR-001, ADR-002 y ADR-003 en `Accepted`.
- Tests: **504 verdes** (Domain 72 · Application 230 · Desktop 25 · Integration 177), cifra del orquestador tras Q1. Piso al cierre de la sesión 2: 451. La sesión de notas no re-ejecutó `dotnet test`.
- Working tree: solo los docs de la sesión 3, pendientes de commit (lo hace el orquestador). `.gitignore` ya ignora `*.lnk` y `*.pfx`.
- Abierto en `docs/TASKS.md`: **0 bloqueadores de piloto** (P-1 cerrado). Quedan: decisión Q2/D9, P-2 (UI diferida, no bloquea), 19 ítems de deuda (D1–D19), 7 de qa (Q3–Q9), 5 de UX (UX1–UX5) y las fases F1–F6.

## Roadmap por fases

| Fase | Sesiones | Contenido | Estado |
|---|---|---|---|
| 0 — Bugs y reglas de negocio | 1-2 (P-1 en 3) | Correctitud del core | **CERRADA.** S1, S2, P-1 (freeze) y Q1 commiteadas en `feat/sesion-3` |
| 1 — Robustez de producción | 3 | Backup off-site, auto-update Velopack, licencia (CUIT + fingerprint), retención de logs | PENDIENTE: no arrancó en S3 (ver "Qué sigue") |
| 2 — WhatsApp gratis + cobranzas | 4 | Pantalla de morosos, `WhatsAppLinkSender` (wa.me), `DunningConfig` en Configuración, `NotificationLog` | PENDIENTE |
| 3 — Escáner HID + check-in premium | 5 | Lector keyboard-wedge en kiosk y POS, foto del socio, teclado numérico en pantalla, arquitectura de hardware | PENDIENTE |
| 4 — Profundidad | 6-7 | Reportes reales (morosos, por plan/método/sede, retención; export Excel/PDF), editar socio desde la ficha, rutinas en la ficha, comisiones de staff | PENDIENTE |
| 5 — Pulido US$100k | 8 | Command palette (Ctrl+K), fix branding (AccentTintBrush derivado), micro-interacciones, dashboard con ejes/hover, calendario semanal, limpieza de tokens, accesibilidad + i18n es-AR | PENDIENTE |
| 6 — AFIP + hardware físico | +3-5 (opcional) | WSFE/WSAA/CAE, sidecars reales ZKTeco | OPCIONAL |

## Qué sigue

**Próxima sesión: decidir Q2 y arrancar Fase 1 del roadmap.** P-1 está cerrado y el piloto no tiene bloqueadores abiertos.

0. Decidir con el usuario Q2 / D9 (`AsNoTracking` antes de operar, o aceptar con reinicios diarios de la PC de recepción).
1. Confirmar con el orquestador si Fase 1 queda como continuación de la Sesión 3 o pasa a la Sesión 4. El roadmap la numera Sesión 3, pero la Sesión 3 cierra sin ella.
2. **Fase 1 (PENDIENTE, no arrancó):** backup off-site, auto-update Velopack, licencia (CUIT + fingerprint), retención de logs, fix "Sedes: 2 de 1". Antes de arrancar, verificar lo ya existente: backup local y recuperación (`src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs`, commit `eaad5bc`) e instalador Velopack (`scripts/pack.ps1`, commit `6b7ecdb`). F1 debe cubrir lo que falta, no rehacerlo.
3. Fuera del piloto: P-2 (saldo a favor en la ficha) y D1 (cobros multi-sede) siguen abiertas. D7 y D11 esperan decisión del usuario. Q3–Q9 y UX1–UX5: ver `docs/TASKS.md`.

Sesiones 4-8: ver tabla de fases arriba.

---

## [2026-10-28] Sesión 3

**Hecho:**
- **P-1 "freeze sin vuelta" cerrado y commiteado** (`b0caf77`). Era bloqueador de piloto.
  - Lógica (dev): `GetFreezeQuotaQuery` (`FreezeQuotaDto`, día local con `ICompanyCalendar`). `FreezeModalViewModel` + `FreezeModalView`: fechas, días usados y restantes del año, tope. Si el dominio rechaza igual (pantalla desactualizada), muestra su mensaje en el modal. "Reactivar" → `UnfreezeMembershipCommand` desde la ficha; "Congelar" solo en `Active`, "Reactivar" solo en `Frozen`. Auto-descongelado idempotente como primer paso de `DailyMaintenanceService` (`GetFreezeEndedAsync`, company-wide, con catch-up); `MaintenanceDayResult.Unfrozen`. `Membership.FreezeDaysUsedIn(year)`. Verificado con grep: ya no queda `DateTime.Today` en `MemberDetailViewModel.cs`.
  - Pulido (ux): medidor de cupo; bloque de resultado (días a congelar y nuevo vencimiento); tope y fecha en `InfoBar` Warning; Escape = Cancelar, Enter = Congelar, Tab cíclico, foco inicial en la fecha, nombres accesibles, scroll bajo 600 px. Ficha: insignia de estado en español con pill semántico (`StatusToneConverter`, recurso `StatusTone`); botones Congelar/Reactivar rotulados. Tokens nuevos: `InfoBrush` / `InfoTintBrush` (`Assets/Styles/Colors.axaml`), `Border.Pill.info` (`AppStyles.axaml`). Uso del converter: `Classes.success="{Binding Status, Converter={StaticResource StatusTone}, ConverterParameter=success}"`.
  - qa: APROBADO CON OBSERVACIONES, sin bloqueantes. Build Debug y Release OK; render real de ficha, modal y `StatusTone` verificado (capturas 19-23 y 20-22 dark en `tools/GymForge.Screenshots`). qa agregó `FreezeAtTheDoorTests`.
- **Q1 cerrado y commiteado** (`a07303f`). Preexistente de S1, expuesto por P-1: el anti-passback contaba swipes denegados, así que un socio regularizado recibía `AlreadyInside` dentro de 5 min. Fix en el repositorio (`GetLastGrantedAsync`), no en el use case, para no abrir un bypass. Con test-guardia.
- **Pre-cierre T-00/T-01:** rama `feat/sesion-3` desde `master`; `.gitignore` (`/TASKS.md` para versionar `docs/TASKS.md`; `*.pfx`, `*.lnk`); ADR-002 y ADR-003 a **Accepted**.
- **Commits: 11 en la rama.** Nueve de S1+S2 agrupados por feature (no un commit por punto, porque S1 y S2 compartían archivos como `ValidateSwipeUseCase.cs` y `ProcessPaymentCommand.cs`), más `b0caf77` y `a07303f`. Sin merge ni push.
- **Tests: 504 verdes** (Domain 72 · Application 230 · Desktop 25 · Integration 177), re-ejecutados por el orquestador tras Q1. Cifras intermedias: 498 (dev) y 500 (qa, antes de Q1). Esta sesión de notas no re-ejecutó `dotnet test`.

**En curso:**
- Nada a medias en código.
- **Q2 / D9:** decisión pendiente, sin cambios de código. Falta `AsNoTracking` en `src/GymForge.Infrastructure/Repositories/MembershipRepository.cs` (verificado: sin ocurrencias). El DbContext de vida larga se registra en `src/GymForge.Desktop/ServiceCollectionExtensions.cs`.
- **Fase 1 (F1):** no arrancó. Sin archivos tocados.
- **P-2** (saldo a favor en la ficha, `src/GymForge.Desktop/Views/Members/MemberDetailView.axaml`) y **D1** (`src/GymForge.Infrastructure/Repositories/ChargeRepository.cs:50`): abiertas, sin arrancar.
- **CI de GitHub** (`.github/workflows/ci.yml`): sin ejecutar (no hubo push).

**Blockers:** ninguno para el piloto (P-1 cerrado). Decisiones pendientes que no bloquean:
- Q2 / D9: aceptable si la PC de recepción se reinicia seguido. Si queda prendida semanas, aplicar `AsNoTracking` antes de operar.
- Usuario: política de datos personales en AuditLog (D11, con Q4: el motivo de freeze es texto libre) y DNI/licencia multi-sede (D7).

**Próximo paso:** decidir Q2 con el usuario y confirmar si F1 arranca en S3 o pasa a S4 (el roadmap numera F1 = Sesión 3 y F2 = Sesión 4). Luego F1, empezando por backup off-site. Antes, verificar lo ya existente: `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs` (backup local y recuperación) y el instalador Velopack (`scripts/pack.ps1`, `src/GymForge.Desktop/Program.cs`).

**Tocado:**
- Docs: `docs/PROGRESS.md`, `docs/TASKS.md`, `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md`, `docs/adr/ADR-003-estado-automatico-auditoria-cuenta-corriente.md`, `.gitignore`.
- Freeze (backend): `src/GymForge.Application/UseCases/Memberships/GetFreezeQuotaQuery.cs`, `src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceService.cs`, `src/GymForge.Infrastructure/Repositories/MembershipRepository.cs`, `src/GymForge.Domain/Entities/Membership.cs` (`FreezeDaysUsedIn`, L87).
- Freeze (Desktop): `src/GymForge.Desktop/ViewModels/Members/FreezeModalViewModel.cs`, `src/GymForge.Desktop/ViewModels/Members/MemberDetailViewModel.cs`, `src/GymForge.Desktop/Views/Members/FreezeModalView.axaml` (+ `.axaml.cs`), `src/GymForge.Desktop/Views/Members/MemberDetailView.axaml`, `src/GymForge.Desktop/Converters/StatusToneConverter.cs`, `src/GymForge.Desktop/Assets/Styles/Colors.axaml`, `src/GymForge.Desktop/Assets/Styles/AppStyles.axaml`.
- Q1: `src/GymForge.Infrastructure/Repositories/AccessLogRepository.cs:19`, `src/GymForge.Application/Interfaces/IRepositories.cs:155`, `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs:139`.
- Tests: `tests/GymForge.Desktop.Tests/` (proyecto nuevo), `tests/GymForge.Integration.Tests/FreezeAtTheDoorTests.cs`.
- Capturas: `tools/GymForge.Screenshots/Program.cs`.

---

## [2026-10-27] Sesión 2

**Hecho:**
- Fase 0 parte 2 según `docs/adr/ADR-003-estado-automatico-auditoria-cuenta-corriente.md` (Proposed). Los 4 puntos, solo backend:
  1. **Mantenimiento diario con catch-up.** `DailyMaintenanceRunner` recorre empresa × día local pendiente (ventana máx. 35 días; primer arranque solo hoy). Por día: vence membresías → marca mora → dunning → limpia mora saldada. Corre al arrancar y cada 6 h (`DailyMaintenanceHost`). `DunningStartup` borrado.
  2. **AuditLog real.** `AuditSaveChangesInterceptor` escribe en la misma transacción para 13 entidades `IAuditable`. Nunca audita `AuditLog` ni `AccessLog`. Redacta `PinCodeHash`, huellas y datos de salud.
  3. **Tope de freeze 90 días/año** (año calendario, por membresía; configurable con `Company.MaxFreezeDaysPerYear`). `Unfreeze(DateOnly)` devuelve el cupo no usado. `UnfreezeMembershipCommand` creado.
  4. **Cuenta corriente.** El excedente de pago genera `MemberCreditMovement` (ledger append-only) en la misma transacción. `ApplyMemberCreditCommand` consume FIFO. `GetMemberCreditBalanceQuery` lista para la ficha.
- Migraciones nuevas: `AddFreezePolicy`, `AddMemberCreditLedger`. Puntos 1 y 2 sin cambio de schema.
- Tests: 451 verdes (Domain 70 · Application 218 · Integration 163), desde 216.
- Flujo: arqui (ADR) → dev (4 puntos) → qa (aprobó con observaciones) → dev (cerró blockers) → qa (validó).

**Resuelto** (blockers de piloto que aparecieron en la revisión de S2):
- Overdue → Active al saldar. `Member.ClearArrears()` idempotente, `Charge.IsInArrears(asOf)` como definición única de mora, `ArrearsRelief` en `ProcessPaymentCommand` y `ApplyMemberCreditCommand` (mismo SaveChanges). Red de seguridad en el paso 4 del job.
- Día local por empresa, no UTC. `CompanyCalendar` (`ICompanyCalendar`) en el scheduler, la vigencia del Gatekeeper y `UnfreezeMembershipCommandHandler`. Corregía el check-in 21:00-24:00 ARG y el off-by-one del unfreeze.
- `GetOverdueAsync` excluye `Refunded` (fix de qa, `ChargeRepository`).

**En curso:** nada a medias en código. Piezas de UI diferidas a propósito (backend-only, ADR-003 §UI):
- Freeze: `MemberDetailViewModel.FreezeMembershipAsync` (`src/GymForge.Desktop/ViewModels/Members/MemberDetailViewModel.cs:104-111`) sigue igual: 30 días fijos, sin atrapar el tope.
- Saldo a favor: sin pieza en `src/GymForge.Desktop/Views/Members/MemberDetailView.axaml`.
- Cambios sin commit: ver T-00 y T-01 en `docs/TASKS.md`.

**Blockers:**
- **P-1 (bloquea piloto): freeze sin vuelta.** Corrección verificada en código al brief: el 4º freeze **no** deja la membresía congelada, porque `Membership.Freeze` valida antes de mutar (`src/GymForge.Domain/Entities/Membership.cs:94-106`). El efecto real: (a) el botón falla en silencio, sin feedback al operador; (b) ningún código de Desktop llama `UnfreezeMembershipCommand` y el job no descongela al llegar `FreezeEnd`. Una membresía `Frozen` sale de las consultas de membresía activa (`src/GymForge.Infrastructure/Repositories/MembershipRepository.cs:34,54,67`) y la app no tiene forma de devolverla a `Active`.
- Ninguno técnico para el resto de la Sesión 3.

**Próximo paso:** Sesión 3, en este orden: (1) decidir con el orquestador el commit de S1 + S2 (T-00) y el `.gitignore` (T-01); (2) cerrar P-1 (ver "Qué sigue"); (3) Fase 1.

**Tocado:**
- Mantenimiento: `src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceService.cs`, `src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceRunner.cs`, `src/GymForge.Application/Interfaces/IMaintenanceCheckpoint.cs`, `src/GymForge.Application/Interfaces/IDailyMaintenanceRunner.cs`, `src/GymForge.Infrastructure/Services/FileMaintenanceCheckpoint.cs`, `src/GymForge.Desktop/Services/DailyMaintenanceHost.cs`, `src/GymForge.Desktop/Program.cs`. Borrado: `src/GymForge.Desktop/Services/DunningStartup.cs`.
- Auditoría: `src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`, `src/GymForge.Domain/Entities/IAuditable.cs`, `src/GymForge.Application/Interfaces/ICurrentUser.cs`, `src/GymForge.Infrastructure/Persistence/AmbientCurrentUser.cs`, `src/GymForge.Desktop/Services/SessionCurrentUser.cs`, `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs`.
- Freeze: `src/GymForge.Domain/Entities/Membership.cs` (Freeze L90, Unfreeze L127), `src/GymForge.Domain/Entities/Company.cs`, `src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs` (Freeze L78, Unfreeze L109), migración `src/GymForge.Infrastructure/Migrations/20261027211351_AddFreezePolicy.*`.
- Cuenta corriente: `src/GymForge.Domain/Entities/MemberCreditMovement.cs`, `src/GymForge.Domain/Enums/CreditMovementType.cs`, `src/GymForge.Application/Interfaces/IRepositories.cs` (`IMemberCreditRepository` L63), `src/GymForge.Infrastructure/Repositories/MemberCreditRepository.cs`, `src/GymForge.Application/UseCases/Charges/ApplyMemberCreditCommand.cs`, `src/GymForge.Application/UseCases/Charges/GetMemberCreditBalanceQuery.cs`, `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs`, migración `src/GymForge.Infrastructure/Migrations/20261027212030_AddMemberCreditLedger.*`.
- Blockers resueltos: `src/GymForge.Application/Services/ArrearsRelief.cs`, `src/GymForge.Application/Services/CompanyCalendar.cs`, `src/GymForge.Domain/Entities/Member.cs`, `src/GymForge.Domain/Entities/Charge.cs`, `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs`, `src/GymForge.Infrastructure/Repositories/ChargeRepository.cs`.
- Docs: `docs/NOTIFICATIONS-DUNNING.md`, `docs/adr/ADR-003-estado-automatico-auditoria-cuenta-corriente.md`, `docs/TASKS.md`.

---

## [2026-10-27] Sesión 1

**Hecho:**
- Fase 0 parte 1, "Correctitud del core y multi-tenant", implementada según `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md`. qa APROBÓ con observaciones (veto no ejercido).
- P1 Gatekeeper: el decremento de visitas se persiste junto con el AccessLog en un solo SaveChanges (antes se perdía).
- P2 Vencimiento: `MembershipType.CalculateEndDate(start)` en dominio; lo usan `SellMembershipCommand` y `CreateMembershipCommand` (este último creaba membresías sin fin).
- P3 Multi-tenant: global query filters por markers `ISiteScoped` / `ITenantScoped`. `ICurrentTenant` con `ScopedTenantProvider` (AsyncLocal, falla cerrado con Guid.Empty), `AmbientTenantProvider` (Infra/API/tests) y `SessionTenantProvider` (Desktop). Se borró `TenantEntity`. El Gatekeeper abre `BeginScope(CompanyId, SiteId)`.
- P4 Pagos: `ProcessPaymentCommand` valida que cada ChargeId exista y sea del socio y de la empresa correctos. `ChargeRepository.GetByIdAsync` pasó de `FindAsync` a `FirstOrDefaultAsync` (FindAsync ignora filtros).
- P5 Credencial desconocida: `AccessLog.MemberId` pasa a `Guid?`, factory `DeniedUnknown`; `AppendDenied` ya no corta temprano. Migración `20261027171441_AccessLogAllowUnknownMember`. AccessLog sigue append-only.
- Fuera del ADR, con test: `DunningService` busca a cada socio en la sede de su cargo (no se pierde mora multi-sede).
- Fix de qa: parámetro muerto `membershipId` eliminado de `AppendDenied`.
- Tests: 216 verdes (Domain 17 · Application 128 · Integration 71). qa agregó `tests/GymForge.Integration.Tests/TenantAdversarialIntegrationTests.cs` (11 casos adversariales). Veredicto: sin fugas cross-company.

**En curso:**
- Nada a medias en código. Todo está en el working tree **sin commit**.
- Commits pendientes. El ADR sugiere un commit por punto, en este orden:
  1. `feat: [membresias] calcular vencimiento desde el plan` (P2)
  2. `fix: [acceso] persistir el decremento de visitas` (P1)
  3. `feat: [acceso] auditar swipes de credencial desconocida` (P5)
  4. `fix: [cobros] validar que el cargo sea del socio` (P4)
  5. `feat: [multi-tenant] filtros globales por empresa y sede` (P3)
- ADR-002 sigue en `Status: Proposed` (línea 3). Pasa a **Accepted** al commitear.

**Blockers:** Ninguno técnico. Pendiente de decisión del usuario: cómo y cuándo commitear (lo retiene el orquestador).

**Próximo paso:** Confirmar con el usuario la estrategia de commit. Antes de cada commit, `dotnet test GymForge.sln` en verde. Después arrancar la Sesión 2 (ver "Qué sigue").

**Tocado:**
- `docs/adr/ADR-002-multi-tenant-y-correctitud-core.md`
- `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs` (BeginScope L74 · DecrementVisit L154 antes de AppendAsync L164 · AppendDenied L187)
- `src/GymForge.Domain/Entities/MembershipType.cs` · `src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs` (L59) · `src/GymForge.Application/UseCases/Sales/SellMembershipCommand.cs`
- `src/GymForge.Application/Interfaces/ICurrentTenant.cs` (nuevo) · `src/GymForge.Domain/Entities/ITenantScoped.cs` (nuevo) · markers en `src/GymForge.Domain/Entities/*.cs`
- `src/GymForge.Infrastructure/Persistence/GymForgeDbContext.cs` · `src/GymForge.Infrastructure/Persistence/AmbientTenantProvider.cs` (nuevo) · `src/GymForge.Desktop/Services/SessionTenantProvider.cs` (nuevo)
- `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs` (L123-132) · `src/GymForge.Infrastructure/Repositories/ChargeRepository.cs`
- `src/GymForge.Domain/Entities/AccessLog.cs` · `src/GymForge.Infrastructure/Persistence/Configurations/AccessLogConfiguration.cs` · `src/GymForge.Infrastructure/Migrations/20261027171441_AccessLogAllowUnknownMember.cs` (nuevo)
- `src/GymForge.Application/UseCases/Dunning/DunningService.cs`
- `tests/GymForge.Integration.Tests/TenantAdversarialIntegrationTests.cs` (nuevo)
