# ADR-011 — Endurecimiento final (post-Fase 5): scope del DbContext, auth de API, SiteId + migración, DNI único, higiene de tooling/vulns, lector y verificación manual

**Status:** Accepted
**Date:** 2026-11-04
**Deciders:** arqui (planner) · implementa `dev` · veta `qa` · decide `usuario` (los ítems marcados)
**Sesión:** Endurecimiento final "todo junto" — posterior a la Fase 5 del roadmap (`docs/PROGRESS.md`, "Qué sigue"). Rama `feat/sesion-6`. Piso: **1461 tests verdes** (no bajar).

---

## Addendum — decisiones del usuario y cierre (2026-11-04, al pasar a `Accepted`)

- **Alcance (decisión del usuario): "plan por tiers"**, no "todo junto" literal. Se entregaron **MUST + SHOULD**;
  **D9b, D2 y UX19 quedan diferidos con plan** (ver "Fuera de alcance / diferido" más abajo). Motivo: D9 completo
  es un refactor transversal de alto riesgo, D2 (auth de API) está **apagado** (el Desktop no lanza la API), y
  UX19 necesita hardware real (no testeable headless).
- **DNI único (D7) — decisión del usuario: por EMPRESA + SEDE** → índice único `(CompanyId, SiteId, DocumentNumber)`.
  El mismo DNI puede existir como ficha separada en cada sede (y cuenta/consume licencia por sede).
- **Entregado y verificado por `qa` (APROBADO CON OBSERVACIONES, sin bloqueantes), 1472 tests verdes:**
  - **NEW-10** `chore: [deps]` — SSH.NET pin 2026.0.0 (vuln solo-tests resuelta).
  - **NEW-9** `chore: [build]` — `tools/GymForge.Screenshots` al `.sln`.
  - **D9a** `fix: [di]` — rota la captive dependency del Dashboard (scope por carga) + `ValidateScopes=true` en el test de DI.
  - **NEW-12** `test: [ui]` — `Avalonia.Headless.XUnit` + 3 tests de `ModalHost` (foco/Escape); `fix: [test]`
    estabiliza los tests de teclado (pasan a `[AvaloniaFact]` por el `Dispatcher.UIThread` headless).
  - **D3** `feat: [db]` — SiteId en Routine/WorkoutLog/BodyMeasurement/Booking (ISiteScoped) + migración con backfill.
  - **D7** `feat: [db]` — índice único `(CompanyId, SiteId, DNI)` + guard `DuplicateMemberDocumentException`.
- **Riesgo de migración en base viva (D7):** si una base real ya tiene DNIs duplicados en la misma (empresa, sede),
  `CREATE UNIQUE INDEX` **falla crudo** al migrar. Antes de migrar una base viva: **backup** y depurar duplicados
  (`SELECT CompanyId, SiteId, DocumentNumber, COUNT(*) FROM Members GROUP BY 1,2,3 HAVING COUNT(*) > 1`).
- **Fuera de alcance / diferido (a `docs/TASKS.md`):** **D9b** (scope-por-operación en los VMs Transient — su propia
  sesión; hoy `ValidateScopes` no se activa en `Program.cs` porque el shell resuelve esos VMs desde el provider raíz),
  **D2** (auth de API — solo si se decide prender la API), **UX19** (primer carácter del lector — necesita hardware).
  Nueva deuda de qa: `DuplicateMemberDocumentException` sin capturar en la UI (mensaje amigable), `Tmds.DBus.Protocol`
  0.21.2 (High, transitiva de Avalonia), backfill sin `COALESCE`, warnings CA1826/CS0618 de `Screenshots`.

## Context

La Fase 5 cerró el pulido "US$100k". El usuario pidió un **endurecimiento final "todo junto"** antes de
seguir. Este ADR define **qué entra, en qué orden y con qué dependencias**, verificando el estado **real**
de cada candidato en el código (no asumido). Los candidatos vienen de `docs/TASKS.md` y de "Qué sigue" de
`docs/PROGRESS.md`: **D9** (scope del DbContext), **D2** (auth de API), **D3** (SiteId faltante +
migración), **D7** (DNI único + migración), **NEW-9** (`tools/GymForge.Screenshots` al `.sln`), **NEW-10**
(vuln `SSH.NET` en tests), **UX19/F3-D2** (primer carácter del lector), **NEW-12** (verificación manual de
F5), y la deuda menor **D12 residuo** + **F5-D1 a F5-D4**.

