# TASKS — GymForge

Backlog y deuda del roadmap de mejora. Se mueve entre **TODO / DOING / DONE**.
Historial de sesiones: `docs/PROGRESS.md`. Reglas del proyecto: `CLAUDE.md`.

Dueños: roles del equipo (arqui, dev, qa, usuario) propuestos; el orquestador asigna nombre si hace falta.
Rutas con `:línea` verificadas al 2026-10-27.

**Piloto de 1 sede:** bloquea **P-1** (freeze sin vuelta: UI y descongelado automático). El resto de la deuda no bloquea. Además, el piloto depende del roadmap (Fase 1, Sesión 3).

---

## DOING

- (vacío) Nada en curso en código. Sesiones 1 y 2 cerradas, sin commit (ver T-00 y T-01).
  Próximo: Sesión 3 arranca por P-1 (ver TODO).

---

## TODO

### Bloqueadores de piloto

- **P-1** Freeze sin vuelta. **ALTA · bloquea piloto.** Dueño: dev (lógica, ViewModel, job) · ux/preview (modal y botón).
  Verificado en código al 2026-10-27:
  - `src/GymForge.Desktop/ViewModels/Members/MemberDetailViewModel.cs:104-111`: `FreezeMembershipAsync` congela 30 días fijos desde `DateTime.Today` (L107, no pasa por `ICompanyCalendar`) y no atrapa el error. El 4º freeze del año lanza `InvalidOperationException` y la UI no muestra nada.
  - Corrección al brief: el 4º freeze **no** deja la membresía en `Frozen`. `Membership.Freeze` valida antes de mutar (`src/GymForge.Domain/Entities/Membership.cs:94-106`). Además, nada en Application ni Desktop llama `Member.Freeze()` (`src/GymForge.Domain/Entities/Member.cs:134`), así que el socio no pasa a `Frozen` al congelar.
  - Nada devuelve la membresía a `Active`: ningún código de Desktop llama `UnfreezeMembershipCommand` (`src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs:109`) y `src/GymForge.Application/UseCases/Maintenance/` no descongela al llegar `FreezeEnd`. Las consultas de membresía activa filtran `Status == Active` (`src/GymForge.Infrastructure/Repositories/MembershipRepository.cs:34,54,67`), así que una membresía `Frozen` deja de contar como vigente sin salida desde la app. Confirmar el camino exacto en el Gatekeeper.
  - Hay que hacer: (a) modal con fechas y días usados/restantes del año (`Membership.FreezeDaysUsedYear`, `Company.MaxFreezeDaysPerYear`) con el mensaje del tope; (b) botón "Reactivar" → `UnfreezeMembershipCommand`; (c) auto-descongelado en `DailyMaintenanceService` al llegar `FreezeEnd`, idempotente; (d) fechas en día local con `ICompanyCalendar`.

- **P-2** Saldo a favor en la ficha del socio + botón "Usar saldo a favor". UI diferida; no bloquea piloto por decisión del orquestador en S2. Backend listo: `GetMemberCreditBalanceQuery`, `ApplyMemberCreditCommand`. Hoy el excedente se acumula en el ledger pero no se ve ni se aplica desde la UI. Dónde: `src/GymForge.Desktop/Views/Members/MemberDetailView.axaml` (pestaña Datos o Cobros) + `src/GymForge.Desktop/ViewModels/Members/MemberDetailViewModel.cs`. Dueño: ux/preview · dev.

### Pre-cierre de Sesiones 1 y 2

- **T-00** Commitear S1 + S2 (todo el working tree, sin commit). S1: 5 commits en el orden de `docs/PROGRESS.md` (Sesión 1). S2: un commit por punto de ADR-003 (orden A freeze → B crédito → C auditoría → D scheduler, sección "Plan de implementación") más los fixes de la revisión. `dotnet test` verde antes de cada commit. Pasar ADR-002 (línea 3) y ADR-003 (línea 3) de `Proposed` a `Accepted`. **No usar `git add -A`**: hay `GymForge-backup-licencias.lnk` en la raíz.
  Dueño: orquestador + usuario (commit) · arqui (ADRs).
