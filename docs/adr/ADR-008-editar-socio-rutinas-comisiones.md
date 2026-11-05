# ADR-008 — Editar socio, rutinas en la ficha y comisiones de staff (Fase 4, parte 2)

**Status:** Accepted
**Date:** 2026-11-04
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` · veta `qa`
**Sesión:** 7 — Fase 4 "Profundidad" (parte 2) del roadmap (`docs/PROGRESS.md`)

---

## Addendum — decisiones confirmadas y desvíos (2026-11-04, al pasar a `Accepted`)

Al implementar la parte 2, el usuario confirmó las 7 decisiones abiertas y `qa` validó un desvío. Este
bloque **prevalece sobre el texto original de las piezas** donde difiera:

- **Decisión 2 (PII en AuditLog al editar socio): REDACTADO.** El diff de un UPDATE de `Member` no guarda
  el valor viejo/nuevo de datos personales ni de salud; solo el nombre del campo que cambió. Implementado en
  `AuditSaveChangesInterceptor`. El snapshot de un **DELETE** de `Member` sigue sin redactar (es la única
  evidencia de qué se borró): queda como decisión de retención ligada a **D11**, no de esta fase.
- **Decisión 3 (alcance de editar socio): INCLUYE datos médicos y contacto de emergencia**, además de
  identidad, contacto, nacimiento y consentimiento. **Corrige el texto original de la Pieza A**, que los
  dejaba fuera de v1. Por ser datos de salud, su diff también va redactado.
- **Decisiones 1, 4, 5, 6, 7 (defaults tomados):** comisiones = snapshot al vender, sin `CommissionLedger`
  ni clawbacks; vendedor = cajero logueado (sin selector); PT (`CommissionPctPt`) fuera de v1; reporte de
  comisiones sin roles nuevos (ligado a **O10**); porcentaje en 0..100.
- **Desvío #2 en la Pieza C (aceptado por qa):** la comisión de una línea de producto usa
  `Product.CommissionStaffPct` si es > 0 y, si es 0, cae al `CommissionPctProducts` del vendedor (el texto
  original tomaba solo el del producto). Es coherente con "0 = apagado" y revertible en una línea.

## Context

La parte 1 de la Fase 4 (ADR-007) entregó reportes reales + export. La **parte 2** cierra la "profundidad"
del día a día de recepción y de administración con las **tres features confirmadas por el usuario**:

1. **Editar socio desde la ficha.** Hoy la ficha (`MemberDetailView`, pestaña Datos) muestra los datos del
   socio en modo lectura y solo deja editar **foto** y **credencial**; cualquier corrección de nombre,
   documento, email, celular o fecha de nacimiento obliga a… nada, porque **no existe un caso de uso de
   edición** (`CreateMemberCommand` solo da de alta). En un gimnasio real los datos llegan con errores de
   tipeo, el socio cambia de celular, o el padrón se importó por CSV con huecos: hay que poder corregirlos
   sin dar de baja y volver a crear (lo que rompería historial, membresías y cobros).

2. **Rutinas en la ficha.** El armador de rutinas ya existe como hub aparte (`Rutinas` → pestañas
   **Armador | Biblioteca**, `RoutineBuilderViewModel`), pero la ficha del socio tiene una pestaña
   **Rutinas** que hoy **no carga nada** (`MemberDetailViewModel.LoadActiveTabAsync` no tiene caso para
   `Routines`). El profesor/recepcionista que está mirando al socio tiene que salir de la ficha, ir al hub,
   y volver a buscar al mismo socio en un combo de 500 nombres. El objetivo es **ver y armar la rutina del
   socio desde su propia ficha**, reutilizando el armador que ya funciona, sin duplicar lógica.

3. **Comisiones de staff.** El dueño le paga a sus vendedores/profesores un porcentaje por lo que venden
   (membresías, productos). Hoy **el dominio ya tiene los campos** para eso pero **nadie los llena ni los
   muestra**: `Staff.CommissionPctMemberships/Pt/Products`, `Product.CommissionStaffPct`,
   `Membership.SoldByStaffId` + `Membership.CommissionAmount`, `SaleLine.CommissionStaffId`. Falta: (a) que
   los porcentajes se puedan **editar**, (b) que la comisión se **calcule y se guarde** al vender, y (c) un
   **reporte** de cuánto le toca a cada uno en el período. Es lo más nuevo de la parte 2 (toca cálculo,
   persistencia, UI y reporte), pero se apoya en el modelo que ya está en la base.

Esta es la guía de implementación para `dev` en **TDD**: seguir las firmas y el orden de abajo; `ux` entra
en las piezas marcadas. Alcance acotado a estas tres features; lo que queda afuera (incluidas F4-D1 a F4-D4)
está en "Fuera de alcance".

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first**: todo persiste en SQLite local antes de cualquier sync. Nada nuevo sale a cloud.
- **Multi-tenant** por `CompanyId` + `SiteId`. Toda lectura/escritura filtra por empresa; el reporte de
  comisiones "toda la empresa" cruza sedes **de una misma empresa** con el patrón ya establecido
  (`IgnoreQueryFilters()` + `CompanyId` explícito), nunca cross-company (igual que ADR-007, decisión 5).
- **AccessLog y AuditLog append-only**: no se tocan. Las comisiones se guardan como **snapshot en la entidad
  de la transacción** (Membership/SaleLine), no en un log. El AuditLog registra automáticamente los cambios
  de las entidades `IAuditable` (Member, Staff, Product, Membership); ver la **nota de PII** en la Pieza A
  (edición de socio) ligada a D11/Q4.
- **PAN nunca en DB**: no aplica (las comisiones no tocan medios de pago; `Payment` ya guarda solo
  `CardLast4`).
- **FingerprintTemplate `byte[]`**: no se toca.
- `dotnet test` verde antes de cada commit (regla 6). Piso declarado al cierre de F4 parte 1: **1234 tests
  verdes**. Un commit por feature (regla 7).
- Moneda ARS, formato `$35.000,00` (es-AR); fechas `dd/MM/yyyy`; el "hoy" del negocio es el de la empresa
  (`ICompanyCalendar`, no `DateTime.Today`). **Nota de deuda:** varias rutas nuevas copian el patrón
  existente de `DateTime.Today` (F4-Q1/D12); no se resuelve acá, pero `dev` no debe **sumar** nuevos usos
  sin inyectar `ICompanyCalendar` cuando el código alrededor ya lo tiene.

### Hechos del código verificados al 2026-11-04 (no asumir: ya chequeado)

**Socio / edición:**
- `Member` (`src/GymForge.Domain/Entities/Member.cs`): tiene `UpdateContact(email, mobile)` y
  `CompleteProfile(birthDate, source, salesRepId, marketingConsent)`, pero **ningún método** para cambiar
  `FirstName`/`LastName`/`DocumentType`/`DocumentNumber`/`Gender` (solo se setean en `Create`). Falta un
  método de edición de identidad.
- `CreateMemberCommand` (`src/GymForge.Application/UseCases/Members/CreateMemberCommand.cs`) crea; **no hay
  `UpdateMemberCommand`.** El validador de alta ya fija las reglas (NotEmpty, MaxLength, Email válido cuando
  hay, fecha de nacimiento sana) que la edición debe reusar.
- `DocumentNumber` es `string` + `DocumentType` enum; **no** usa el value object `Cuit` (ese es de la
  empresa). No hay índice único de DNI (D7): la edición, como el alta, no valida DNI duplicado.
- `MemberDetailViewModel` (`src/GymForge.Desktop/ViewModels/Members/MemberDetailViewModel.cs`) ya edita foto
  (`PhotoPickerViewModel` + `UpdateMemberPhotoCommand`) y credencial (`AssignCredentialCommand`) desde la
  pestaña Datos. La edición de datos se suma a esa pestaña.
- `IMemberRepository` aplica los global query filters (empresa+sede); `GetByIdAsync` cross-tenant devuelve
  null. Igual, los handlers de dominio guardan `entity.CompanyId == cmd.CompanyId` a mano (patrón de
  `GetRoutineDetailQueryHandler`).
- **AuditLog en edición** (`AuditSaveChangesInterceptor.cs`): un `Member` en estado `Modified` registra el
  diff **old→new de cada escalar que cambió**, salvo los redactados (`PinCodeHash`, `MedicalConditions`,
  `Medications`, `Allergies`) y los timestamps. Es decir, editar DNI/email/celular/`Observations`
  escribiría esos valores en el AuditLog **append-only** (no purgable). Ver la nota de PII en la Pieza A.

**Rutinas:**
- `Routine` (`src/GymForge.Domain/Entities/Routine.cs`) es `ITenantScoped` (solo `CompanyId`, **sin
  `SiteId`** — es D3) → `RoutineDay` → `RoutineItem` → `RoutineItemSet`.
- Casos de uso completos en `RoutineCommands.cs`: `GetMemberRoutinesQuery`, `CreateRoutineCommand`,
  `GetRoutineDetailQuery`, `AddRoutineDayCommand`, `AddRoutineItemCommand`; todos validan tenant por
  `CompanyId` y por socio. **Se reutilizan tal cual.**
- `RoutineBuilderViewModel` (`src/GymForge.Desktop/ViewModels/Routines/RoutineBuilderViewModel.cs`) ya hace
  todo lo per-socio (crear rutina, agregar día, agregar ejercicio con series/reps), pero arranca cargando
  **todos** los socios (`GetPagedAsync(..., 500)`) para el combo del hub. `LoadAsync` es idempotente
  (`if Members.Count > 0 return`). El hub `RoutinesViewModel` = `Builder` + `Library`.
- `CreateRoutineCommand` usa `DateOnly.FromDateTime(DateTime.Today)` (residuo de F4-Q1/D12; no se corrige
  acá).

**Comisiones (el modelo ya está en la base, pero sin usar):**
- `Staff` (`src/GymForge.Domain/Entities/Staff.cs`): `CommissionPctMemberships`, `CommissionPctPt`,
  `CommissionPctProducts`, **mapeados `DECIMAL(5,2)`** (`CompanySiteStaffConfiguration.cs:63-65`) → un
  porcentaje 0..999,99; se usa como **0..100**. `Staff` es `IAuditable`. No hay método de dominio para
  setearlos ni UI de staff (solo `ChangePinCommand`).
- `Product.CommissionStaffPct` (`DECIMAL`, snapshot 1305) mapeado; `Product.Update(name, salePrice,
  costPrice, barcode)` **no** lo toca.
- `Membership.SoldByStaffId` (Guid?) + `Membership.CommissionAmount` (decimal) mapeados (snapshot 948/1001).
  `SellMembershipCommand` **ya** pasa `soldByStaffId: cmd.CashierId` al crear la membresía, pero **nunca
  setea `CommissionAmount`** (queda 0).
- `SaleLine.CommissionStaffId` (Guid?) mapeado (snapshot 1619); `SellProductCommand` **no** lo setea.
  `SaleLine` **no tiene** campo de monto de comisión → es el **único cambio de esquema** de esta parte
  (Pieza C).
- `Payment.CashierId` (Guid, requerido) identifica quién procesó. No hay flujo de venta de **Personal
  Training**: los cargos PT (`ConceptType.PersonalTraining`) se cobran por `ProcessPaymentCommand`, no por un
  `Sell*Command`, así que `CommissionPctPt` **queda fuera de v1** (ver decisión 5).
- `IStaffRepository.GetActiveByCompanyAsync` y `GetByIdAsync` ya existen (`IRepositories.cs:140-146`);
  `StaffDto` existe (`DTOs/StaffDto.cs`).
- **Infra de reporte/export de ADR-007 disponible y se reutiliza:** `IReportExporter`, `ReportDocument`/
  `ReportTable`/`ReportColumn(Type)`, los builders puros de `ReportBuilders.cs`, `ReportExportService`
  (diálogo nativo) y el hub `ReportsViewModel` con `enum ReportKind`. La desagregación de comisiones es un
  **nuevo `ReportKind`** que vuelca un `ReportDocument` por el mismo camino. `CompanyTime`/`ReportPeriod`
  (bordes UTC por día de la empresa) ya existen.

---

## Decisión — resumen

| # | Pieza | Decisión clave | Dónde vive | Migración EF | Multi-tenant | UI | Tamaño |
|---|---|---|---|---|---|---|---|
| A | Editar socio desde la ficha | `UpdateMemberCommand` + método de dominio de edición; se edita en la **pestaña Datos** (modo edición), **no** se reusa `CreateMemberViewModel` | Domain + Application + Desktop | **No** | `CompanyId`+guard; sin cambio de sede | Sí | M |
| B | Rutinas en la ficha | Modo embebido de `RoutineBuilderViewModel` (`InitForMemberAsync` + `ShowMemberPicker=false`); reutiliza los use cases de `RoutineCommands.cs`; D3 queda **anotado, no se toca** | Desktop (+ anotación Domain) | **No** | vía el socio (site-scoped) | Sí | M |
| C | Comisiones: cálculo y persistencia | **Snapshot al vender** en la entidad de la transacción. **No hay entidad nueva** (no `CommissionLedger`). Agrega `SaleLine.CommissionAmount`; setea `Membership.CommissionAmount` | Domain + Application + Infra | **Sí** (1: `AddSaleLineCommissionAmount`) | `CompanyId`+`SiteId` (pago/venta de la sede) | No | M |
| D | Comisiones: tarifas editables | `Staff.SetCommissions` + `UpdateStaffCommissionsCommand` (card en Config) y `Product.CommissionStaffPct` editable en el ABM de Productos | Domain + Application + Desktop | **No** | `CompanyId` | Sí | M |
| E | Comisiones: reporte "Comisiones del staff" | `GetStaffCommissionsQuery` + 2 lecturas de repo + builder; **reutiliza** el export de ADR-007; nuevo `ReportKind` en el hub | Application + Infra + Desktop | **No** | `SiteId` o toda la empresa (patrón ADR-007) | Sí | L |

### Decisiones transversales (leer antes del detalle)

- **Una sola migración EF en toda la parte 2:** `AddSaleLineCommissionAmount` (Pieza C). Todo lo demás
  (comisiones de Staff/Membership/Product) **ya es columna** desde `InitialCreate`; editar socio y rutinas
  en la ficha **no tocan el esquema**. La migración se agrega con el design-time factory de Infrastructure
  (CLAUDE.md): `dotnet ef migrations add AddSaleLineCommissionAmount --project src/GymForge.Infrastructure
  --startup-project src/GymForge.Infrastructure`, y `dev` verifica Up/Down y que
  `migrations has-pending-model-changes` quede limpio (precedente de F3).
- **Comisiones = snapshot, no ledger, no derivación.** La comisión se **calcula y se congela al momento de
  la venta** sobre la propia transacción (`Membership.CommissionAmount`, `SaleLine.CommissionAmount`). El
  reporte **suma snapshots**, no recalcula. Motivos: (1) los porcentajes cambian con el tiempo —derivar en
  el reporte haría que un cambio de % reescribiera comisiones históricas; el snapshot las vuelve inmunes;
  (2) respeta append-only (no mete nada en AuditLog/AccessLog) y no agrega una entidad-libro que habría que
  mantener sincronizada. Alternativas evaluadas y descartadas en la Pieza C.
- **Quién gana la comisión (v1): el cajero que procesó la venta** (`Payment.CashierId`, que
  `SellMembershipCommand` ya usa como `SoldByStaffId`). El login de caja es por PIN, así que el cajero
  identifica al staff. Elegir un **vendedor distinto del cajero** en el momento de la venta es un agregado de
  UI que queda como decisión a confirmar (decisión 4), no v1.
- **Editar socio no reusa el form del alta.** `CreateMemberViewModel` está acoplado a lógica que no aplica a
  una edición (cobro de la primera cuota, alta de credencial, activación inmediata, antiduplicado por
  reintento). Reusarlo entero ensucia los dos módulos. Se edita **en la pestaña Datos de la ficha** (que ya
  es el lugar de foto y credencial), con su propio comando acotado. Límites de módulo claros
  (CLAUDE.md: simplicidad > cleverness).
- **PII al AuditLog en la edición (ligado a D11/Q4):** editar identidad/contacto de un socio escribe el diff
  old→new en el AuditLog append-only (DNI, email, celular). Ver la nota y la decisión 2 en la Pieza A.

---

## Pieza A — Editar socio desde la ficha

### Qué falta
Un caso de uso de edición y un método de dominio para cambiar identidad. Hoy solo se puede editar foto y
credencial desde la ficha.

### Diseño

**Dominio** (`src/GymForge.Domain/Entities/Member.cs`). Un método de edición de identidad + reuso de
`UpdateContact`/`CompleteProfile`:

```csharp
/// <summary>Corrige los datos de identidad del socio (no cambia estado, sede ni fecha de alta).</summary>
public void EditIdentity(
    string firstName, string lastName, DocumentType documentType, string documentNumber, Gender gender)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
    ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
    ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
    FirstName = firstName; LastName = lastName;
    DocumentType = documentType; DocumentNumber = documentNumber; Gender = gender;
    UpdatedAt = DateTime.UtcNow;
}
```

**Application** (`src/GymForge.Application/UseCases/Members/UpdateMemberCommand.cs`, nuevo):

```csharp
public record UpdateMemberCommand(
    Guid CompanyId, Guid MemberId,
    string FirstName, string LastName,
    DocumentType DocumentType, string DocumentNumber, Gender Gender,
    string? Email, string? Mobile, DateOnly? BirthDate,
    bool MarketingConsent) : IRequest<MemberDto>;