**Tesis honesta de tamaño (clave de este ADR):** "todo junto" en una sola pasada **no es seguro**. **D9
completo** (scope por operación en todos los VMs) es un refactor transversal de alto riesgo; **D2 completo**
(auth real) es sobre-ingeniería para algo que hoy **ni siquiera se lanza**; **UX19** necesita hardware real
y su fix robusto es **difícil de testear**. Mezclar eso con dos migraciones de esquema (D3, D7) en un solo
branch es la receta para una regresión silenciosa. Por eso este ADR propone **tres tiers priorizados**
(MUST / SHOULD / DEFER), con las migraciones primero y el refactor grande fuera de esta pasada. Mejor
endurecer **bien** cinco cosas que dejar diez a medias.

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first / multi-tenant / append-only:** ninguna pieza puede romper el filtro por `CompanyId`+
  `SiteId` ni escribir en `AccessLog`/`AuditLog` con UPDATE/DELETE. D3, de hecho, **refuerza** el
  aislamiento por sede; D7 refuerza la unicidad por empresa.
- **Migración EF:** **solo D3 y D7 tocan esquema.** El resto (D9, D2, NEW-9/10, UX19, NEW-12, deuda menor)
  es código/tooling/tests, **sin** migración. Cada migración va con `dotnet ef migrations add`, se verifica
  Up/Down y `migrations has-pending-model-changes` limpio.
- **`dotnet test` verde antes de cada commit (regla 6)** y **un commit por feature (regla 7):** cada ítem
  es su propio commit con su propio test. Piso declarado: **1461 verdes**.
- **Simplicidad > cleverness:** D2 y UX19 se resuelven con lo mínimo correcto, no con un framework.

### Hechos del código verificados al 2026-11-04 (no asumir: ya chequeado)

**D9 — ciclo de vida del DbContext (el más arquitectónico):**
- `AddDbContext<GymForgeDbContext>` se registra con el **lifetime por defecto = Scoped**
  (`src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:26-29`). Todos los repos son **Scoped**
  (`:51-66`).
- El Desktop arma el contenedor con `services.BuildServiceProvider()` **sin opciones** → `ValidateScopes`
  queda en **false** (`src/GymForge.Desktop/Program.cs:54`). El shell resuelve **todos** los VMs desde ese
  **provider raíz**: `MainWindowViewModel(IServiceProvider sp)` guarda el raíz y hace
  `_sp.GetRequiredService<T>()` (`src/GymForge.Desktop/Views/Shell/MainWindowViewModel.cs:48-70`). Resolver
  un Scoped desde la raíz lo hace vivir **como singleton de facto** (toda la sesión de la app).
- **Caso agravado real:** `DashboardViewModel` está registrado como **Singleton**
  (`src/GymForge.Desktop/ServiceCollectionExtensions.cs:87`) **e inyecta 5 repos Scoped**
  (`IMemberRepository, IPaymentRepository, IAccessLogRepository, IChargeRepository, IMembershipRepository`,
  `src/GymForge.Desktop/ViewModels/Dashboard/DashboardViewModel.cs:100-103`). Es una **captive dependency**
  clásica: el Dashboard retiene esos repos — y por ende el `GymForgeDbContext` — **de por vida**.
- No lo detecta el test de DI porque `DesktopWiringTests.cs:61` usa `ValidateOnBuild = true` **pero no**
  `ValidateScopes = true`. Con `ValidateScopes=true` **hoy tiraría**.
- Varios VMs Transient inyectan repos directos además de `IMediator`: `ChargesViewModel`
  (`IChargeRepository`), `CashViewModel` (`IShiftRepository`+`IMemberRepository`), `RegisterSaleViewModel`
  (`IMemberRepository`), `ClassesViewModel` (`IMemberRepository`). Todos resueltos de la raíz → **mismo
  contexto largo**.
- **Ya scopean bien** (patrón a replicar): `SessionContext` (`Services/SessionContext.cs:103`),
  `ReceiptService` (`Services/ReceiptService.cs:27-28`) y `DailyMaintenanceHost`
  (`Services/DailyMaintenanceHost.cs:38`) usan `IServiceScopeFactory.CreateScope()`. **Por eso** el
  auto-descongelado (job en su propio scope, contexto fresco) no se refleja en la ficha: el contexto largo
  de la UI conserva la entidad `Frozen` trackeada (el síntoma de D9).
- `AsNoTracking` ya aplicado (DONE O9): `MembershipRepository`, `PaymentRepository`,
  `MemberRepository.GetJoinedBetweenAsync`, `SaleRepository.GetCommissionLinesBetweenAsync`. Es la
  mitigación parcial; **falta el scope por operación**.
- **No** se usa `IDbContextFactory` en ningún lado (verificado por grep).

**D2 — auth de la API:**
- `GymForge.Api` **ya hace bind solo a loopback**: `builder.WebHost.UseUrls("http://localhost:5000")`
  (`src/GymForge.Api/Program.cs:28`). Sin auth; el tenant es un singleton ambiente fijado desde la primera
  empresa al arrancar (`:35-39,51-59`) y el comentario ya reconoce la deuda (`:37`).