- **T-01** `.gitignore` antes del commit:
  - Agregar `*.lnk` y `*.pfx` (el `.pfx` lo genera `scripts/sign-selfsigned.ps1 -BackupPfx`).
  - Revisar la regla `TASKS.md` (`.gitignore` línea 77, sección "Productivity plugin"): sin barra inicial, también matchea `docs/TASKS.md`. Si este archivo no está ya versionado, no entra en ningún commit. Cambiarla a `/TASKS.md` o quitarla.
  Dueño: usuario (decisión) · dev.

### Deuda de la Sesión 1 (dejada por qa, por severidad)

| # | Sev | Tarea | Dónde | Bloquea piloto 1 sede | Dueño propuesto |
|---|---|---|---|---|---|
| D1 | MEDIA | **Cobros multi-sede.** `GetOverdueAsync` es company-wide, pero el cobro exige que el cargo sea de la sede activa. Un cajero de la sede A ve cobros de la B en Dashboard/Cobros y al cobrarlos recibe `KeyNotFoundException`. Fix: `GetOverdueAsync` company-wide solo para dunning, y una versión por sede para la UI. | `src/GymForge.Infrastructure/Repositories/ChargeRepository.cs:50`; cobro en `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs:129-132` | No | dev |
| D2 | MEDIA | **API sin auth (preexistente).** `POST /api/access/swipe`, `POST /api/members` y `POST /api/memberships` toman el tenant del body. `CreateMembershipCommand` carga socio y plan solo por Id, así que puede quedar una membresía huérfana. Falta: auth, tenant derivado del servidor, guard de pertenencia, CORS, rate limiting, headers. | `src/GymForge.Api/Endpoints/AccessEndpoints.cs:13`, `src/GymForge.Api/Endpoints/MemberEndpoints.cs:44`, `src/GymForge.Api/Endpoints/MembershipEndpoints.cs:13`; guard faltante en `src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs:46-49` | No, mientras Kestrel escuche solo en localhost:5000. **Sí** bloquea si la API se expone. | dev (auth) · arqui (diseño) |
| D3 | BAJA-MEDIA | **Sin SiteId.** `Routine`, `Booking`, `WorkoutLog` y `BodyMeasurement` son `ITenantScoped` (solo CompanyId). El aislamiento entre sedes es indirecto, vía el socio. Agregar `SiteId` + migración. (`WorkoutLog` ya está en su propio archivo; se movió de `AuditLog.cs`.) | `src/GymForge.Domain/Entities/Routine.cs:5`; `src/GymForge.Domain/Entities/ClassSchedule.cs:77` (Booking); `src/GymForge.Domain/Entities/WorkoutLog.cs:3` (WorkoutLog); `src/GymForge.Domain/Entities/BodyMeasurement.cs:5` | No | dev |
| D4 | BAJA | **Hardening de sedes.** `GetSiteAsync` y `SiteHasDataAsync` filtran solo por Id. `UpdateSiteCommandHandler` y `DeleteSiteCommandHandler` no validan `site.CompanyId`. | `src/GymForge.Infrastructure/Repositories/SiteRepository.cs:31,36`; `src/GymForge.Application/UseCases/Settings/SettingsCommands.cs:125,147` | No | dev |
| D5 | BAJA | **Biblioteca global (preexistente).** `DeleteExerciseCommandHandler` permite borrar ejercicios globales de la biblioteca compartida. El handler no consulta `Exercise.IsGlobal`. | `src/GymForge.Application/UseCases/Exercises/ExerciseQueries.cs:92`; `src/GymForge.Domain/Entities/Exercise.cs:47` | No | dev |
| D6 | BAJA | **Pago "a cuenta".** `ProcessPaymentCommand` sin `ChargeIds` no valida que `MemberId` sea del tenant. La UI no lo permite. | `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs:123-124` | No | dev |
| D7 | Decisión de producto | **DNI y licencia multi-sede.** Hoy no hay índice único de DNI. `CreateMemberCommand` nunca validó DNI duplicado. `FindByDocumentAsync` y el dedupe del import son por sede. Hay que decidir si el mismo socio en varias sedes consume una sola licencia y si el DNI es único por empresa. | `src/GymForge.Infrastructure/Repositories/MemberRepository.cs:31`; `src/GymForge.Application/UseCases/Members/ImportMembersCommand.cs:63` | No (1 sede). Decidir antes de multi-sede real. | usuario (decisión) · arqui (propuesta) |
| D8 | MENOR | **Trazabilidad de denegados.** Los logs denegados no guardan `MembershipId`. TODO sin resolver: sembrar `VisitsRemaining` para planes `VisitPack` (anotado en ADR-002 §2). | `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs:187`; `src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs:52` | No | dev |

