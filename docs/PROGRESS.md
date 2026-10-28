# PROGRESS — GymForge

Bitácora del equipo entre sesiones. **Entradas nuevas arriba**; no se borra historial.
Deuda y tareas: `docs/TASKS.md`. Decisiones: `docs/adr/`. Historia por sprints y reglas del proyecto: `CLAUDE.md` (no duplicado acá).

> "Sesión N" = sesiones de este roadmap de mejora. No es lo mismo que los "Sprint" de CLAUDE.md.

---

## Contexto del roadmap

Objetivo: evaluar GymForge, mejorarlo y dejarlo "listo para un gimnasio real" con aspecto de sistema de US$100k.

- Plan: 6 fases en ~8 sesiones.
- Hito: **tras la sesión 3** se puede instalar y operar en el gimnasio piloto mientras se sigue puliendo. Condición: cerrar el bloqueador P-1 (freeze) al arrancar la sesión 3 (ver `docs/TASKS.md`). **Estado (2026-10-28): condición P-1 cumplida y Fase 1 cerrada.** Piloto habilitado con 2 condiciones de uso (ver la entrada "Fase 1 cerrada").
- Flujo por sesión: arqui (ADR) → dev (implementa) → qa (aprueba o veta).
- Regla 6 de CLAUDE.md: `dotnet test` verde antes de cada commit.

## Estado general (al 2026-10-28)

- Sesiones 1, 2, 3 y Fase 1: **commiteadas** en `feat/sesion-3` (18 commits desde `master`: 12 previos + 6 de F1). Sin merge a `master`, sin push. ADR-001 a ADR-004 en `Accepted`.
- Tests: **646 verdes** (Domain 83 · Application 288 · Desktop 54 · Integration 221), cifra de qa tras Fase 1. Antes de F1: 504 (cifra del orquestador tras Q1). La sesión de notas no re-ejecutó `dotnet test`.
- Working tree: solo los docs de cierre de Fase 1 (`docs/PROGRESS.md`, `docs/TASKS.md`, `docs/adr/README.md`), pendientes de commit (lo hace el orquestador). `.gitignore` ya ignora `*.lnk` y `*.pfx`.
- Abierto en `docs/TASKS.md`: **0 bloqueadores de piloto**. Quedan: decisiones del usuario (UX11, Q2/D9, D7, D11/Q4, F1-D8, F1-D11), P-2, D1–D19, F1-D1 a F1-D11, Q3–Q9, UX1–UX12 y las fases F2–F6.

## Roadmap por fases

| Fase | Sesiones | Contenido | Estado |
|---|---|---|---|
| 0 — Bugs y reglas de negocio | 1-2 (P-1 en 3) | Correctitud del core | **CERRADA.** S1, S2, P-1 (freeze) y Q1 commiteadas en `feat/sesion-3` |
| 1 — Robustez de producción | 3 (continuación) | Backup off-site, auto-update Velopack, licencia (CUIT + fingerprint), retención de logs, fix "Sedes: 2 de 1" | **CERRADA (2026-10-28).** F1-1 a F1-5 commiteadas; qa aprobó con observaciones. Deuda en TASKS (F1-D1 a F1-D11) |
| 2 — WhatsApp gratis + cobranzas | 4 | Pantalla de morosos, `WhatsAppLinkSender` (wa.me), `DunningConfig` en Configuración, `NotificationLog` | PENDIENTE |
| 3 — Escáner HID + check-in premium | 5 | Lector keyboard-wedge en kiosk y POS, foto del socio, teclado numérico en pantalla, arquitectura de hardware | PENDIENTE |
| 4 — Profundidad | 6-7 | Reportes reales (morosos, por plan/método/sede, retención; export Excel/PDF), editar socio desde la ficha, rutinas en la ficha, comisiones de staff | PENDIENTE |
| 5 — Pulido US$100k | 8 | Command palette (Ctrl+K), fix branding (AccentTintBrush derivado), micro-interacciones, dashboard con ejes/hover, calendario semanal, limpieza de tokens, accesibilidad + i18n es-AR | PENDIENTE |
| 6 — AFIP + hardware físico | +3-5 (opcional) | WSFE/WSAA/CAE, sidecars reales ZKTeco | OPCIONAL |