- Endpoints sin auth, tenant tomado del query/body: `GET/POST /api/members` (`MemberEndpoints.cs:15-57`),
  `POST /api/memberships` + freeze/cancel (`MembershipEndpoints.cs:13,29,37`), `POST /api/access/swipe`
  (`AccessEndpoints.cs:13`). Las lecturas pasan `companyId`/`siteId` explícitos → pueden **saltear** el
  tenant ambiente del filtro global.
- **Hecho decisivo de prioridad:** el **Desktop NO lanza la API** (no hay `Process.Start` de
  `GymForge.Api` ni `dotnet run` de la API en el runtime del escritorio; verificado por grep). La API es un
  proyecto aparte **que hoy no se inicia** en el producto que se distribuye. D2 es **latente**, no expuesto.

**D3 — SiteId faltante:**
- Existe `ISiteScoped : ITenantScoped` con `SiteId` (`src/GymForge.Domain/Entities/ITenantScoped.cs:10-13`).
- El filtro global aplica **company+site** a `ISiteScoped` y **solo company** a `ITenantScoped`
  (`src/GymForge.Infrastructure/Persistence/GymForgeDbContext.cs:89-117`).
- **Sin SiteId (solo `ITenantScoped`):** `Routine` (`Routine.cs:5`), `WorkoutLog` (`WorkoutLog.cs:3`),
  `BodyMeasurement` (`BodyMeasurement.cs:5`), `Booking` (`ClassSchedule.cs:77`). Su aislamiento entre sedes
  es **indirecto** (vía el socio).
- **Ya con SiteId** (no tocar): `Member`, `Membership`, `Charge`, `Payment`, `AccessLog`, `ClassSchedule`
  (`:29`), `Product`/`StockBySite`, `Shift`, `NotificationLog`, `MemberCreditMovement`.

**D7 — DNI único:**
- Hoy hay índice **NO único** en `DocumentNumber`: `IX_Members_Document`
  (`src/GymForge.Infrastructure/Persistence/Configurations/MemberConfiguration.cs:47`).
- **Precedente exacto** de índice único filtrado: `UX_Members_CompanyTagSerial` sobre `(CompanyId,
  TagSerial)` con filtro `TagSerial IS NOT NULL AND <> ''` (`:52-55`). D7 replica ese patrón para el DNI.
- `CreateMemberCommand` nunca validó DNI duplicado; el dedupe del import es por sede/archivo (TASKS D7,
  `MemberRepository.cs:31`, `ImportMembersCommand.cs:63`).

**NEW-9 — Screenshots fuera del `.sln`:**
- `GymForge.sln` tiene 10 proyectos de `src` + 4 de `tests` + 2 carpetas de solución; `tools/
  GymForge.Screenshots` **no está** (`GymForge.sln:7-36`).
- `tools/GymForge.Screenshots/GymForge.Screenshots.csproj`: `OutputType Exe`, **`net9.0-windows`** (mismo
  TFM que Desktop/Tests), refs `GymForge.Desktop` + `Avalonia.Headless` + `Avalonia.Skia`. Nada exótico.
- CI compila **solo** `GymForge.sln` (`.github/workflows/ci.yml:24-31`) → la herramienta **no** la compila
  nadie en CI; se rompió en silencio 2 veces (CS7036 por cambios de firma).

**NEW-10 — vuln `SSH.NET`:**
- `SSH.NET` **no** figura en `Directory.Packages.props` → es **transitiva de `Testcontainers 4.0.0`**
  (`Directory.Packages.props:73`), que solo referencia `GymForge.Integration.Tests`. **No llega a
  producción.** `CentralPackageTransitivePinningEnabled=true` (`:4`), así que se puede **pinear** desde el
  props sin tocar los csproj. CI no corre `dotnet list package --vulnerable`.

**UX19 / F3-D2 — primer carácter del lector:**
- Por **diseño documentado**, el primer carácter de una ráfaga es `Passthrough` (no hay gap previo que
  medir): `KeyboardWedgeDetector.PushCharacter` (`src/GymForge.Desktop/Services/KeyboardWedgeDetector.cs:85-92`),
  comentado en `:39-43`. El `WedgeKeyboardListener` solo traga cuando el detector dice `Swallow`
  (`WedgeKeyboardListener.cs:57-58`). Resultado: el primer carácter cae en el campo enfocado.
- Las vistas ya **mitigan** (foco al campo de credencial en "Escanear"; el modal del POS abre sin foco).

**NEW-12 — verificación manual de F5:**
- `ModalHost` (`src/GymForge.Desktop/Controls/ModalHost.cs`) hace foco al abrir (`Open`/`FocusFirstControl`,
  `:40-62`) y devuelve el foco al cerrar (`Close`, `:46-52`). El **Tab cíclico** es XAML
  (`TabNavigation="Cycle"` en `Border.ModalCard`); **Escape** lo resuelve el botón `IsCancel` de cada modal;
  `:focus-visible` es puramente visual.
