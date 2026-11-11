# ADR-013 — Política de datos personales: redacción de PII en AuditLog + PIN para exportar reportes

**Status:** Accepted
**Date:** 2026-11-11
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` · veta `qa`
**Decisión del usuario:** bundle **"Redactar + PIN"** (redactar PII en el borrado del AuditLog + exigir PIN para exportar reportes)
**Extiende:** ADR-003 (§2 AuditLog append-only) · **resuelve** las decisiones **5** (alcance "toda la empresa") y **6** (PII en el archivo exportado) de ADR-007
**Cierra:** D11 ampliada · Q4 · O10 (`docs/TASKS.md`)

---

## Context

Dos superficies sacan datos personales (PII) del socio fuera del control de la Ley 25.326, y el usuario
decidió cerrar ambas con el mismo criterio:

1. **PII en el AuditLog (D11 + Q4).** El `AuditSaveChangesInterceptor` es **append-only** (regla de CLAUDE.md:
   nunca UPDATE/DELETE sobre AuditLog). Hoy:
   - En el **diff de un UPDATE de `Member`** la PII ya se redacta (decisión previa de F4 parte 2): nombre, DNI,
     email, teléfono, `Observations`, contacto de emergencia, grupo sanguíneo → `[redacted]`. Los datos de
     salud (`MedicalConditions`/`Medications`/`Allergies`) y el hash de PIN se redactan **siempre** (set
     `RedactedProperties`, por nombre de propiedad, en update y en delete).
   - En el **snapshot de un DELETE de `Member`** la PII **NO se redacta**: `SnapshotJson` llama a `Present(...)`
     con `redactPersonalData: false` hardcodeado, así que DNI, email, teléfono, nombre y `Observations`
     quedan **en claro y para siempre** en una tabla que no se puede purgar. (Los datos de salud sí se
     redactan, porque van por el set universal.) El borrado existe: `DeleteMemberCommand` hace un `Remove`
     real sobre socios sin movimientos (alta de prueba / por error).
   - `CustomFieldsJson` y `SignatureUrl` de `Member` **no están en ningún set de redacción**: hoy ninguna UI
     los edita, pero si mañana alguna los toca, su valor viajaría al diff y al snapshot.
   - **Q4:** `Membership.FreezeReason` es texto libre y el watermark del modal invita a "lesión"
     (`FreezeModalView.axaml:85` → `"Viaje, lesión, licencia..."`). Va **sin redactar** al AuditLog en cada
     freeze/unfreeze (y en un eventual delete de `Membership`): termina siendo **dato de salud no purgable**.

2. **Export de reportes sin control (O10 + decisiones 5 y 6 de ADR-007).** Cualquier sesión de recepción
   puede exportar a un archivo local, con el diálogo nativo, reportes con PII y finanzas: **Morosos** (nombre,
   celular, deuda), **Altas y bajas** (DNI, nombre, motivo de baja), **Recaudación** (detalle y desagregada),
   **Comisiones de staff**, inclusive con alcance **"toda la empresa"** (cruza sedes). No hay gate: una
   recepción desatendida puede llevarse "toda la empresa" en un `.xlsx`.

El usuario eligió: **redactar la PII que falta en el AuditLog** y **exigir PIN para exportar**.

### Hechos del código verificados al 2026-11-11

- **Interceptor:** `src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`.
  - `RedactedProperties` (líneas 29-35): `Staff.PinCodeHash`, `Member.MedicalConditions`, `Member.Medications`,
    `Member.Allergies`. Se redactan por **nombre**, en update y delete, en cualquier entidad.
  - `RedactedMemberUpdateProperties` (líneas 40-53): set de PII del socio redactado **solo en el update**
    (gateado por `redactPersonalData`, que `ChangedValuesJson` fija en `entry.Entity is Member`). **No** incluye
    `CustomFieldsJson` ni `SignatureUrl`.
  - `SnapshotJson` (líneas 193-196): delete → `Present(..., redactPersonalData: false)` (de ahí la fuga de D11).
  - `Present` (líneas 207-213): `RedactedProperties` por nombre; `RedactedMemberUpdateProperties` solo si
    `redactPersonalData`; `byte[]` → `<N bytes>`; null se deja null (no se inventa `[redacted]`).
- **Modelo:** `Member` (`IAuditable`) tiene `FirstName`, `LastName`, `DocumentNumber`, `Email`, `Mobile`,
  `BirthDate`, `Observations`, `EmergencyName`, `EmergencyPhone`, `EmergencyRelation`, `BloodType`,
  `CustomFieldsJson`, `SignatureUrl` (PII) + salud (ya redactada) + `FingerprintTemplate` (byte[], → tamaño).
  `Membership` (`IAuditable`) tiene `FreezeReason` (set en `Freeze`, limpiado en `Unfreeze`) y `CancelReason`
  (set en `Cancel`). Ningún otro `IAuditable` tiene una propiedad con esos nombres.
- **Export:** `ReportsViewModel.ExportAsync(ReportFormat)` (`src/GymForge.Desktop/ViewModels/Reports/
  ReportsViewModel.cs:336`) arma el `ReportDocument` del reporte activo y llama a `IReportExportService.
  ExportAsync` (diálogo nativo + escritura). Es el **único** punto de export de la app. 5 reportes, todos con
  PII o finanzas.
- **Auth por PIN:** `AuthenticateStaffCommand(CompanyId, Pin)` → `StaffDto?` (null = PIN inválido). Busca el
  staff **activo** de la company cuyo PIN matchea (PBKDF2). **No** filtra por rol. Precedente F1-D2:
  `SettingsViewModel.ConfirmImportAsync` y `SetBackupPassphraseAsync` gatean con este comando y aceptan
  **cualquier** PIN de staff válido (el texto dice "PIN de admin", pero el handler no verifica rol).
- **Roles:** existe `StaffRole { Admin, Receptionist, Trainer, Instructor, SalesRep, Manager }` y `StaffDto`
  expone `Role`. Pero **el rol no se usa como control de acceso en ninguna parte**: el login es solo por PIN,
  `SessionContext` guarda `CashierId`/`CashierName` pero **no el rol** del logueado, y no hay UI de gestión de
  roles. O10 lo resume: "no hay roles finos en la UI".

---

## Decisión

### 1. Redacción de PII en el AuditLog (D11)

En `AuditSaveChangesInterceptor`, **extender la redacción al snapshot de DELETE** y a los dos campos que
faltaban, **reutilizando el mecanismo existente** (sets de nombres + `Present`). Marcador: el mismo
`[redacted]` que ya se usa (constante `RedactedValue`). Un campo **vacío (null) se deja null** (no se disfraza
de redactado), igual que hoy.

- **El snapshot de DELETE redacta la PII del socio igual que el update.** `SnapshotJson` pasa a calcular
  `redactPersonalData = entry.Entity is Member` (mismo criterio que `ChangedValuesJson`), en vez del `false`
  hardcodeado. Así el set de PII del socio aplica **también** en el borrado.
- **Renombrar** `RedactedMemberUpdateProperties` → `RedactedMemberPersonalProperties` (ya no es "solo update").
- **Agregar** a ese set: `Member.CustomFieldsJson` y `Member.SignatureUrl` (D11 (b): por si una UI futura los
  edita; quedan redactados en update y en delete).

**Qué contiene el snapshot de un DELETE de `Member` después de esto:** los campos **no PII** siguen en claro
(`Id`, `CompanyId`, `SiteId`, `Status`, `JoinDate`, `DocumentType`, `Gender`, `Source`, `TagSerial`,
timestamps, etc.) — alcanza para probar "qué socio se borró, en qué estado, quién y cuándo"; la **PII** y la
**salud** van `[redacted]`; la **huella** sigue como `<N bytes>`.

### 2. Motivo de freeze fuera del AuditLog (Q4)

**Redactar `Membership.FreezeReason`** (recomendado sobre "sacarlo del diff": redactar deja la traza de *que*
el campo cambió sin exponer el valor, consistente con cómo se tratan los datos de salud).

- **Agregar `Membership.FreezeReason` al set universal `RedactedProperties`** (por nombre; ningún otro
  `IAuditable` tiene esa propiedad). Queda redactado **siempre**: en el diff del freeze/unfreeze (UPDATE de
  `Membership`) y en un eventual DELETE de `Membership`.
- **El valor operativo NO cambia:** `Membership.FreezeReason` se sigue guardando en la entidad (lo usan la
  ficha y la lógica de freeze). Solo **no** viaja al AuditLog.
- **`Membership.CancelReason` NO se redacta** (exclusión consciente): es un dato **operativo** que el negocio
  conserva a propósito y que el reporte de **Altas y bajas** muestra como "Motivo" (ADR-007 A.4 Tabla 3). No
  tiene el patrón "watermark que invita a dato de salud" de `FreezeReason`. Riesgo residual aceptado: si un
  operador escribe algo sensible en el motivo de baja, queda en la entidad y en el diff del AuditLog. Si a
  futuro preocupa, se redacta con el mismo patrón (NEW).

### 3. PIN para exportar reportes (O10)

**Interponer un gate de PIN antes de cualquier export de Reportes.** Se reutiliza `AuthenticateStaffCommand`
(el mismo patrón de F1-D2). El gate vive en la **UI** (`ReportsViewModel`): `IReportExportService` /
`ReportExportService` **no se tocan** (siguen siendo "generá y guardá el archivo", sin saber de permisos).

- **Alcance: TODOS los exports de Reportes** (los 5 reportes × PDF/Excel, con cualquier alcance de sede). No se
  clasifica reporte por reporte: todos tienen PII o finanzas y una regla única evita que un reporte nuevo se
  olvide el gate. El gate se pone en el **único** punto de export (`ExportAsync`).
- **Flujo (mismo espíritu que `ConfirmImportAsync`):** "Exportar ▾ → PDF/Excel" **no exporta directo**: abre un
  **modal de PIN** (antes de elegir el archivo). El usuario escribe el PIN y confirma:
  - PIN **inválido** (`AuthenticateStaffCommand` → null): **no se genera ni se guarda nada**, mensaje claro
    ("PIN incorrecto."), el campo queda abierto para **reintentar**, el modal no se cierra.
  - PIN **válido**: se cierra el modal y recién ahí corre el export real (arma el `ReportDocument` + diálogo
    nativo de guardado, como hoy).
  - **Cancelar**: cierra el modal, no exporta.
- **Qué es "admin" acá (limitación explícita):** el gate acepta **cualquier PIN de staff activo** de la
  empresa, igual que F1-D2. **No** es un control de rol: es un **gate de confirmación / de responsable** que
  evita el export desde una sesión desatendida y deja la intención de alguien que conoce un PIN. Aunque
  `StaffRole.Admin`/`StaffDto.Role` existen, **no** se exige rol Admin en esta versión, porque (a) es
  consistente con el precedente F1-D2; (b) el `SessionContext` no lleva el rol del logueado ni hay UI de
  gestión de roles; (c) un gimnasio de un solo staff quedaría trabado o el gate sería trivial. El endurecimiento
  a "solo Admin" es un cambio de una línea (`staff.Role == StaffRole.Admin`) que se difiere (ver "Fuera de
  alcance").

### 4. Alcance de roles (dejado explícito)

Hoy **no hay roles finos efectivos**: todos los PIN de staff son equivalentes como gate. Esta decisión no
inventa roles; el PIN es un gate mínimo de responsable. **El VIEW on-screen de datos de empresa/comisiones
sigue SIN gate**: la decisión del usuario fue sobre **exportar** (el artefacto que sale de la app), no sobre
ver en pantalla dentro de la sesión. Control de rol real (admin-only para export/company-wide/comisiones,
sesión que lleve el rol, UI de roles) queda como deuda futura.

---

## Qué NO se toca (límites)

- **Append-only del AuditLog:** nunca se hace UPDATE/DELETE de filas de AuditLog. La redacción aplica **de acá
  en adelante**, sobre lo que **entra**. **Lo ya escrito no se puede purgar** (es el punto de la regla). Las
  filas legadas con PII en claro quedan; si el gimnasio piloto ya tuvo borrados de socios, esos snapshots
  viejos **no** se limpian (purgarlos violaría append-only). Nota de release: ver "Consecuencias".
- **El valor operativo de los campos en las entidades:** `Member.*`, `Membership.FreezeReason`,
  `Membership.CancelReason` se siguen guardando y usando normalmente. La redacción es **solo** en el AuditLog.
- **`IReportExportService` / `ReportExportService`:** sin cambios (el gate es de UI, no del servicio de archivo).
- **La redacción en UPDATE de `Member` que ya existía:** se conserva; solo se amplía a delete + 2 campos.
- **El contenido del archivo exportado:** sigue llevando PII (el dueño necesita los nombres/celulares/deudas
  para operar). El recaudo que eligió el usuario es el **gate de PIN**, no mutilar el reporte. (Decisión 6 de
  ADR-007 cerrada así.)

---

## Consecuencias

- **AuditLog:** desde esta versión, ningún borrado de socio ni ningún freeze/unfreeze deja PII ni motivo de
  salud en el AuditLog. Se mantiene la trazabilidad no-PII (qué, quién, cuándo, en qué estado).
- **Un test existente cambia de intención (esperado):**
  `AuditInterceptorIntegrationTests.Delete_SnapshotsTheOriginalValues` hoy **afirma** que el snapshot del
  delete contiene `"FirstName":"Ana"` y `"DocumentNumber":"30111222"`. Con esta decisión eso pasa a ser
  `[redacted]`: el test se **reescribe** para afirmar la redacción de la PII + la conservación de un campo
  no-PII (p. ej. `"Status":"Prospect"`). No es una regresión; es el nuevo contrato.
- **Export:** un paso más (PIN) antes de bajar un reporte. Es un archivo local que no se transmite; el gate
  solo prueba responsable.
- **Nota de release (datos legados):** la redacción no es retroactiva. Si en el piloto ya se borraron socios o
  se congelaron membresías antes de esta versión, esos registros de AuditLog conservan la PII/motivo en claro
  y **no** se pueden purgar (append-only). No hay acción de migración; queda asentado.
- **Deuda futura abierta (no bloquea):** roles finos reales (admin-only para export, company-wide y
  comisiones; sesión con rol; UI de roles); redacción opcional de `CancelReason`; purga/retención de AuditLog
  legado (requeriría un ADR que relaje append-only con política de retención — hoy no se hace).

## Fuera de alcance

- **Roles finos / RBAC** (O10 v2): `Role == Admin` en el gate, gate de view on-screen, sesión que porte el rol.
- **Purga o anonimización de AuditLog legado** (choca con append-only; necesitaría su propio ADR de retención).
- **Redacción de PII de otras entidades `IAuditable`** en sus deletes (p. ej. `Staff` nombre/email): `Staff` no
  se borra en el producto (`Deactivate`, no `Remove`); se deja anotado pero no se implementa.
- **Mutilar/omitir columnas PII del archivo exportado** (el usuario eligió PIN, no recortar el reporte).

## Referencias

- Interceptor: `src/GymForge.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`
- Entidades: `src/GymForge.Domain/Entities/Member.cs` · `.../Membership.cs` (`Freeze`/`Unfreeze`/`Cancel`)
- Borrado real de socio: `src/GymForge.Application/UseCases/Members/DeleteMemberCommand.cs`
- Modal de freeze (watermark "lesión"): `src/GymForge.Desktop/Views/Members/FreezeModalView.axaml:85`
- Export: `src/GymForge.Desktop/ViewModels/Reports/ReportsViewModel.cs:336` ·
  `src/GymForge.Desktop/Services/ReportExportService.cs` (NO se toca)
- Auth por PIN y precedente F1-D2: `src/GymForge.Application/UseCases/Staff/AuthenticateStaffCommand.cs` ·
  `src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs:618,679`
- Roles: `src/GymForge.Domain/Enums/StaffRole.cs` · `src/GymForge.Application/DTOs/StaffDto.cs`
- Tests existentes de AuditLog: `tests/GymForge.Integration.Tests/AuditSensitiveDataQaTests.cs` ·
  `AuditInterceptorIntegrationTests.cs`
- ADRs relacionados: `docs/adr/ADR-003-estado-automatico-auditoria-cuenta-corriente.md` (§AuditLog) ·
  `docs/adr/ADR-007-reportes-y-export.md` (decisiones 5 y 6) · estado: `docs/PROGRESS.md` · `docs/TASKS.md`