### Deuda de la Sesión 2 (nueva, por severidad)

Origen: revisión de qa y observaciones de dev al cierre de S2. La numeración sigue a D8.
S2-1..S2-4 pasaron a DONE. **S2-5 = D1**: no entró en S2 y sigue abierta en la tabla de la Sesión 1.
Límites aceptados de ADR-003 (no son tareas): checkpoint de mantenimiento global (desktop = 1 empresa);
conteo de freeze por membresía (una membresía nueva reinicia el cupo); `Company` fuera de AuditLog (raíz, baja frecuencia).

| # | Sev | Tarea | Dónde | Bloquea piloto 1 sede | Dueño propuesto |
|---|---|---|---|---|---|
| D9 | MEDIA | **DbContext de vida larga en Desktop.** Sin scope por operación ni `AsNoTracking`: la UI puede ver estado viejo tras el tick de 6 h, y un `SaveChanges` fallido deja el tracker sucio (un reintento podría duplicar). El paso 4 del job compensa la mora. Fix: scope por operación en Desktop. | Registro en `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:26`; Desktop en `src/GymForge.Desktop/ServiceCollectionExtensions.cs` | No | dev |
| D10 | MEDIA (multi-país; BAJA en AR) | **Zona horaria partida.** `SessionContext.InitializeAsync` no copia `Company.Timezone` a `GatekeeperConfig.Timezone`. El horario usa la zona de `GatekeeperConfig` (`src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs:122-124`) y la vigencia usa la de la empresa vía `ICompanyCalendar`. Fix: una sola fuente (`Company.Timezone`) y quitar el parámetro `timezone` sin uso de `IsTimeAllowed`. | `src/GymForge.Desktop/Services/SessionContext.cs` (InitializeAsync); `ValidateSwipeUseCase.cs:31,122-124` | No | dev |
| D11 | MEDIA-BAJA | **PII en AuditLog.** Los snapshots de Delete siguen guardando DNI, email, teléfono y `Observations`. Los datos de salud ya se redactan. Decidir la política antes de la Fase 4 (Ley 25.326). | `src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` | No | usuario (decisión) · arqui (propuesta) |
| D12 | BAJA | **Barrido de fecha de negocio.** Pasar `DateTime.Today` e `IClock.Today` a `ICompanyCalendar`: `SellMembershipCommand`, `CreateMemberCommand`, `ImportMembersCommand`, `RoutineCommands`, `AccessQueries`, `ChargesViewModel`, `DashboardViewModel`, `ReportsViewModel`, `ActivateLicenseCommand` y `MemberDetailViewModel.cs:107` (lo toca P-1). Hoy sirve si la PC tiene la zona correcta; `ActivateLicenseCommand` tiene 15 días de gracia que lo vuelven inofensivo. | Buscar `DateTime.Today` e `IClock.Today` en `src/`. Solo L107 de `MemberDetailViewModel.cs` está verificada línea a línea | No | dev |
| D13 | BAJA | **Paso 4 del job en cada día del catch-up.** `IsInArrears(day)` se evalúa en cada día de la ventana: puede limpiar de más al reprocesar un día anterior al vencimiento (converge, pero mete ruido en AuditLog). Fix: correr el paso 4 solo en el último día de la ventana y moverlo antes del dunning (hoy, si el sender lanza, no se limpia ese día). | `src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceService.cs` | No | dev |
| D14 | BAJA (latente) | **`SellMembershipCommand` activa a cualquier socio no-Active.** Hace `member.Activate` a Frozen o Suspended y dispara `MemberActivatedEvent` cada vez. Un Overdue con otras deudas queda Active hasta que el job lo vuelva a marcar. Fix: usar el relief de mora en vez de `Activate` para Overdue. | `src/GymForge.Application/UseCases/Sales/SellMembershipCommand.cs` (guard L93) | No | dev |
| D15 | BAJA (latente) | **Cobro con `ChargeIds` sin guard `IsCollectable`.** `ProcessPaymentCommand` no lo valida (sí lo hace `ApplyMemberCreditCommand`): un cargo WrittenOff o Refunded se "revive" al cobrarlo. Unificar el guard antes de implementar write-off. Hoy nada genera WrittenOff ni Refunded en producción. | `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs` | No | dev |
| D16 | BAJA | **Freeze: conteo anual pisado al alternar `freezeStart`.** `FreezeYear` toma el año de `freezeStart`, así que alternar fechas entre años pisa el conteo. Validar que `freezeStart` sea del año en curso y no futuro. | `src/GymForge.Domain/Entities/Membership.cs:101-113` | No | dev |
| D17 | BAJA | **API: errores de dominio sin mapear.** Mapear a 409, 404 o 422. Hoy caen como error no mapeado. | `src/GymForge.Api/Endpoints/` (p. ej. `MembershipEndpoints.cs`) | No | dev |
| D18 | BAJA | **Checkpoint con fecha futura.** Si el reloj retrocede o el archivo tiene una fecha posterior a hoy, `DailyMaintenanceRunner` hace `start > localToday` → `continue` y no procesa hasta alcanzarla. Validar o acotar el valor leído. | `src/GymForge.Infrastructure/Services/FileMaintenanceCheckpoint.cs`; `src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceRunner.cs` | No | dev |
| D19 | BAJA | **SQLite: `Cache=Shared` y WAL.** Revisar la cadena de conexión contra CLAUDE.md (SQLite local en WAL). | `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:26` | No | dev |