- **Hoy NO hay tests headless de Avalonia en `dotnet test`:** `GymForge.Desktop.Tests.csproj` referencia
  solo `xunit`/`FluentAssertions`/`NSubstitute` (`:19-25`). En `Directory.Packages.props` está
  `Avalonia.Headless` (base) **pero no `Avalonia.Headless.XUnit`** (`:13`). La cobertura de render/teclado
  vive solo en `tools/GymForge.Screenshots` (fuera del `.sln`). **Corrección a ADR-009/F3-D1:** el paquete
  de xUnit headless **no** está presente; para un test de `ModalHost` hay que **agregarlo**.

---

## Decisión — resumen y priorización honesta

No se hace "todo junto". Se ordena en tres tiers. **Imprescindible = lo que endurece de verdad antes del
piloto con bajo riesgo de regresión.** El refactor grande y lo que necesita hardware se **difieren con
plan**, no se prometen a medias.

| # | Ítem | Entra | Tier | Toca esquema | Tamaño | Riesgo |
|---|---|---|---|---|---|---|
| NEW-9 | `Screenshots` al `.sln` | **Sí** | MUST | No | S | Bajo |
| NEW-10 | Pin de `SSH.NET` (tests) | **Sí** | MUST | No | S | Bajo |
| D9a | Mitigación de D9 (captive dep + `ValidateScopes` + `AsNoTracking` restante) | **Sí** | MUST | No | S–M | Bajo-Medio |
| NEW-12 | Infra headless + test de `ModalHost` (Escape/foco) | **Sí** | MUST | No | M | Bajo |
| D7 | DNI único + migración | **Sí** (con decisión del usuario) | MUST | **Sí** | M | Medio (dupes previos) |
| D3 | `SiteId` en 4 entidades + migración + backfill | **Sí** | SHOULD | **Sí** | M–L | Medio (backfill/filtro) |
| D9b | Scope por operación en **todos** los VMs | **No (diferido)** | DEFER | No | **L** | Alto |
| D2 | Auth real de la API | **No (mínimo + diferido)** | DEFER | No | M | — (no se lanza) |
| UX19 | Fix robusto del primer carácter | **No (diferido)** | DEFER | No | M | Alto (timing/HW) |
| D12 residuo, F5-D1..D4 | Deuda menor | **No (queda deuda)** | DEFER | No | S | Nulo |

**Guía de "suficiente":** con **NEW-9 + NEW-10 + D9a + NEW-12 + D7** el producto queda medido por CI, sin la
vuln de tests, sin el DbContext cautivo del Dashboard, con la red headless del modal y con el DNI
protegido en DB. **D3** es el "should": agrega el aislamiento por sede real (valioso para multi-sede, no
bloqueante para el piloto de 1 sede). **D9b, D2, UX19** son la cola larga: cada una su propia pasada.

---

## Orden de implementación y dependencias

Un commit por ítem, `dotnet test` verde antes de cada uno. **Las migraciones van primero** para que el
esquema quede estable mientras se toca el código encima; D3 y D7 son **independientes entre sí** (columnas/
índices distintos) pero cada una su migración y su commit.

1. **NEW-10 — pin de `SSH.NET`** (sin dependencias; quick win). `feat: [deps] pin SSH.NET parcheado en tests`
2. **NEW-9 — `Screenshots` al `.sln`** (independiente; habilita que CI lo compile). `feat: [build] sumar GymForge.Screenshots al sln`
3. **D7 — DNI único + migración** (primero de esquema; requiere **decisión del usuario** sobre el alcance del índice, ver abajo). `feat: [socios] indice unico de DNI por empresa + migracion`
4. **D3 — SiteId + migración + backfill** (segundo de esquema; el más delicado por el backfill y el cambio de filtro). `feat: [tenant] SiteId en rutinas, reservas, medidas y logs de entreno + migracion`
5. **D9a — mitigación de D9** (no-esquema; puede ir en paralelo a lo visual/tests, pero después de las migraciones para no mezclar). `fix: [di] romper la captive dependency del Dashboard y activar ValidateScopes`
6. **NEW-12 — infra headless + test de `ModalHost`** (no-esquema; paralelizable con D9a). `test: [ui] Avalonia.Headless.XUnit + ModalHost foco/Escape`

**Qué puede paralelizarse:** NEW-9, NEW-10, NEW-12 son independientes entre sí y de las migraciones.
**Qué es secuencial:** D7 antes que D3 solo por disciplina de una migración a la vez (EF las aplica por
orden de timestamp; no hay dependencia lógica). **D9a después de las migraciones** para no enredar un
cambio de DI con un cambio de esquema en el mismo tramo.
**Qué es riesgoso (va con más cuidado/qa):** D7 (puede fallar si hay DNIs duplicados preexistentes) y D3
(el backfill y el nuevo filtro de sede pueden ocultar filas si el SiteId no se completa bien).

**Diferidos con plan (NO en esta pasada):** D9b (refactor completo), D2 (auth), UX19 (primer carácter).

---

## Diseño por ítem