## Qué sigue

**Próxima sesión (4): Fase 2, WhatsApp gratis + cobranzas.** Empezar por la pantalla de morosos, `WhatsAppLinkSender` (wa.me), `DunningConfig` y `NotificationLog`. F1 está cerrada; quedan solo sus deudas F1-D1 a F1-D11 en TASKS (ninguna bloquea el piloto con las condiciones de uso).

Decisiones del usuario abiertas (ninguna bloquea el piloto con las 2 condiciones de uso):
0. **UX11 + F1-D7:** origen de las actualizaciones (hoy vacío = inactivo) y `https://` obligatorio. Junto con F1-D3, bloquea activar el auto-update en campo.
1. **Q2 / D9:** `AsNoTracking` antes de operar, o aceptar reinicios diarios de la PC de recepción.
2. **D11 + Q4:** política de datos personales en AuditLog (el motivo de freeze es dato de salud).
3. **D7:** DNI y licencia en multi-sede.
4. **F1-D8:** el CUIT de la empresa se edita sin PIN; decidir si se pide PIN de admin para cambiarlo.
5. **F1-D11 (verificación):** antes de emitir claves con la versión nueva, `.\scripts\licencia.ps1 show <clave ya entregada>` debe seguir mostrándolas válidas.
6. **F1-D1 / D2 / D3:** resolver antes de escalar. F1-D3 antes de activar el auto-update.
7. Pendiente operativo: merge de `feat/sesion-3` a `master`, push y CI. Lo decide el usuario.

Fuera del piloto: P-2 (saldo a favor en la ficha) y D1 (cobros multi-sede) siguen abiertas. Resto de deuda (D1–D19, F1-D4 a F1-D10, Q3–Q9, UX1–UX12): ver `docs/TASKS.md`.

Sesiones 4-8: ver tabla de fases arriba.

---

## [2026-10-28] Sesión 3 (continuación) — Fase 1 cerrada