### Fases siguientes (detalle en `docs/PROGRESS.md`)

- **F1** Robustez de producción: backup off-site, auto-update Velopack, licencia (CUIT + fingerprint), retención de logs, fix "Sedes: 2 de 1". Sesión 3, después de P-1. Dueño: dev.
- **F2** WhatsApp gratis + cobranzas: pantalla de morosos, `WhatsAppLinkSender` (wa.me), `DunningConfig`, `NotificationLog`. Sesión 4. Dueño: dev.
- **F3** Escáner HID + check-in premium: keyboard-wedge en kiosk y POS, foto del socio, teclado numérico, arquitectura de hardware. Sesión 5. Dueño: dev.
- **F4** Profundidad: reportes reales con export Excel/PDF, editar socio desde la ficha, rutinas en la ficha, comisiones de staff. Sesiones 6-7. Dueño: dev.
- **F5** Pulido US$100k: command palette (Ctrl+K), fix branding, micro-interacciones, dashboard con ejes/hover, calendario semanal, limpieza de tokens, accesibilidad + i18n es-AR. Sesión 8. Dueño: dev.
- **F6** AFIP + hardware físico (opcional, +3-5 sesiones): WSFE/WSAA/CAE, sidecars ZKTeco reales. Dueño: dev · arqui.

---

## DONE