### NEW-10 — pin de `SSH.NET` (MUST, sin esquema)
Agregar en `Directory.Packages.props` una `PackageVersion` de `SSH.NET` en la **primera versión parcheada**
de GHSA-q939-rpr3-3284 (≥ 2024.x; `dev` confirma la mínima que acepte `Testcontainers 4.0.0`). Con el
transitive pinning ya activo, eso **sube la transitiva** sin tocar csproj. Solo afecta `Integration.Tests`.
- **Verificación:** `dotnet list GymForge.sln package --vulnerable --include-transitive` sin `SSH.NET`
  High; `dotnet test` 1461+ verde. Opcional (barato, recomendado): sumar ese comando como paso **no
  bloqueante** en `ci.yml` para no volver a enterarse tarde.

### NEW-9 — `Screenshots` al `.sln` (MUST, sin esquema)
`dotnet sln GymForge.sln add tools/GymForge.Screenshots/GymForge.Screenshots.csproj` (o editarlo a mano con
su GUID, dejándolo en la carpeta de solución `tools` o suelto). Mismo TFM que Desktop → compila en CI sin
cambios de workflow. **No** se ejecuta en CI (es un Exe que abre ventanas headless); solo **compila**. Si se
quiere, en una iteración futura se corre como check aparte (F3-D1), pero eso **no** es parte de NEW-9.
- **Riesgo/mitigación:** que al sumarlo rompa el build de CI por un desfasaje ya existente → se corre
  `dotnet build GymForge.sln` localmente antes de commitear; si hay una firma vieja, se corrige en el mismo
  commit (es justamente el objetivo: que deje de romperse en silencio).
- **Verificación:** `dotnet build GymForge.sln -c Release` 0 errores con la herramienta dentro.

### D7 — DNI único por empresa + migración (MUST, **esquema**, decisión del usuario)
Replicar el patrón de `UX_Members_CompanyTagSerial`: índice **único filtrado** sobre el DNI. **Decisión del
usuario (ver "Decisiones a confirmar"):** `(CompanyId, DocumentNumber)` (un DNI por empresa, atraviesa
sedes) **vs.** `(CompanyId, SiteId, DocumentNumber)` (un DNI por sede). El piloto es de 1 sede: ambos
sirven; por empresa es el default recomendado (un socio = una persona en el gimnasio).
- **Diseño:** en `MemberConfiguration`, nuevo `HasIndex(...).IsUnique().HasFilter("DocumentNumber IS NOT
  NULL AND <> ''").HasDatabaseName("UX_Members_CompanyDocument")` (o con `SiteId` según la decisión). El
  `IX_Members_Document` plano **se conserva** (lo usa la búsqueda) o se reemplaza por el único — `dev` lo
  evalúa (un único índice puede servir ambas funciones).
- **Guard en app (defensa en profundidad):** `CreateMemberCommand` e `ImportMembersCommand` validan DNI
  duplicado **antes** de insertar (mensaje claro es-AR) y, además, el handler **mapea la violación de
  unicidad de SQLite** a un error de dominio legible (que el índice no reviente como excepción cruda).
- **Riesgo clave — dupes preexistentes:** una base ya en uso puede tener DNIs repetidos (el alta nunca lo
  impidió). La migración `Up` **debe fallar con un mensaje accionable si hay duplicados** (no crear el índice
  a ciegas): un `SELECT` de control previo en la migración, o documentar el procedimiento de limpieza. **No**
  borrar datos automáticamente.
- **Verificación:** test de integración (dos altas con mismo DNI en la misma empresa → rechazo; misma DNI
  en otra empresa → permitido; import con dupes → los omite como ya hace). Migración Up/Down + `has-pending-
  model-changes` limpio. Multi-tenant: el índice **incluye `CompanyId`**, nunca es global.

### D3 — SiteId en Routine, Booking, WorkoutLog, BodyMeasurement + migración + backfill (SHOULD, **esquema**)
Hacer que las 4 entidades implementen `ISiteScoped` (agregar `SiteId` con setter privado y asignarlo en sus
factorías) para que el filtro global de `GymForgeDbContext` las aísle por sede automáticamente (sin tocar el
filtro: ya trata `ISiteScoped` como company+site).
- **Fuente del SiteId:** `Routine`, `WorkoutLog`, `BodyMeasurement` → del `Member` (su `SiteId`). `Booking`
  → del `Member` **o** del `ClassSchedule` (que ya tiene `SiteId`); `dev` elige una y la documenta
  (recomendado: el del `ClassSchedule`, que es donde ocurre la clase).
- **Migración + backfill (lo delicado):** `AddColumn SiteId` (no nulo con default temporal) y un **UPDATE de
  backfill** que complete `SiteId` desde el socio/horario para todas las filas existentes, **antes** de que
  el filtro de sede empiece a exigir el match. Si una fila queda con `SiteId` incorrecto, **desaparece** de
  las lecturas de su sede. Por eso el backfill es parte de la misma migración y se testea.