```

- **Validador**: mismas reglas que `CreateMemberCommandValidator` (NotEmpty + MaxLength de nombre/apellido/
  documento; `Email` válido cuando no está vacío; `Mobile` ≤ 30; fecha de nacimiento no futura/absurda). No
  se valida DNI duplicado (igual que el alta; D7 sigue abierto).
- **Handler**: `GetByIdAsync(MemberId)` → si null o `member.CompanyId != cmd.CompanyId` lanza
  "El socio no existe."; aplica `EditIdentity(...)`, `UpdateContact(Email, Mobile)` y un seteo de
  `BirthDate`/`MarketingConsent` (reusar `CompleteProfile` conservando `Source`/`SalesRepId` actuales, o un
  método chico `UpdateBirthDate`/`SetMarketingConsent`); `Update(member)` + `SaveChangesAsync`; devuelve
  `MemberDto.FromEntity`.
- **Campos editables**: nombre, apellido, tipo y nº de documento, género, email, celular, fecha de
  nacimiento, consentimiento de marketing. **No editables acá** (ya tienen su camino o su ciclo de vida):
  `Status` (lifecycle), `TagSerial` (`AssignCredentialCommand`), foto (`PhotoPickerViewModel`), `JoinDate`
  (la fija `Activate`), datos médicos/de emergencia (fuera de v1 — decisión 3). No se cambia de sede.

**Desktop** (`MemberDetailViewModel` + `MemberDetailView.axaml`, pestaña Datos):
- Agregar a la pestaña Datos un **modo edición**: botón "Editar" → los campos de identidad/contacto pasan a
  editables con "Guardar" / "Cancelar". `EditMode` (bool) en el VM; copias editables de los campos
  (`EditFirstName`, …) para poder cancelar sin pisar `Member`; `SaveCommand` manda `UpdateMemberCommand`,
  recarga `Member` con el DTO devuelto y sale del modo edición; `CancelCommand` descarta. Mensaje de error
  inline (patrón de `SaveCredentialAsync`: `catch ValidationException` → join de errores).
- `ux` define el layout (modo edición inline vs. sub-card), estados y foco inicial.

### Nota de PII (D11/Q4) — **decisión 2**
Al guardar, `Member` queda `Modified` y el `AuditSaveChangesInterceptor` escribe el diff **old→new de los
escalares que cambiaron** al AuditLog append-only: eso incluye **DNI, email y celular** (y `Observations` si
se editara). Es dato personal que, una vez en el log, **no se puede purgar** (Ley 25.326). Opciones:
- **(Recomendado v1, seguro por defecto)** agregar los campos de identidad/contacto del socio
  (`DocumentNumber`, `Email`, `Mobile`, y `Observations` si entra) al set `RedactedProperties` del
  interceptor: el audit sigue registrando **quién, cuándo y qué campo** cambió (`"DocumentNumber": {old:
  [redacted], new: [redacted]}`), sin inmortalizar el valor. Mantiene la traza de ediciones sin ampliar la
  huella de PII.
- **(Alternativa)** dejar el diff con valores: más útil para auditar correcciones, pero graba PII permanente.
  Es, de fondo, la decisión D11 que el usuario tiene abierta.

No se amplía la huella de PII sin que el usuario lo decida: por defecto se redacta (opción recomendada).

### Tests (TDD)
`tests/GymForge.Application.Tests/Handlers/UpdateMemberCommandTests.cs`:
- Edita nombre/documento/contacto y persiste; el `MemberDto` refleja los cambios.
- `CompanyId` que no coincide (o socio inexistente) → lanza; no modifica nada.
- Validación: nombre vacío, email inválido, fecha de nacimiento futura → `ValidationException`.
- No cambia `Status`, `JoinDate` ni `SiteId`.
Integración (tenant): un socio de otra empresa no es editable (sumar caso en
`TenantAdversarialIntegrationTests`). Si se adopta la redacción (opción recomendada), un test de
`AuditSaveChangesInterceptor` que confirme que al editar DNI/email el diff del AuditLog queda `[redacted]`.
Desktop: `MemberDetailViewModelTests` — "Editar" entra en modo edición, "Guardar" manda el comando y recarga,
"Cancelar" descarta.

---

## Pieza B — Rutinas en la ficha

### Qué falta
La pestaña Rutinas de la ficha no carga nada; el armador vive solo en el hub y obliga a reelegir al socio.

### Diseño — reusar el armador, no duplicarlo

Separar en `RoutineBuilderViewModel` la **selección de socio** del **armado para un socio dado**, sin
reescribir los comandos (que ya están bien):
- Agregar un **modo embebido**: un método `InitForMemberAsync(MemberDto member)` que **no** carga los 500
  socios; setea `SelectedMember = member`, carga **solo** los ejercicios (para el form de item) y las
  rutinas de ese socio, y expone un bool `ShowMemberPicker` (= `false` embebido, `true` en el hub).
- `LoadAsync` (hub) queda como está (carga socios + ejercicios). Los comandos `CreateRoutineCommand`,
  `AddRoutineDayCommand`, `AddRoutineItemCommand` y las queries `GetMemberRoutinesQuery`/
  `GetRoutineDetailQuery` se **reutilizan sin cambios**.
- `MemberDetailViewModel`: inyectar un `RoutineBuilderViewModel` (transient) y, al entrar a la pestaña
  `Routines` (`LoadActiveTabAsync` → nuevo `case MemberDetailTab.Routines`), llamar
  `InitForMemberAsync(Member)`. Exponerlo como propiedad para el binding de la vista.
- `MemberDetailView.axaml`: la pestaña Rutinas hospeda la **misma** vista del armador
  (`Views/Routines/RoutineBuilderView`) con el selector de socio oculto (`ShowMemberPicker=false`). `ux`
  decide el encuadre dentro de la ficha (la vista del armador está pensada para el hub; puede necesitar
  ajustes de densidad, no de lógica).

### D3 (SiteId en Routine) — **no se toca acá; queda anotado**
`Routine`/`Booking`/`WorkoutLog`/`BodyMeasurement` son `ITenantScoped` sin `SiteId` (D3). Exponer rutinas en
la ficha **no empeora** D3: siempre se accede a través de un socio concreto, que **sí** es site-scoped, y las
queries filtran por `CompanyId` + `MemberId`. Agregar `SiteId` a `Routine` implicaría migración + tocar las
otras tres entidades: es una deuda transversal que excede estas tres features. **Decisión: no se agrega
`SiteId` en la parte 2**; D3 sigue abierto en `docs/TASKS.md`.

### Tests (TDD)
`tests/GymForge.Desktop.Tests/RoutineBuilderViewModelTests.cs` (ampliar):
- `InitForMemberAsync` fija `SelectedMember`, carga ejercicios y las rutinas del socio, y **no** carga la
  lista de socios; `ShowMemberPicker` queda `false`.
- Crear rutina / agregar día / agregar ejercicio funcionan igual en modo embebido (reusa los comandos).
`tests/GymForge.Desktop.Tests/MemberDetailViewModelTests.cs`: entrar a la pestaña Rutinas inicializa el
armador con el socio actual.

---

## Pieza C — Comisiones: cálculo y persistencia (snapshot)

### Qué falta
Que la comisión se **calcule y se guarde al vender**. Hoy los porcentajes existen pero el monto nunca se
registra.

### Diseño — snapshot en la transacción, sin entidad nueva

**Esquema (única migración de la parte 2):** agregar `SaleLine.CommissionAmount` (decimal, por línea; default
0). `Membership.CommissionAmount` ya existe.

```csharp
// SaleLine (Product.cs): nuevo campo + setter de dominio
public decimal CommissionAmount { get; private set; }
public void SetCommission(Guid staffId, decimal amount) { CommissionStaffId = staffId; CommissionAmount = amount; }