**Hecho:**
- **Fase 1 "Robustez de producción" cerrada** según ADR-004 (`Accepted`). Seis commits de feature sobre `feat/sesion-3`:
  - **F1-1 Retención de logs** (`0f37c63`): `retainedFileCountLimit: 30` en Desktop y API. La API loguea ahora en `%LOCALAPPDATA%\GymForge\logs\`.
  - **F1-2 Fix "Sedes: 2 de 1"** (`4b6e8ec`): helper puro `LicenseUsage` + medidor `UsageMeter`. El excedente se muestra "(excede el plan)" con texto, color y pill. No toca sedes ni socios preexistentes.
  - **F1-3 Backup off-site** (`b257ce1`): `ILocalSettings` (JSON en `%LOCALAPPDATA%\GymForge\settings.json`, no viaja en el export). Copia del último backup a carpeta configurable, rotación 10 en destino, best-effort (nunca tira, no crea la carpeta padre). UI en Config → Datos.
  - **F1-4 Licencia CUIT + fingerprint** (`ba30ea4`): el CUIT bloquea al activar (clave sin CUIT activa igual; CUIT no parseable = warning). Fingerprint `MachineGuid` con fallback: avisa, no bloquea. `MachineId` opcional en el payload firmado (claves viejas verifican; vector fijo en tests). `LicenseGen --machine`. UI en Config → Licencia.
  - **F1-5 Auto-update Velopack** (`2440c08`): `IUpdateService` / `VelopackUpdateService`. **No-op si `UpdateFeedUrl` está vacío** (hoy inactivo). Banner "actualización lista" con "Reiniciar ahora" / "Después".
  - **Pulido de UI de Config y shell** (`2bd7f78`): ux sobre las 4 superficies. `NeutralBrush` a AA; estilos `TextBlock.success|warning|danger|Feedback`; `ProgressBar.warning`; `Border.Divider`.
- **Verificación (qa, APROBADO CON OBSERVACIONES, sin bloqueantes):** build Debug y Release 0 errores. **646 tests verdes** (Domain 83 · Application 288 · Desktop 54 · Integration 221), cifra de qa, no re-ejecutada en esta nota. Qa agregó 2 regresiones en `tests/GymForge.Integration.Tests/DataTransferServiceTests.cs` (`settings.json` no viaja en el export ni se pisa al importar). XAML verificado en runtime con `tools/GymForge.Screenshots`.
- **`docs/adr/README.md`:** ADR-004 figuraba como `Proposed`; corregido a `Accepted`.
- **TASKS consolidado sin duplicar:** F1-1 a F1-5 y F1-D12 (housekeeping) pasan a DONE. F1-D1 a F1-D11 y UX6 a UX12 quedan en TODO con las entradas que cargaron qa y ux, sin repetirlas. Solo se ajustó lo que cambió: F1-D3 de MEDIA-BAJA a MEDIA (decisión de cierre); F1-D1 y F1-D2 con la referencia a la UI que recomienda carpeta de red o Dropbox/OneDrive; F1-D3 y F1-D7 con la marca de prerequisito para activar el auto-update; UX11 con el bloqueo; la intro de UX cita "4 superficies", no "5 piezas".
- **Commits:** 18 desde `master` = 12 previos (9 de S1+S2, `b0caf77`, `a07303f` y el docs `77eaaa4`) + 6 de F1. La cifra "11" de la Sesión 3 no contaba `77eaaa4`. El commit de este cierre lo hace el orquestador.

**Condiciones de uso del piloto** (de uso, no de código):
1. **Backup externo: pendrive LOCAL, no carpeta de red.** La copia off-site es síncrona en el arranque (`src/GymForge.Desktop/Program.cs:57` → `InitialiseDatabaseAsync` → `InfrastructureServiceExtensions.cs:109`) y en el cierre de caja (`CashCommands.cs:120`). Con red caída puede colgar (F1-D1). Config todavía ofrece "pendrive, carpeta de red o carpeta de Dropbox/OneDrive" (`SettingsView.axaml:416-422`). Una carpeta sincronizada sube el backup sin cifrar a la nube (F1-D2).
2. **Auto-update INACTIVO** hasta configurar el origen (`settings.json` → `UpdateFeedUrl`; UX11). Sin origen no hace nada ("sin configurar"). Antes de activarlo en campo: F1-D3 (backup previo a migraciones) y F1-D7 (`https://`).

**En curso:** nada a medias en código. Esta sesión solo tocó docs.

**Blockers:** ninguno para el piloto con las 2 condiciones de uso. Activar el auto-update en campo queda bloqueado por UX11, F1-D3 y F1-D7.

**Desvíos detectados al cerrar** (no corregidos en código; ADR-004 no se editó):
- ADR-004 pieza 2 pide la constante `DefaultFeedUrl = https://github.com/ManuelHidalgo26/GymForge`. El código no la tiene (`src/GymForge.Desktop/Services/VelopackUpdateService.cs:12`: "no hay ningún repositorio fijo"). Es la decisión que deja abierta UX11.
- ADR-004 pieza 2 define `CheckAsync` devolviendo `UpdateInfo?`. La firma real es `UpdateCheckResult CheckAsync()`, con `DownloadAsync`, `ApplyAndRestart` y `ApplyOnExit`. Los tests del banner están en `tests/GymForge.Desktop.Tests/UpdateViewModelTests.cs` (el ADR decía `UpdateBannerTests.cs`).
- `VelopackUpdateService.IsGitHubRepository` solo mira el host: un `http://` pasa (F1-D7 confirmado en `VelopackUpdateService.cs:106-113`).

**Próximo paso:**
1. El orquestador commitea los docs de cierre. El mensaje debe decir que el auto-update queda inactivo hasta resolver UX11.
2. Usuario: decidir UX11 y Q2/D9 (ver "Qué sigue").
3. Sesión 4: arrancar Fase 2 por la pantalla de morosos (`DunningConfig`, `WhatsAppLinkSender`, `NotificationLog`).