- **Factorías/comandos:** `Routine.Create`, `Booking.Create/CreateWaitlisted`, `WorkoutLog.Create`,
  `BodyMeasurement.Create` reciben y setean `SiteId`; sus callers (`CreateRoutineCommand`, el booking de
  Clases, etc.) lo pasan desde la sesión/socio.
- **Riesgo/mitigación:** para el piloto de 1 sede el cambio es inocuo (todas las filas comparten sede);
  para multi-sede es la corrección buscada. `qa` corre los adversariales multi-tenant/multi-sede existentes
  (`TenantAdversarialIntegrationTests`) + casos nuevos de que una rutina/reserva de la sede A **no** aparece
  en la sede B.
- **Verificación:** migración Up/Down con datos (incluye el backfill), `has-pending-model-changes` limpio,
  tests de aislamiento por sede para las 4 entidades.

### D9a — mitigación de D9: romper la captive dependency + red de validación (MUST, sin esquema)
**No** es el refactor completo (eso es D9b). Es el subconjunto de **bajo riesgo y alto valor** que saca el
peor caso y deja una red para el futuro:
1. **Dashboard sin captura:** `DashboardViewModel` deja de ser Singleton con repos cautivos. Opción simple
   y correcta: **(a)** pasarlo a `Transient` **y** que cada carga obtenga sus datos vía `IServiceScopeFactory`
   (patrón ya usado en `ReceiptService`/`SessionContext`), **o (b)** que inyecte `IServiceScopeFactory` y
   abra un scope por refresh. Se descarta dejarlo Singleton con Scoped adentro.
2. **Red de validación:** activar `ValidateScopes = true` (además de `ValidateOnBuild`) en el test de DI
   (`DesktopWiringTests.cs:61`). Eso **convierte en rojo** cualquier captive dependency futura y **obliga**
   a que D9a deje el contenedor limpio. Es el guard que hoy falta.
3. **`AsNoTracking` restante:** completar las lecturas de UI que aún trackean sin necesidad (las de O9 ya
   están; `dev` barre las que queden, p. ej. las lecturas de Clases/Rutinas de solo-lectura).
- **Qué NO hace D9a:** no reescribe los ~15 VMs para scope por operación ni cambia cómo se resuelve
  `IMediator`. Eso es D9b. D9a baja el riesgo del "contexto de por vida" al mínimo accionable sin refactor
  masivo.
- **Verificación:** el test de DI con `ValidateScopes=true` pasa (prueba que no queda ningún Singleton con
  Scoped adentro); test de que el Dashboard recarga datos frescos tras un cambio en otro scope (reproduce el
  caso del auto-descongelado si es alcanzable, o un equivalente). `dotnet test` verde.

### NEW-12 — infra headless + test de `ModalHost` (MUST, sin esquema)
(a) **Automatizable:** sumar `Avalonia.Headless.XUnit` a `Directory.Packages.props` y a
`GymForge.Desktop.Tests.csproj`, y escribir con `[AvaloniaTest]` un test de `ModalHost`:
  - al poner `IsVisible=true`, el foco entra al primer control focusable de la card;
  - al poner `IsVisible=false`, el foco **vuelve** al elemento previo;
  - el comando `IsCancel`/Escape del modal cierra (verificable vía el bool/commando del VM).
(b) **Queda sí o sí para el usuario (no headless):** el **render** de `:focus-visible` (anillo de foco), el
  **recorrido real** del Tab cíclico tecla a tecla en los 4 modales, y el foco por teclado en las grillas
  (`DataGrid`) — todo visual/timing. Se verifica en la app viva (checklist en PROGRESS/TASKS para el
  usuario), apoyado por `tools/GymForge.Screenshots` en light/dark.
- **Verificación:** el test headless corre dentro de `dotnet test` (nueva infra); el resto queda como
  checklist manual explícito.

---

## Diseño de los DIFERIDOS (plan, para no prometerlos a medias)

### D9b — scope por operación en todos los VMs (DEFER, su propia sesión)
El fix de fondo: que **cada operación de UI** (cada `Send` de MediatR, cada lectura de repo) corra en su
**propio scope** con un `GymForgeDbContext` fresco, como ya hacen `ReceiptService`/`SessionContext`. Camino
incremental de bajo riesgo, en una pasada dedicada:
- Introducir un pequeño façade **`IScopedMediator`** (o `IUnitOfWork`/`IOperationScope`) que haga
  `using var scope = _scopeFactory.CreateScope(); await scope.Resolve<IMediator>().Send(...)` — centraliza
  el patrón probado y evita tocar MediatR.
- Migrar los VMs **de a uno** (cada uno su commit + test) de `IMediator` directo al façade; y **mover a
  queries/handlers** las lecturas que hoy hacen repo directo (ChargesViewModel, CashViewModel,
  RegisterSaleViewModel, ClassesViewModel) para que también pasen por scope.