// Membership.cs: setter de dominio (el campo ya existe)
public void SetCommission(Guid staffId, decimal amount) { SoldByStaffId = staffId; CommissionAmount = amount; }
```

**Cálculo (regla única, es-AR, redondeo a 2 decimales):** `comisión = Round(base * pct / 100m, 2,
MidpointRounding.AwayFromZero)`, con `pct` como **porcentaje 0..100** (coherente con `DECIMAL(5,2)`).
- **`SellMembershipCommand`** (`UseCases/Sales/SellMembershipCommand.cs`): inyectar `IStaffRepository`,
  cargar el vendedor (`cmd.CashierId`), `base = plan.Price`, `pct = staff.CommissionPctMemberships` →
  `membership.SetCommission(cmd.CashierId, comisión)` antes de persistir. Ya pasa `soldByStaffId`.
- **`SellProductCommand`** (`UseCases/Sales/SellProductCommand.cs`): `base = line.LineTotal` (sin IVA),
  `pct = product.CommissionStaffPct` → `line.SetCommission(cmd.CashierId, comisión)`.
- Si el % es 0 (default) la comisión es 0: feature "apagada" sin ramas especiales.
- **No** se calcula comisión en `ProcessPaymentCommand` (cobro de cuotas ya emitidas): esa comisión ya quedó
  congelada en la `Membership` al vender; recalcular al cobrar la duplicaría. El cobro de deuda no genera
  comisión nueva.

### Alternativas evaluadas (y descartadas)
- **Entidad `CommissionLedger` append-only** (una fila por comisión ganada): más "contable", pero duplica
  datos que ya viven en la transacción, agrega otra migración y otra fuente de verdad a sincronizar. Se deja
  para v2 solo si aparecen ajustes/clawbacks de comisión (hoy no hay).
- **Derivar en el reporte** desde los % actuales: rompe el histórico cuando el dueño cambia un %. El snapshot
  es la decisión correcta.

### Tests (TDD)
`tests/GymForge.Application.Tests/Handlers/SellMembershipCommandTests.cs` (ampliar) y
`SellProductCommandTests.cs`:
- Con % > 0, la comisión se calcula (`base * pct / 100`, redondeada) y queda en `Membership.CommissionAmount`
  / `SaleLine.CommissionAmount`, con `SoldByStaffId`/`CommissionStaffId = CashierId`.
- Con % = 0 (o staff sin %), comisión 0.
- Redondeo: `$12.345 * 7,5%` → monto esperado exacto.
Dominio: `MembershipTests`/`SaleLineTests` — `SetCommission` setea staff + monto.
Migración: `dev` verifica Up/Down con datos y `has-pending-model-changes` limpio.

---

## Pieza D — Comisiones: tarifas editables (Staff + Producto)

### Qué falta
No hay forma de editar los porcentajes. Staff no tiene ABM; `Product.Update` no incluye el %.

### Diseño

**Staff** (porcentajes por persona):
- Dominio: `Staff.SetCommissions(decimal pctMemberships, decimal pctPt, decimal pctProducts)` (valida
  0 ≤ pct ≤ 100; `UpdatedAt`).
- Application: `UpdateStaffCommissionsCommand(Guid CompanyId, Guid StaffId, decimal PctMemberships,
  decimal PctPt, decimal PctProducts)` → handler carga el staff (`IStaffRepository.GetByIdAsync` + guard
  `CompanyId`), aplica, `Update` + `SaveChanges`. Validador: cada pct en `[0, 100]`.
- Desktop: card **"Comisiones del personal"** en Configuración, lista el staff activo
  (`IStaffRepository.GetActiveByCompanyAsync`) con los % editables (membresías / productos; PT **deshabilitado
  con nota** "disponible cuando se agregue la venta de PT" — decisión 5). Guardado por staff.
  `Staff` es `IAuditable` → el cambio queda en AuditLog (sin PII; es un dato de negocio).

**Producto** (`Product.CommissionStaffPct`):
- Dominio: incluir el % en el camino de edición de producto (extender `Product.Update(...)` con el pct, o
  un `Product.SetCommissionPct(decimal pct)`), validando `[0, 100]`.
- Desktop: agregar el campo "% comisión" al ABM de Productos (`ProductsView`), junto a precio/costo.
- `ux` define la ubicación y el formato del campo (% con separador es-AR).

Sin migración (todas las columnas existen).

### Tests (TDD)
`tests/GymForge.Application.Tests/Handlers/UpdateStaffCommissionsCommandTests.cs`: aplica y persiste; pct
fuera de `[0,100]` → `ValidationException`; staff de otra empresa → lanza. Producto: el % viaja por el
comando de edición y se persiste. Desktop: VM de Config lista staff y guarda; VM de Productos edita el %.

---

## Pieza E — Comisiones: reporte "Comisiones del staff"

### Qué falta
Un reporte de cuánto le corresponde a cada staff en el período. Se apoya 100% en el export de ADR-007.

### Diseño

**Lecturas nuevas** (siteId null = toda la empresa; `IgnoreQueryFilters()` + `CompanyId` explícito, patrón
ADR-007). Período por el **timestamp de la transacción** con bordes UTC por día de la empresa
(`ReportPeriod.UtcBounds`, ya existe):

```csharp
// IMembershipRepository: membresías vendidas con comisión en el período (por fecha de alta de la membresía).
Task<IReadOnlyList<Membership>> GetSoldBetweenAsync(
    Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
// ISaleRepository: líneas con comisión en el período (incluye Sale para la fecha y la sede).
Task<IReadOnlyList<SaleLine>> GetCommissionLinesBetweenAsync(
    Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
```

(`GetSoldBetweenAsync` filtra `CommissionAmount > 0` por `Membership.CreatedAt`; `GetCommissionLinesBetweenAsync`
filtra `CommissionStaffId != null && CommissionAmount > 0` por `Sale.SaleDatetime`.)

**Query MediatR** (`src/GymForge.Application/UseCases/Reports/StaffCommissionsQuery.cs`):

```csharp
public record StaffCommissionRow(
    Guid StaffId, string StaffName, decimal MembershipCommission, decimal ProductCommission, decimal Total);
public record StaffCommissions(decimal Total, IReadOnlyList<StaffCommissionRow> Rows);
public record GetStaffCommissionsQuery(Guid CompanyId, Guid? SiteId, DateOnly From, DateOnly To)
    : IRequest<StaffCommissions>;   // SiteId null = toda la empresa
```

Handler: resolver nombres de staff (`IStaffRepository`), agrupar membresías por `SoldByStaffId` y líneas por
`CommissionStaffId`, sumar por staff, ordenar por total desc; `Total` = suma de las filas. **Invariante de
reconciliación** (test obligatorio): `Σ(MembershipCommission) + Σ(ProductCommission) = Σ(Total) = Total`.

**Reporte + export (reutiliza ADR-007):** un **builder puro** `(StaffCommissions + ReportBranding) ->
ReportDocument` en `ReportBuilders.cs`, tabla "Comisiones del staff": Staff (Text) · Comisión membresías
(Money) · Comisión productos (Money) · Total (Money); fila de totales. Nuevo `ReportKind.StaffCommissions` en
`ReportsViewModel`, con el mismo toggle `CompanyWide` (esta sede / toda la empresa) y "Exportar ▾"
(PDF/Excel) que los reportes de la parte 1. Sin infra de export nueva.

Multi-tenant / roles: el alcance "toda la empresa" es el mismo patrón y la misma decisión abierta que
ADR-007 (decisión 5 / **O10**): hoy cualquier sesión puede verlo. No se agregan roles finos acá (ver
decisión 6).

### Tests (TDD)
`tests/GymForge.Application.Tests/Handlers/GetStaffCommissionsQueryTests.cs`: agrupa y suma por staff;
reconciliación; respeta el período (bordes inclusivos por día de empresa); `SiteId` filtra una sede y
`null` toma todas las de la empresa (y ninguna de otra — tenant adversarial).
`tests/GymForge.Application.Tests/Reports/ReportBuildersTests.cs` (ampliar): el builder arma columnas/tipos y
la fila de totales. Desktop: `ReportsViewModelTests` — `ReportKind.StaffCommissions` carga y exporta.

---

## Orden de implementación (TDD)

Primero lo de menor riesgo y sin esquema; la UI al final de cada pieza. Un commit por feature (regla 7),
`dotnet test` verde antes de cada uno. **Una sola migración** (paso 3).

1. **Pieza A — Editar socio.** `Member.EditIdentity` + `UpdateMemberCommand` + validador + handler + guard
   de tenant; decisión de redacción de PII (decisión 2); modo edición en la pestaña Datos (`ux`). Tests de
   handler, tenant y VM.
   `feat: [socios] editar datos del socio desde la ficha`
2. **Pieza B — Rutinas en la ficha.** Modo embebido de `RoutineBuilderViewModel` (`InitForMemberAsync`,
   `ShowMemberPicker`) + caso `Routines` en `MemberDetailViewModel` + pestaña Rutinas en la ficha (`ux`).
   Tests de VM. Sin migración.
   `feat: [socios] armador de rutinas en la ficha`
3. **Pieza C — Comisiones: cálculo y persistencia.** `SaleLine.CommissionAmount` + migración
   `AddSaleLineCommissionAmount`; `Membership.SetCommission`/`SaleLine.SetCommission`; cálculo en
   `SellMembershipCommand`/`SellProductCommand`. Tests de handler, dominio y migración.
   `feat: [comisiones] calcular y registrar comision del staff en ventas`
4. **Pieza D — Tarifas editables.** `Staff.SetCommissions` + `UpdateStaffCommissionsCommand` + card en Config;
   `Product.CommissionStaffPct` editable en el ABM de Productos (`ux`). Tests de handler y VM. Sin migración.
   `feat: [comisiones] editar porcentajes por staff y producto`
5. **Pieza E — Reporte de comisiones.** `GetSoldBetweenAsync` + `GetCommissionLinesBetweenAsync` +
   `GetStaffCommissionsQuery` + builder (reconciliación) + `ReportKind.StaffCommissions` en el hub (`ux`).
   Reutiliza `IReportExporter`/`ReportExportService`. Tests de query, builder y VM. Sin migración.
   `feat: [reportes] comisiones del staff por periodo`

> **`ux` entra** en: A (modo edición de la pestaña Datos), B (pestaña Rutinas de la ficha), D (card de
> comisiones en Config + campo % en Productos), E (nuevo reporte en el hub). **Verificación (`qa`):** tests
> verdes + runtime con `tools/GymForge.Screenshots` en **light y dark** (editar socio, rutina en la ficha,
> Config→comisiones, Productos con %, reporte de comisiones y su export `.pdf`/`.xlsx`); migración de la
> Pieza C verificada Up/Down. Con F3-D1 abierto (checks fuera de CI), todo cambio visual se corre también con
> el tool de capturas.

## Piezas que tocan UI (entra `ux`)
- **A:** modo edición en la pestaña Datos de `MemberDetailView` (editar/guardar/cancelar, foco, errores).
- **B:** pestaña Rutinas de la ficha hospedando el armador con el selector de socio oculto.
- **D:** card "Comisiones del personal" en Configuración + campo "% comisión" en el ABM de Productos.
- **E:** nuevo reporte "Comisiones del staff" en el hub de Reportes (selector + toggle de alcance + export).

## Decisiones a confirmar con el usuario
1. **Comisiones = snapshot al vender (recomendado), sin entidad `CommissionLedger`.** Las comisiones quedan
   congeladas en la transacción y el reporte las suma. Confirmá que no necesitás ajustes/clawbacks de
   comisión en v1 (eso pediría el ledger, v2).
2. **PII en el AuditLog al editar socio (D11/Q4).** Recomendado **redactar** DNI/email/celular/`Observations`
   en el diff de edición (el log guarda quién/cuándo/qué campo, no el valor) para no inmortalizar PII en un
   log no purgable. ¿Lo dejamos redactado por defecto, o preferís que el audit guarde los valores viejos y
   nuevos (es la decisión de fondo D11)?
3. **Alcance de "editar socio" (v1).** Edita identidad + contacto + fecha de nacimiento + consentimiento.
   ¿Alcanza, o querés editar también **datos médicos y contacto de emergencia** desde la ficha? (son datos de
   salud; sumarlos implica cuidar su redacción en AuditLog, igual que la decisión 2).
4. **Vendedor de la comisión.** v1: la comisión es del **cajero logueado** (quien procesa la venta).
   ¿Querés poder **elegir otro vendedor** en el momento de vender la membresía/producto? (agrega un selector
   en Caja/alta; más trabajo).
5. **Comisión de Personal Training (`CommissionPctPt`).** Queda **fuera de v1** porque no hay flujo de venta
   de PT (los cargos PT se cobran como cualquier cuota). ¿Confirmás que PT espera a que exista esa venta, o
   querés que v1 atribuya comisión de PT al cobrar un cargo `PersonalTraining`?
6. **Roles para ver "toda la empresa" (O10, igual que ADR-007).** El reporte de comisiones cruza sedes de la
   empresa. Hoy cualquier sesión de recepción puede verlo (no hay roles finos). ¿Lo dejamos visible para
   quien tenga sesión, o lo limitamos a admin? Misma decisión abierta que los reportes de la parte 1.
7. **Unidad del porcentaje.** Se asume **0..100** (ej.: `10` = 10%), coherente con `DECIMAL(5,2)`.
   Confirmá que ese es el criterio para la UI y el seed.

---

## Consecuencias
- **Esquema:** una sola migración nueva, `AddSaleLineCommissionAmount` (una columna decimal). Todo lo demás
  de comisiones ya era columna desde `InitialCreate`; editar socio y rutinas en la ficha no tocan la base.
- **Dominio:** `Member` suma `EditIdentity`; `Membership`/`SaleLine`/`Staff`/`Product` suman setters de
  comisión; nada rompe invariantes existentes (constructores privados + factory se mantienen).
- **Application:** `UpdateMemberCommand`, `UpdateStaffCommissionsCommand`, `GetStaffCommissionsQuery`
  (nuevos); `IMembershipRepository`/`ISaleRepository` suman 2 lecturas de solo lectura; `IStaffRepository`
  se usa desde las ventas. `SellMembershipCommand` suma una dependencia (`IStaffRepository`).
- **Desktop:** la pestaña Datos gana edición; la pestaña Rutinas reutiliza el armador; Config gana una card;
  Productos gana un campo; Reportes gana un `ReportKind`. El export no cambia (reusa ADR-007).
- **Comisiones:** quedan congeladas al vender (inmunes a cambios de % posteriores). Un cambio de % **no**
  reescribe comisiones históricas (por diseño).
- **Multi-tenant:** todas las escrituras filtran por empresa (+ sede en ventas); el reporte "toda la empresa"
  usa el patrón `IgnoreQueryFilters()` + `CompanyId` ya establecido, acotado a una empresa.
- **AuditLog:** editar socio y editar % de staff generan filas de audit (es lo esperado). La única atención
  es la PII del diff de socio (decisión 2).

## Riesgos
- **PII en AuditLog (editar socio).** Si se adopta "guardar valores" en vez de redactar, se acumula PII
  permanente en un log no purgable (D11). Mitigación: redactar por defecto (decisión 2).
- **Unidad del porcentaje mal interpretada.** Si el seed/UI mezclan 0..1 con 0..100, las comisiones salen
  100× o /100. Mitigación: un único helper de cálculo + tests de redondeo + decisión 7.
- **Doble conteo de comisión.** Si alguien agregara cálculo en `ProcessPaymentCommand`, se duplicaría lo ya
  congelado en la membresía. Mitigación: el cobro de deuda **no** calcula comisión (documentado + test).
- **D3 latente (Routine sin SiteId).** No lo agrava esta parte (acceso vía socio), pero sigue abierto.
- **Reusar el armador en la ficha** podría arrastrar supuestos de layout del hub; `ux` ajusta densidad, no
  lógica.

## Fuera de alcance (deuda explícita → `docs/TASKS.md`)
- **F4-D1 (retención/churn cohort real):** **no entra.** Necesita una tabla de snapshots diarios de
  membresías (histórico temporal) que no existe; no es ninguna de las tres features; Fase 5+.
- **F4-D2 (churn por vencimiento en altas/bajas):** **no entra.** Es una extensión del reporte de altas/bajas
  de la parte 1, no una de las tres features de la parte 2.
- **F4-D3 (detalle de líneas de producto del POS en la desagregación):** **no entra.** Es del reporte de
  recaudación desagregada (parte 1). Nota: las lecturas de `SaleLine` que agrega la Pieza E podrían ayudar a
  una futura F4-D3, pero no se implementa.
- **F4-D4 (efectividad de cobranza con `NotificationLog`):** **no entra.** No se relaciona con las tres
  features; sigue en backlog.
- **`CommissionLedger` / ajustes y clawbacks de comisión:** v2 (decisión 1).
- **Comisión de PT** y **venta con vendedor distinto del cajero:** decisiones 4 y 5.
- **ABM completo de staff** (alta/baja/roles): fuera; la Pieza D solo edita porcentajes del staff existente.
- **Mover un socio de sede** y **editar datos médicos/de emergencia desde la ficha:** fuera de v1
  (decisión 3).

---

## Referencias
- Socio: `src/GymForge.Domain/Entities/Member.cs` · `src/GymForge.Application/UseCases/Members/CreateMemberCommand.cs` ·
  `src/GymForge.Desktop/ViewModels/Members/MemberDetailViewModel.cs` · `Views/Members/MemberDetailView.axaml` ·
  `ViewModels/Members/CreateMemberViewModel.cs`
- Rutinas: `src/GymForge.Domain/Entities/Routine.cs` · `src/GymForge.Application/UseCases/Routines/RoutineCommands.cs` ·
  `src/GymForge.Desktop/ViewModels/Routines/RoutineBuilderViewModel.cs` · `RoutinesViewModel.cs` ·
  `Views/Routines/RoutineBuilderView.axaml`
- Comisiones (dominio existente): `src/GymForge.Domain/Entities/Staff.cs` (`CommissionPct*`) ·
  `src/GymForge.Domain/Entities/Membership.cs` (`SoldByStaffId`, `CommissionAmount`) ·
  `src/GymForge.Domain/Entities/Product.cs` (`Product.CommissionStaffPct`, `SaleLine.CommissionStaffId`) ·
  `src/GymForge.Application/UseCases/Sales/SellMembershipCommand.cs` · `SellProductCommand.cs` ·
  `UseCases/Charges/ProcessPaymentCommand.cs`
- EF: `src/GymForge.Infrastructure/Persistence/Configurations/CompanySiteStaffConfiguration.cs` ·
  `ProductSaleShiftConfiguration.cs` · `Migrations/GymForgeDbContextModelSnapshot.cs`
- Export reutilizado (ADR-007): `src/GymForge.Application/Interfaces/IReportExporter.cs` ·
  `UseCases/Reports/{ReportModel,ReportBuilders,ReportPeriod}.cs` · `Services/CompanyTime.cs` ·
  `src/GymForge.Desktop/Services/ReportExportService.cs` · `ViewModels/Reports/ReportsViewModel.cs`
- AuditLog / PII: `src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs` (D11/Q4)
- Repos/interfaces: `src/GymForge.Application/Interfaces/IRepositories.cs`
- Sesión/cajero: `src/GymForge.Desktop/Services/SessionContext.cs` (`EffectiveCashierId`)
- Roadmap/estado: `docs/PROGRESS.md` · `docs/TASKS.md` (Deuda de F4 parte 1, F4-D1 a F4-D4) · `docs/adr/README.md`