**Tocado:**
- Docs de cierre: `docs/PROGRESS.md`, `docs/TASKS.md`, `docs/adr/README.md`. Sin cambios de código.
- Fase 1 (referencia, no tocada aquí): `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs`, `src/GymForge.Infrastructure/Services/LocalSettingsService.cs`, `src/GymForge.Desktop/Services/VelopackUpdateService.cs`, `src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs`, `src/GymForge.Desktop/Views/Settings/SettingsView.axaml`, `src/GymForge.Api/Program.cs`, `src/GymForge.Desktop/Program.cs`, `tests/GymForge.Application.Tests/Licensing/LicenseUsageTests.cs`, `tests/GymForge.Integration.Tests/DataTransferServiceTests.cs`, `docs/adr/ADR-004-robustez-de-produccion.md`.

---

## [2026-10-28] Sesión 3 (continuación) — Fase 1: pulido de UI (ux)

**Hecho (sin build ni tests: ux no tiene shell; falta la validación de qa):**
- Pulidas las 4 superficies de F1 sin tocar ViewModels ni lógica: Config → Datos ("Copia de seguridad externa"), Config → Licencia (uso Socios/Sedes, aviso de otra PC, código de esta PC), Config → "Versión y actualizaciones" y el aviso de update del shell (`src/GymForge.Desktop/Views/Settings/SettingsView.axaml`, `src/GymForge.Desktop/Views/Shell/MainWindow.axaml`). Bindings, commands y `x:DataType` intactos.
- Copia externa: campo de ruta de solo lectura (enfocable), pill de estado ("Sin configurar" neutral / "Al día" success / "Requiere atención" warning) + detalle, "Copiar ahora" con anillo mientras copia. Licencia: medidor por uso con barra y pill "Excede el plan" (el exceso ya no depende solo del color). Update: versión en grande, pill "Actualización lista", botón con anillo, origen de solo lectura. Shell: el aviso vive en un `Border` con el lienzo (`ContentBackground`) y margen, y la fila Auto mide 0 con el aviso cerrado.
- Todos los mensajes de resultado de Config pasaron de `ActiveColorBrushConverter` (≈3,5:1) a `TextBlock.Feedback` + `success`/`danger`, con `LiveSetting`.

**Registro de componentes y tokens (design system):**
- Nuevos en `Assets/Styles/AppStyles.axaml`: `Border.Divider` (hairline en cards), `TextBlock.success|warning|danger` (texto con tono, tokens AA; antes `warning` era local de SettingsView), `TextBlock.Feedback` (resultado de acción), `ProgressBar.warning` (medidor pasado del tope).
- Cambiado en `Assets/Styles/Colors.axaml`: `NeutralBrush` light `#6B7280`→`#4B5563`, dark `#9CA3AF`→`#D1D5DB` (el pill neutral daba ≈4,1:1 y ≈3,5:1; ahora ≈6:1). Solo lo usa `Border.Pill.neutral`.
- Patrón nuevo en `SettingsView.axaml`: `DataTemplate` `UsageMeter` (`LicenseUsageLine`) reutilizado para Socios y Sedes.
- Para uso de dev: tono encendido con `Classes.success="{Binding Ok}" Classes.danger="{Binding !Ok}"` sobre `Classes="Feedback"`.

**Pendiente / abierto:** UX6–UX12 en `docs/TASKS.md` (tokens de tono sin AA, `ActiveColorBrushConverter` en el dashboard, hints con `Opacity`, aviso de PIN, feedback inline de "Copiar", **UX11 decisión del usuario: origen de updates editable o con valor por defecto**, "Reiniciar ahora" tras "Después"). Capturas a revisar por qa: `10-configuracion`, `10b-configuracion-licencia-otra-pc`, `24-shell-actualizacion-lista` (falta dark, sidebar colapsada, uso excedido y backup con atención).

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