- Guard: con `ValidateScopes=true` ya activo (D9a), el contenedor no deja reintroducir capturas.
- **Por qué no ahora:** es transversal (~15 VMs), de alto riesgo de regresión funcional, y D9a + reinicios
  periódicos de la PC de recepción cubren el piloto. Hacerlo junto a dos migraciones es imprudente.

### D2 — auth de la API (DEFER; mínimo si se decide exponerla)
Como **la API no se lanza** hoy, lo único imprescindible ahora es **no exponerla**: ya hace bind a
`localhost:5000` (loopback), así que no hay superficie de red. Cuando/si se quiera prender (mobile/web),
endurecer con lo **mínimo correcto** para un producto offline de un host:
- mantener el **binding a loopback**;
- **token local compartido** (header) generado en la PC y leído por el cliente local — sin IdentityServer ni
  OAuth (sobre-ingeniería);
- **derivar el tenant del servidor** (no del body) y **guard de pertenencia** en `CreateMembershipCommand`
  (`:46-49`, hoy carga socio y plan solo por Id → membresía huérfana posible);
- mapear errores de dominio (D17) y agregar CORS/rate-limit básicos.
No entra en esta pasada: no hay demanda (API apagada) y mezclarlo diluye el foco.

### UX19 / F3-D2 — primer carácter del lector (DEFER; necesita hardware)
El fix robusto (que `WedgeKeyboardListener` **retenga** la primera tecla ≤ `InterKeyScanMaxMs` con un
`DispatcherTimer` y la **reinyecte** al control si no llega una segunda) es **dependiente de timing**,
requiere re-sintetizar entrada en Avalonia y **no se puede testear en headless/CI con confianza**, además de
exigir verificación con el lector real es-AR. Se mantiene la **mitigación de vista** existente (foco al campo
de credencial; POS sin foco). Se difiere hasta tener la pasada de hardware real. No se mete un fix
timing-dependiente sin forma de probarlo.

### Deuda menor que queda como deuda (DEFER)
- **D12 residuo:** `Member.cs:88` (edad/regla de nacimiento), `DatabaseBackupService.cs:62`,
  `DemoDataSeeder.cs:79`. Inofensivo con la zona correcta. Queda abierto en TASKS.
- **F5-D1** (ancho de la columna Dom en Clases; decisión del usuario), **F5-D2** (hoy no se refresca a
  medianoche), **F5-D3** (navegación de semana fire-and-forget), **F5-D4** (test frágil con `Task.Yield`).
  Todas BAJAS/no bloqueantes. F5-D2 y F5-D3 son triviales y podrían colarse si sobra tiempo, pero **no** son
  objetivo de esta pasada; se dejan como deuda para no inflar el alcance.

---

## Tests / verificación por ítem (resumen)

- **NEW-10:** `dotnet list package --vulnerable` sin `SSH.NET` High; `dotnet test` verde.
- **NEW-9:** `dotnet build GymForge.sln -c Release` 0 errores con la herramienta dentro.
- **D7:** integración (duplicado por empresa rechazado; otra empresa permitido; import omite dupes);
  migración Up/Down; `has-pending-model-changes` limpio; el índice incluye `CompanyId`.
- **D3:** aislamiento por sede de las 4 entidades (A no ve lo de B); backfill correcto; migración Up/Down;
  `has-pending-model-changes` limpio; adversariales multi-tenant/sede verdes.
- **D9a:** test de DI con `ValidateScopes=true` pasa; Dashboard recarga datos frescos tras cambio externo.
- **NEW-12:** test headless de `ModalHost` (foco al abrir, devolución al cerrar, Escape cierra); checklist
  manual para `:focus-visible`, Tab cíclico y teclado en grillas.
- **Todos:** `dotnet test GymForge.sln` **≥ 1461 verdes**, 0 omitidos; build Debug y Release 0 errores;
  `qa` veta. Cero regresiones.

## Consecuencias

- **Esquema:** dos migraciones nuevas (D7: índice único de DNI; D3: `SiteId` + backfill en 4 entidades).
  El resto no toca esquema. Multi-tenant **reforzado** (D3 agrega aislamiento por sede; D7 unicidad por
  empresa). Append-only intacto.
- **Robustez de la PC de recepción:** D9a saca el peor caso (DbContext cautivo del Dashboard de por vida) y
  deja `ValidateScopes` como red; el refactor de fondo (D9b) queda planificado, no improvisado.
- **Proceso/CI:** `Screenshots` se compila con la solución (NEW-9) y la vuln de tests desaparece (NEW-10);
  aparece la primera infra de test headless real en `dotnet test` (NEW-12).
- **Honestidad de alcance:** se endurecen bien 5–6 cosas; D9b/D2/UX19 quedan con plan explícito en vez de a
  medias.

## Riesgos

- **D7 con dupes preexistentes:** la migración falla si la base tiene DNIs repetidos. Mitigación: control
  previo en la migración con mensaje accionable; no borrar datos solo.