- **S2-1** Mantenimiento diario con catch-up (punto 1 de ADR-003). `DailyMaintenanceRunner` recorre empresa × día local pendiente (máx. 35 días; primer arranque solo hoy). Por día: vence membresías → marca mora → dunning → limpia mora saldada (paso 4, agregado en la revisión). Corre al arrancar y cada 6 h (`DailyMaintenanceHost`). Borrado `DunningStartup.cs`. Sin migración. Dueño: dev. Hecho, sin commit.
- **S2-2** AuditLog real vía `AuditSaveChangesInterceptor` (punto 2). `IAuditable` en 13 entidades, `ICurrentUser`, misma transacción, sin recursión. Redacta `PinCodeHash`, huellas y datos de salud. Sin migración. Dueño: dev. Hecho, sin commit.
- **S2-3** Tope de freeze 90 días/año (punto 3): validación en `Membership.Freeze`, `Company.MaxFreezeDaysPerYear`, `Unfreeze(DateOnly)` devuelve el cupo, `UnfreezeMembershipCommand`. Migración `AddFreezePolicy`. Backend hecho; la UI quedó en P-1. Dueño: dev. Hecho, sin commit.
- **S2-4** Cuenta corriente (punto 4): ledger `MemberCreditMovement` append-only; el excedente de `ProcessPaymentCommand` genera crédito en la misma transacción; `ApplyMemberCreditCommand` (FIFO); `GetMemberCreditBalanceQuery`. Migración `AddMemberCreditLedger`. Backend hecho; la UI quedó en P-2. Dueño: arqui (diseño) · dev. Hecho, sin commit.
- **S2-B1** Overdue → Active al saldar (blocker de piloto, surgido en la revisión de S2). `Member.ClearArrears()`, `Charge.IsInArrears(asOf)`, `ArrearsRelief` en `ProcessPaymentCommand` y `ApplyMemberCreditCommand` (mismo SaveChanges); red de seguridad en el paso 4 del job. Probado: atomicidad, multi-tenant, idempotencia. Dueño: dev. Hecho, sin commit.
- **S2-B2** Día local por empresa, no UTC (blocker de piloto). `CompanyCalendar` (`ICompanyCalendar`) en el scheduler, la vigencia del Gatekeeper y `UnfreezeMembershipCommandHandler`. Corregía el check-in 21:00-24:00 ARG y el off-by-one del unfreeze. Probado en bordes de medianoche. Dueño: dev. Hecho, sin commit.
- **S2-B3** `GetOverdueAsync` excluye `Refunded` (fix de qa, `ChargeRepository`), con test. Dueño: dev. Hecho, sin commit.
- **S2-REV** Revisión de qa de S2: aprobada con observaciones. Dev cerró 2 blockers y qa los validó. Tests 451 verdes según dev/qa. Dueño: qa. Hecho.
- **S2-ADR** ADR-003 escrito (`docs/adr/ADR-003-estado-automatico-auditoria-cuenta-corriente.md`, Status Proposed):
  diseño de los 4 puntos de la Sesión 2 con archivos/firmas, 2 migraciones, orden TDD y tests esperados. Dueño: arqui. Hecho.
- **S1-A** Gatekeeper: decremento de visitas persistido en el mismo SaveChanges que el AccessLog (P1). `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs`. Hecho, sin commit.
- **S1-B** Vencimiento de membresía: `MembershipType.CalculateEndDate` en dominio, usado por ambos comandos (P2). Hecho, sin commit.
- **S1-C** Multi-tenant con global query filters, `ICurrentTenant` y tres providers (P3). Hecho, sin commit.
- **S1-D** Pertenencia de cargos en pagos y `GetByIdAsync` con filtros (P4). `src/GymForge.Application/UseCases/Charges/ProcessPaymentCommand.cs`. Hecho, sin commit.
- **S1-E** Accesos de credencial desconocida: `AccessLog.MemberId` nullable + migración `20261027171441_AccessLogAllowUnknownMember` (P5). Hecho, sin commit.
- **S1-F** Fix de qa: parámetro muerto `membershipId` eliminado de `AppendDenied`. Hecho, sin commit.
- **S1-G** Tests adversariales multi-tenant (11 casos) en `tests/GymForge.Integration.Tests/TenantAdversarialIntegrationTests.cs`. Hecho, sin commit.
- **S1-H** Revisión de qa de la Sesión 1: APROBADO con observaciones (veto no ejercido). Hecho.
- **S1-I** Creación de `docs/PROGRESS.md` y `docs/TASKS.md`. Hecho.