- **D3 backfill:** un `SiteId` mal completado oculta filas de su sede. Mitigación: backfill en la misma
  migración + tests de aislamiento; inocuo en el piloto de 1 sede.
- **D9a captive dep:** cambiar el Dashboard a scope-per-operación puede alterar sutilezas de refresco.
  Mitigación: `ValidateScopes=true` + test de recarga; alcance acotado al Dashboard (no a los 15 VMs).
- **Dos migraciones + cambio de DI en un branch:** mitigación: migraciones primero y separadas, D9a después,
  un commit por ítem, `dotnet test` verde entre cada uno.
- **NEW-12 infra nueva:** agregar `Avalonia.Headless.XUnit` podría requerir un `[AvaloniaTestApplication]`
  mínimo. Mitigación: alcance a un solo test de `ModalHost`; si la infra da problemas, se aísla en su commit
  sin frenar el resto.

## Decisiones a confirmar con el usuario
1. **Alcance del índice de DNI (D7):** `(CompanyId, DocumentNumber)` — un DNI por **empresa**, recomendado —
   **vs.** `(CompanyId, SiteId, DocumentNumber)` — un DNI por **sede** (permite la misma persona como socio
   en dos sedes). Liga con "¿un socio en varias sedes consume una o dos licencias?" (TASKS D7).
2. **Tiers propuestos:** ¿se acepta entregar **MUST + SHOULD** (NEW-9, NEW-10, D9a, NEW-12, D7, D3) en esta
   pasada y **diferir** D9b, D2 y UX19 con plan? ¿O se prioriza alguno de los diferidos por sobre el SHOULD?
3. **Migraciones sobre una base viva:** si el piloto ya tiene datos, confirmar que se hace **backup
   pre-migración** (F1-D3) y que se acepta el riesgo de D7 (posibles dupes) y D3 (backfill) antes de
   aplicarlas en la PC del gimnasio.

## Fuera de alcance (deuda explícita → `docs/TASKS.md`)
- **D9b** (scope por operación en todos los VMs), **D2** (auth real de la API, hoy apagada), **UX19/F3-D2**
  (fix robusto del primer carácter, necesita hardware): diferidos **con plan** en este ADR.
- **D12 residuo**, **F5-D1 a F5-D4**: deuda menor, no bloqueante.
- **Decisiones de política del usuario** (no las decide el equipo): **NEW-8** (`.editorconfig`: severidad o
  barrido de formato), **D11 ampliada** (PII en exports y en el snapshot de DELETE), **O10** (roles para
  exportar/ver comisiones), y **push/merge de `feat/sesion-6`**.

## Referencias
- D9: `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:26-66` · `src/GymForge.Desktop/
  ServiceCollectionExtensions.cs:87` · `src/GymForge.Desktop/Program.cs:54` · `Views/Shell/
  MainWindowViewModel.cs:48-70` · `ViewModels/Dashboard/DashboardViewModel.cs:100-103` ·
  `Services/{SessionContext,ReceiptService,DailyMaintenanceHost}.cs` · `tests/GymForge.Desktop.Tests/
  DesktopWiringTests.cs:61`
- D2: `src/GymForge.Api/Program.cs:28,35-59` · `Endpoints/{Member,Membership,Access}Endpoints.cs` ·
  `src/GymForge.Application/UseCases/Memberships/CreateMembershipCommand.cs:46-49`
- D3: `src/GymForge.Domain/Entities/ITenantScoped.cs` · `Routine.cs:5` · `WorkoutLog.cs:3` ·
  `BodyMeasurement.cs:5` · `ClassSchedule.cs:77` (Booking) · `src/GymForge.Infrastructure/Persistence/
  GymForgeDbContext.cs:89-117`
- D7: `src/GymForge.Infrastructure/Persistence/Configurations/MemberConfiguration.cs:47,52-55` ·
  `Repositories/MemberRepository.cs:31` · `UseCases/Members/ImportMembersCommand.cs:63`
- NEW-9: `GymForge.sln` · `tools/GymForge.Screenshots/GymForge.Screenshots.csproj` · `.github/workflows/
  ci.yml`
- NEW-10: `Directory.Packages.props:73` (Testcontainers) · `:4` (transitive pinning)
- UX19: `src/GymForge.Desktop/Services/KeyboardWedgeDetector.cs:85-92` · `WedgeKeyboardListener.cs:57-58`
- NEW-12: `src/GymForge.Desktop/Controls/ModalHost.cs` · `tests/GymForge.Desktop.Tests/
  GymForge.Desktop.Tests.csproj` · `Directory.Packages.props:13`
- Roadmap/estado: `docs/PROGRESS.md` ("Qué sigue") · `docs/TASKS.md` (D2, D3, D7, D9, NEW-8..NEW-12,
  F5-D1..D4) · `docs/adr/README.md`
