# ADR-007 — Reportes reales + export (Fase 4, parte 1)

**Status:** Accepted
**Date:** 2026-11-03
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` · veta `qa`
**Sesión:** 6 — Fase 4 "Profundidad" (parte 1) del roadmap (`docs/PROGRESS.md`)

---

## Context

Fase 4 "Profundidad" arranca por **reportes de negocio reales con exportación**. Hoy el dueño del gimnasio
solo tiene un reporte: la **recaudación del período** por sede (un `DataGrid` de pagos + dos KPIs), sin forma
de llevarse los datos fuera de la app. El objetivo de esta sesión es darle al dueño los números que mira para
tomar decisiones (quién le debe, de dónde viene la plata, cómo entran y salen socios) y poder **exportarlos a
PDF y a Excel** para imprimir, archivar o mandar al contador.

Son **tres familias de reporte** nuevas/extendidas + la **infraestructura de export** que las tres comparten:

1. **Morosos / deudores** (por sede): ya existe la consulta (`GetOverdueMembersQuery` de Fase 2). Acá solo se
   la hace **exportable** (no se recalcula nada).
2. **Recaudación desagregada**: además del total por período que ya existe, abrir la recaudación **por método
   de pago**, **por concepto/plan** y **por sede**.
3. **Altas y bajas del período** (lo que el pedido llamó "retención"): socios activos (foto de hoy), **altas**
   del período y **bajas** del período, con la métrica acotada a los datos que el dominio **ya** tiene (no se
   inventan campos).

La infraestructura de export es un **servicio genérico tabular** (`IReportExporter`) que cualquier reporte,
presente o futuro, puede usar para generar un PDF (QuestPDF, ya en el stack) o un Excel `.xlsx` (librería
nueva a decidir en este ADR).

Esta es la guía de implementación para `dev` en **TDD**: no inventar arquitectura, seguir las firmas y el
orden de abajo. Alcance acotado a estas tres familias + el export; nada más entra en esta parte de Fase 4
(editar socio desde la ficha, rutinas en la ficha y comisiones de staff quedan para la parte 2, ver roadmap).

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first**: todo lee de SQLite local. Ningún reporte depende de cloud. El export produce un archivo
  local; nada se transmite.
- **Multi-tenant** por `CompanyId` + `SiteId`. Ver la decisión transversal sobre el reporte "por sede", que es
  la única excepción deliberada (sigue acotado a **una** empresa; `SiteId` pasa de filtro a **dimensión**).
- **Logs append-only**: no aplica (los reportes solo **leen**; no tocan `AccessLog`/`AuditLog`/`NotificationLog`).
- **No hay migración EF en esta sesión**: las tres familias se calculan con los datos existentes. El único
  cambio de stack es **agregar una dependencia** para Excel (lo que este ADR autoriza; ver Pieza B).
- `dotnet test` verde antes de cada commit (regla 6). Piso declarado al cierre de F3: **1071 tests verdes**.
- Moneda ARS, formato `$35.000,00` (es-AR, símbolo pegado); fechas `dd/MM/yyyy`; zona horaria de la empresa
  para el "hoy" del negocio (`ICompanyCalendar`, no `DateTime.Today`).

### Hechos del código verificados al 2026-11-03

Se **reutiliza, no se duplica**:

- **Recaudación por período (existe).** `GetPaymentsReportQuery(CompanyId, SiteId, From, To)` →
  `PaymentsReport(Total, Count, Rows)` con `ReportPaymentRow(PaymentId, ReceivedAt, MemberName, Method,
  Amount)` (`src/GymForge.Application/UseCases/Reports/PaymentsReportQuery.cs`). Lee de
  `IPaymentRepository.GetByPeriodAsync` (solo `PaymentStatus.Completed`, incluye `Member`, más recientes
  primero; `ReceivedAt` es UTC y el rango local se aproxima por día calendario completo). **Se conserva** tal
  cual y se lo suma al set exportable.
- **Morosos por sede (existe, Fase 2).** `IChargeRepository.GetOverdueBySiteAsync(companyId, siteId, asOf)`
  (`IgnoreQueryFilters()` + `CompanyId && SiteId` explícitos, incluye `Member`) +
  `GetOverdueMembersQuery(CompanyId, SiteId, AsOf)` → `OverdueMemberDto(MemberId, FullName, Mobile,
  HasUsableMobile, TotalOutstanding, OldestDueDate, DaysOverdue, ChargeCount, CurrentStageLabel,
  LastReminderAt)`, ordenado por días de atraso desc
  (`src/GymForge.Application/UseCases/Dunning/GetOverdueMembersQuery.cs`). **Se reutiliza sin tocar.**
- **Recibo PDF (el molde del PDF de reportes).** `ReceiptPdfGenerator : IReceiptPdfWriter`
  (`src/GymForge.Infrastructure/Services/ReceiptPdfGenerator.cs`): QuestPDF Community
  (`QuestPDF.Settings.License = LicenseType.Community` en el `static` ctor), `NumberFormatInfo` es-AR
  (`$35.000,00`), color de marca validado (`#RRGGBB` o indigo `#6366F1`), logo opcional (`TryLoadLogo`),
  header de marca (logo + nombre + CUIT + sede) y tabla con header + filas. **Los helpers de marca, moneda y
  logo se extraen/comparten con el generador de reportes** (ver Pieza B); no se reimplementan.
- **Guardado del recibo (contraste a propósito).** `ReceiptService`
  (`src/GymForge.Desktop/Services/ReceiptService.cs`): genera los bytes y los escribe en una **carpeta fija**
  `%LOCALAPPDATA%\GymForge\recibos\AAAA-MM\{Code}.pdf`, y los **abre** con el visor del SO. Best-effort
  (nunca lanza). Los reportes usan **otro** destino (diálogo nativo; ver Pieza C).
- **Diálogo nativo de guardado (existe, se reutiliza el patrón).** `SettingsViewModel.ExportDataAsync`
  (`src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs:479`) ya usa
  `IStorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title, SuggestedFileName,
  DefaultExtension, FileTypeChoices })` + `file.TryGetLocalPath()`. El file picker de apertura
  (`OpenFilePickerAsync`) se usa en varios lados (importar CSV, logo, foto). **Mismo patrón** para el guardado
  del reporte.
- **Dominio disponible para "altas y bajas"** (verificado, para no inventar campos):
  - `Member.JoinDate` (`DateOnly?`): se setea en `Member.Activate(joinDate)` con `JoinDate ??= joinDate`
    (conserva la fecha de alta original). Es la señal de **alta**. `Member.Status` (`MemberStatus`:
    Prospect/Active/Suspended/Frozen/Overdue/Expired/Cancelled) es el estado **actual** (foto de hoy).
    `Member.Cancel()` **no** registra fecha → a nivel socio no hay fecha de baja.
  - `Membership.CancelDate` (`DateTime?`) + `CancelReason` (`string?`): se setean en `Membership.Cancel(reason)`.
    Es la única señal de **baja con fecha**. `Membership.EndDate`/`Status` existen para vencimientos.
  - `IMemberRepository.CountAsync(companyId, siteId, status?)` y `CountByCompanyAsync` ya existen (para
    licencia y listados). No hay consulta por `JoinDate` ni por estado agrupado todavía (se agregan en Pieza E).
- **Recaudación desagregada — grafo disponible** (verificado): `Payment` (`Method`, `Amount`, `SiteId`,
  `MemberId?`, `SaleId?`, `Status`) → `Payment.Allocations` (`PaymentAllocation.ChargeId`, `Amount`) →
  `Charge` (`ConceptType` [MembershipFee/SignupFee/LateFee/ProductSale/PersonalTraining/ClassDropIn/
  Adjustment], `MembershipId?`, `Description`) → `Membership.MembershipTypeId` → `MembershipType.Name` (el
  **plan**). `GetByPeriodAsync` **no** incluye `Allocations`: para la desagregación por concepto/plan hace
  falta un método que incluya el grafo (Pieza D). La suma de allocations de un pago puede ser **menor** que
  `Payment.Amount` (el excedente va a saldo a favor): esa diferencia se reconcilia en un bucket "A cuenta".
- **Navegación.** `NavSection.Reports` + `ReportsView`/`ReportsViewModel` ya existen (sección "Reportes" del
  sidebar). **No se agrega** sección nueva: Reportes pasa a ser un **hub** con selector de reporte + export.

---

## Decisión — resumen

| # | Pieza | Decisión clave | Dónde vive | UI | Tamaño |
|---|---|---|---|---|---|
| A | Set de reportes y columnas | 4 reportes exportables, columnas y agrupaciones fijadas abajo. Morosos y "recaudación del período" **reutilizan** consultas existentes | — | — | — |
| B | Export genérico tabular | Modelo `ReportDocument`/`ReportTable` + `IReportExporter` (Application) con impl en Infra: `ReportPdfGenerator` (QuestPDF) + `ReportXlsxGenerator` (**ClosedXML**) | Application + Infra | No | M |
| C | Dónde se guardan | **Diálogo nativo** `SaveFilePickerAsync` (el dueño elige lugar y nombre), no carpeta fija. Servicio `ReportExportService` en Desktop | Desktop | Sí | S |
| D | Recaudación desagregada | `GetForBreakdownAsync` (una sola lectura del período con el grafo) + `GetRevenueBreakdownQuery` → 3 cortes (método · concepto/plan · sede) en una pasada | Application + Infra | Sí | M |
| E | Altas y bajas | `GetMemberFlowQuery` sobre `JoinDate` (altas) + `Membership.CancelDate` (bajas) + foto de estados. Repos nuevos de lectura. **Sin % de retención cohort** (no hay histórico) | Application + Infra | Sí | M |
| F | Hub de Reportes | `ReportsViewModel` con selector de reporte + "Exportar ▾" (PDF/Excel) por reporte; reutiliza el flujo actual | Desktop | Sí | M |

### Decisiones transversales (leer antes del detalle)

- **Excel = ClosedXML.** MIT, 100% manejado (sin DLLs nativas) → compatible con el `publish.ps1` single-file
  self-contained y con offline. Genera `.xlsx` con **varias hojas** (una por corte), header estilado y celdas
  **numéricas reales** (el dueño puede re-sumar/pivotear). Se descarta CSV plano (pierde tipos, no tiene
  multi-hoja y en es-AR el separador decimal coma vs. el locale de Excel rompe columnas — el mismo dolor que
  ya obligó a autodetectar `,`/`;` en el import). **EPPlus queda descartado por licencia** (Polyform
  Noncommercial desde v5: comercial para uso comercial como el nuestro). Detalle y alternativa evaluada en la
  Pieza B. **Esta es la única modificación al stack congelado y es lo que este ADR autoriza** (requiere OK del
  usuario, decisión 1).
- **Guardado por diálogo nativo, no carpeta fija.** Un reporte es un artefacto que el dueño se **lleva**
  (lo imprime, lo archiva en su carpeta, lo adjunta a un mail), a diferencia del recibo que es un comprobante
  que el sistema **archiva** solo. El diálogo le deja elegir destino (Escritorio, pendrive, carpeta de
  Documentos) y nombre. Contraste deliberado con `ReceiptService` (carpeta fija). Detalle en Pieza C.
- **Sin migración EF.** Las tres familias se calculan con el esquema actual. Es una ventaja: menos riesgo,
  nada que versionar en la base.
- **Formateo centralizado en el exportador.** Las consultas/builders producen **valores crudos** (decimales,
  enteros, fechas) tipados por columna; el **exportador** (Infra) formatea es-AR para el PDF (texto) y setea
  el tipo/formato numérico real en el XLSX. Una sola fuente de formato de moneda/fecha (la misma cultura es-AR
  que ya usa `ReceiptPdfGenerator`). La grilla en pantalla sigue formateando con `StringFormat` del XAML
  (no cambia).
- **Reporte "por sede" = excepción documentada a la regla multi-tenant.** Es un reporte de **dueño**: cruza
  las sedes **de una misma empresa**. Sigue acotado por `CompanyId` (nunca cross-company); `SiteId` deja de
  ser filtro y pasa a ser la dimensión de agrupación. Se implementa con `IgnoreQueryFilters()` + `CompanyId`
  explícito (mismo patrón que `GetOverdueAsync`), nunca con el filtro ambiente. Los reportes por sede
  (morosos, recaudación del período) siguen filtrados por `SiteId`. Ver decisión 5.

---

## Pieza A — Set de reportes, columnas y agrupaciones

Cuatro reportes exportables. Cada uno se mapea a un `ReportDocument` con una o más `ReportTable` (Pieza B).
Las columnas listan su **tipo** (Text / Money / Integer / Date / Percent) porque el tipo gobierna el formato
en PDF y Excel.

### A.1 — Recaudación del período (DETALLE DE PAGOS) · reutiliza `GetPaymentsReportQuery`
Por **sede** y rango. Es el reporte que ya se ve en pantalla; solo se lo hace exportable.
Una tabla "Pagos del período":

| Columna | Tipo | Origen |
|---|---|---|
| Fecha | Date | `ReceivedAt` (local, `dd/MM/yyyy HH:mm`) |
| Socio | Text | `MemberName` (o "—") |
| Método | Text | `Method` en es-AR (reutilizar el mapeo de `ReceiptPdfGenerator.MethodLabel`/`EnumEs`) |
| Monto | Money | `Amount` |

Totales: `Total` (suma) · "Pagos: `Count`".

### A.2 — Recaudación desagregada · `GetRevenueBreakdownQuery` (Pieza D)
Por rango; **esta sede** o **toda la empresa** (toggle). Tres tablas (en PDF = secciones; en XLSX = tres
hojas). Cada fila lleva su **% sobre el total del corte**.

**Tabla 1 — Por método de pago:**

| Columna | Tipo | Origen |
|---|---|---|
| Método | Text | `Payment.Method` en es-AR |
| Monto | Money | suma de `Payment.Amount` del método |
| Pagos | Integer | cantidad de pagos |
| % | Percent | monto / total |

**Tabla 2 — Por concepto / plan:**

| Columna | Tipo | Origen |
|---|---|---|
| Concepto / Plan | Text | ver reglas de atribución abajo |
| Monto | Money | monto atribuido |
| % | Percent | monto / total |

Reglas de atribución (una pasada por las allocations del período; ver Pieza D):
- Allocation a un `Charge` con `ConceptType = MembershipFee` o `SignupFee` y `MembershipId` → fila con el
  **nombre del plan** (`MembershipType.Name`).
- Allocation a un `Charge` de otro concepto → fila por **concepto** en es-AR: "Productos", "Personal
  training", "Clases sueltas", "Recargos por mora", "Ajustes".
- Parte del pago **no cubierta por allocations** (`Payment.Amount − Σ allocations`) → bucket **"A cuenta
  (saldo a favor)"**.
- Pago **sin socio** con `SaleId` (venta POS a consumidor final) → **"Productos (venta directa)"**.
- **Invariante de reconciliación**: la suma de todas las filas = el total del período (igual que la Tabla 1 y
  la Tabla 3). Es un **test obligatorio** (Pieza D).

**Tabla 3 — Por sede** (solo con alcance "toda la empresa"; con "esta sede" es una fila):

| Columna | Tipo | Origen |
|---|---|---|
| Sede | Text | nombre de la sede (`ISiteRepository`, map `SiteId`→`Name`) |
| Monto | Money | suma de `Payment.Amount` de la sede |
| Pagos | Integer | cantidad |
| % | Percent | monto / total |

### A.3 — Morosos / deudores · reutiliza `GetOverdueMembersQuery` (Fase 2)
Por **sede**, a la fecha de hoy del negocio (`ICompanyCalendar.TodayFor(company)`). Una tabla "Morosos":

| Columna | Tipo | Origen (`OverdueMemberDto`) |
|---|---|---|
| Socio | Text | `FullName` |
| Celular | Text | `Mobile` (o "—") |
| Deuda | Money | `TotalOutstanding` |
| Cargos | Integer | `ChargeCount` |
| Vence desde | Date | `OldestDueDate` |
| Días de atraso | Integer | `DaysOverdue` |
| Etapa | Text | `CurrentStageLabel` |
| Último recordatorio | Date | `LastReminderAt` (o "—") |

Totales: "Deuda total" (suma de `TotalOutstanding`) · "Deudores: N". Orden: el que ya trae la query (días
desc). No se recalcula nada: el reporte es el builder DTO→tabla + el export.

### A.4 — Altas y bajas del período · `GetMemberFlowQuery` (Pieza E)
Por rango; **esta sede** o **toda la empresa**. Tres tablas:

**Tabla 1 — Resumen** (clave/valor):

| Fila | Valor |
|---|---|
| Activos (a hoy) | `CountByStatus[Active]` |
| Congelados / En mora / Suspendidos (a hoy) | desglose de estados (informativo) |
| Altas del período | socios con `JoinDate ∈ [desde, hasta]` |
| Bajas del período | membresías con `CancelDate ∈ [desde, hasta]` |
| Neto | Altas − Bajas |

**Tabla 2 — Altas del período:** Socio (Text) · Documento (Text) · Alta (Date = `JoinDate`).
**Tabla 3 — Bajas del período:** Socio (Text) · Plan (Text = `MembershipType.Name`) · Baja (Date =
`CancelDate`) · Motivo (Text = `CancelReason`, o "—").

> **Límite honesto (va en la UI y en el PDF como nota).** "Activos" es una **foto de hoy** (el dominio guarda
> el estado actual, no un histórico temporal), no "activos al cierre del período". "Bajas" = **cancelaciones
> de membresía** (`Membership.CancelDate`); **no** incluye "vencidas sin renovar" (churn por vencimiento) ni
> bajas a nivel socio (`Member.Cancel()` no guarda fecha). Por eso el reporte se llama **"Altas y bajas"**, no
> "retención": un % de retención/cohort honesto exige una tabla de snapshots diarios que hoy no existe (queda
> para Fase 5+; ver "Fuera de alcance"). Ver decisión 4.

---

## Pieza B — Export genérico tabular (`IReportExporter`)

### Qué falta
No hay forma de sacar datos de la app a un archivo. Se necesita un servicio **genérico** (que no sepa de
reportes concretos) para que agregar reportes después sea barato: cada reporte produce un `ReportDocument` y
el exportador lo vuelca a PDF o XLSX.

### Diseño

**Modelo tabular, en Application** (`src/GymForge.Application/UseCases/Reports/ReportModel.cs`). Valores
**crudos** tipados por columna; el exportador formatea.

```csharp
public enum ReportColumnType { Text, Money, Integer, Date, Percent }

public record ReportColumn(string Header, ReportColumnType Type);

/// <summary>Una tabla del reporte. Rows = filas de celdas crudas (string/decimal/int/DateOnly/DateTime/null),
/// una por columna, en el mismo orden que Columns. Totals = fila de cierre opcional (misma aridad).</summary>
public record ReportTable(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals = null);

/// <summary>Marca del gimnasio para el encabezado (mismos campos que usa el recibo).</summary>
public record ReportBranding(
    string GymName, string? GymTaxId, string? SiteName, string? LogoPath, string? BrandColorHex);

public record ReportDocument(
    ReportBranding Branding,
    string Title,       // "Morosos", "Recaudación desagregada", ...
    string Subtitle,    // "Sede Centro · 01/11/2026 a 03/11/2026"
    DateTime GeneratedAt,
    IReadOnlyList<ReportTable> Tables);
```

**Contrato del exportador, en Application** (`src/GymForge.Application/Interfaces/IReportExporter.cs`, al lado
de `IReceiptPdfWriter`):

```csharp
public enum ReportFormat { Pdf, Xlsx }

public interface IReportExporter
{
    byte[] Export(ReportDocument document, ReportFormat format);
    string Extension(ReportFormat format);   // "pdf" | "xlsx"
}
```

**Convenciones de celda por tipo de columna** (las aplica el exportador, única fuente de formato):
- `Text` → `string` (null → "—" en PDF, celda vacía en XLSX).
- `Money` → `decimal` → PDF: `$35.000,00` (es-AR, reutilizar el `NumberFormatInfo` de `ReceiptPdfGenerator`);
  XLSX: celda numérica con formato `"$"#,##0.00` adaptado a es-AR, para que sume en Excel.
- `Integer` → `int` → PDF texto; XLSX número entero.
- `Date` → `DateOnly`/`DateTime` → `dd/MM/yyyy` (o `dd/MM/yyyy HH:mm` si viene `DateTime` con hora); XLSX
  celda fecha.
- `Percent` → `decimal` **fracción 0..1** (0,42 → "42%"); XLSX formato porcentaje.

**Implementación en Infrastructure** (`src/GymForge.Infrastructure/Services/`):
- **Helpers compartidos con el recibo**: extraer de `ReceiptPdfGenerator` los helpers de marca/moneda/logo
  (`BuildArsFormat`/`Money`, `BrandColor`, `TryLoadLogo`) a un `ReportFormatting`/`BrandHelpers` estático (o
  internos compartidos) para no duplicar. `ReceiptPdfGenerator` pasa a usarlos (refactor chico, sin cambiar su
  salida; sus tests siguen verdes).
- **`ReportPdfGenerator`** (QuestPDF): header de marca igual al recibo (logo + nombre + CUIT + sede, color de
  acento), `Title` + `Subtitle`, y por cada `ReportTable`: título de sección + tabla (header en negrita, filas
  con hairline, fila de totales en `SemiBold` si viene). A4 vertical (los reportes tienen más columnas que el
  recibo A5). Footer con `GeneratedAt` y paginado.
- **`ReportXlsxGenerator`** (ClosedXML): un `XLWorkbook` con **una hoja por `ReportTable`** (nombre de hoja =
  `Title` saneado, ≤31 chars), fila 1 = encabezado de marca (nombre + subtítulo), header de columnas estilado
  (negrita + fondo tenue), filas tipadas, fila de totales en negrita; `AdjustToContents` para anchos.
- **`ReportExporter : IReportExporter`**: despacha por `ReportFormat` a uno de los dos generadores. Es lo que
  se inyecta (un solo servicio para el Desktop). Registrar en `InfrastructureServiceExtensions`:
  `services.AddSingleton<IReportExporter, ReportExporter>();` (los generadores son stateless, como
  `ReceiptPdfGenerator`).

**Dependencia nueva (lo que el ADR autoriza):** `ClosedXML` (`PackageReference` en `GymForge.Infrastructure`,
donde vive QuestPDF). Sus transitivas (DocumentFormat.OpenXml, SixLabors.Fonts, ExcelNumberFormat) son
**manejadas** (sin nativas) → el single-file self-contained sigue andando. `dev` debe verificar que
`scripts/publish.ps1` genera y abre un `.xlsx` real en Windows (va a la lista de "verificar en runtime").

### Alternativa evaluada (y descartada): CSV plano
- **A favor**: cero dependencias nuevas; abre en Excel.
- **En contra**: una sola "hoja" (los 3 cortes de la desagregación no entran bien); sin encabezado de marca
  ni estilo; **pierde tipos** (todo texto) → el dueño no puede re-sumar; y en es-AR el choque separador
  decimal coma / separador de campo `;` / locale de Excel reproduce el problema que ya sufrió el import de
  socios (autodetección `,`/`;`). Queda como **fallback** si el usuario rechaza sumar la dependencia
  (decisión 1): en ese caso se entrega solo PDF + CSV mínimo.
- **EPPlus**: descartado por **licencia** (Polyform Noncommercial desde v5; GymForge es software comercial).

### Tests (TDD)
`tests/GymForge.Infrastructure.Tests` (o `Integration.Tests` si ahí viven los de Infra; usar el proyecto que
ya tenga QuestPDF referenciado):
- `ReportXlsxGenerator`: round-trip — generar un `ReportDocument` de 2 tablas, **reabrir** los bytes con
  `XLWorkbook` y verificar nº de hojas, headers, una celda `Money` **numérica** con el valor exacto y una
  `Percent`. (ClosedXML lee lo que escribe: test cerrado sin abrir Excel.)
- `ReportPdfGenerator`: los bytes empiezan con `%PDF`, no vacíos; no lanza con logo ausente, color inválido,
  tabla vacía, celdas null y fila de totales presente/ausente.
- `ReportExporter.Extension` devuelve "pdf"/"xlsx"; `Export` despacha al generador correcto.
- Formato: una columna `Money` rinde `$1.234,50` en el texto del PDF o la celda del XLSX trae `1234.50` con el
  formato de moneda; `Percent` 0,42 → "42%".

---

## Pieza C — Dónde se guardan: diálogo nativo + `ReportExportService`

### Diseño
`ReportExportService` en Desktop (`src/GymForge.Desktop/Services/ReportExportService.cs`), hermano de
`ReceiptService` pero con **diálogo** en vez de carpeta fija. Best-effort (nunca rompe la vista):

```csharp
// Recibe el ReportDocument ya armado + el formato elegido.
public async Task<string?> ExportAsync(ReportDocument doc, ReportFormat format, CancellationToken ct = default)
// 1. bytes = _exporter.Export(doc, format);
// 2. SaveFilePickerAsync(new FilePickerSaveOptions {
//      Title = "Guardar reporte",
//      SuggestedFileName = Slug(doc.Title) + "-" + rango + "." + _exporter.Extension(format),
//      DefaultExtension = _exporter.Extension(format),
//      FileTypeChoices = [ pdf -> "Documento PDF (*.pdf)" | xlsx -> "Libro de Excel (*.xlsx)" ] });
// 3. file?.TryGetLocalPath() is not { } destino -> return null (cancelado);
// 4. File.WriteAllBytesAsync(destino, bytes, ct);
// 5. Process.Start(ProcessStartInfo(destino){UseShellExecute=true})  // abrir, best-effort, try/catch + Serilog
// 6. return destino;  (todo envuelto en try/catch con Log.Error, como ReceiptService)
```

Nombre sugerido: `morosos-2026-11-03.xlsx`, `recaudacion-desagregada-2026-11-01_a_2026-11-03.pdf`,
`altas-bajas-2026-11-01_a_2026-11-03.pdf`. `Slug` = minúsculas sin acentos ni espacios.

Registrar `services.AddSingleton<ReportExportService>()` en `ServiceCollectionExtensions` (Desktop), como
`ReceiptService`.

### Decisiones
- **Diálogo nativo** (recomendado): el reporte es un artefacto que el dueño se lleva; elige lugar/nombre; usa
  `IStorageProvider.SaveFilePickerAsync` (ya probado en `ExportDataAsync`). Se abre tras guardar (best-effort).
- **Descartada la carpeta fija** `%LOCALAPPDATA%\GymForge\reportes\AAAA-MM\`: sirve para comprobantes que el
  sistema archiva (recibos), no para algo que el usuario quiere mandar/imprimir ya. (Si el usuario prefiere lo
  contrario, decisión 2: es un cambio chico, solo este servicio.)

---

## Pieza D — Recaudación desagregada

### Diseño

**1. Lectura (una sola, con el grafo).** Nuevo método en `IPaymentRepository` (no tocar `GetByPeriodAsync`):

```csharp
/// <summary>
/// Pagos Completed del período para desagregar. siteId null = TODA la empresa (reporte de dueño por sede):
/// IgnoreQueryFilters() + CompanyId explícito. Incluye Allocations -> Charge -> Membership -> MembershipType.
/// </summary>
Task<IReadOnlyList<Payment>> GetForBreakdownAsync(
    Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
```

Implementación: `_db.Payments.IgnoreQueryFilters().Where(p => p.CompanyId == companyId && (siteId == null ||
p.SiteId == siteId) && p.ReceivedAt >= from && p.ReceivedAt < to && p.Status == PaymentStatus.Completed)` +
`.Include(p => p.Allocations).ThenInclude(a => a.Charge).ThenInclude(c => c.Membership).ThenInclude(m =>
m.MembershipType)`. (El `IgnoreQueryFilters` solo abre la **sede** dentro de la misma empresa; el `CompanyId`
explícito mantiene el límite de tenant.)

**2. Query MediatR** (`src/GymForge.Application/UseCases/Reports/RevenueBreakdownQuery.cs`):

```csharp
public record RevenueBreakdownRow(string Label, decimal Amount, int Count);
public record RevenueBreakdown(
    decimal Total,
    IReadOnlyList<RevenueBreakdownRow> ByMethod,
    IReadOnlyList<RevenueBreakdownRow> ByConcept,   // concepto/plan
    IReadOnlyList<RevenueBreakdownRow> BySite);
public record GetRevenueBreakdownQuery(Guid CompanyId, Guid? SiteId, DateOnly From, DateOnly To)
    : IRequest<RevenueBreakdown>;   // SiteId null = toda la empresa
```

Handler: una pasada sobre la lista de `GetForBreakdownAsync`:
- `Total` = Σ `Payment.Amount`.
- `ByMethod` = group by `Method` (label es-AR; `Count` = nº pagos).
- `BySite` = group by `SiteId` → nombre vía `ISiteRepository.GetByCompanyAsync` (map id→name).
- `ByConcept` = recorrer allocations con las reglas de atribución de A.2 (plan por `MembershipType.Name`;
  concepto en es-AR para el resto; "A cuenta (saldo a favor)" para `Amount − Σ allocations`; "Productos
  (venta directa)" para pago sin socio con `SaleId`). Ordenar cada corte por monto desc.
- El `%` por fila lo calcula el **builder** del reporte (no la query), para no mezclar presentación.

Rango: `From`/`To` a UTC por día calendario completo, igual que `GetPaymentsReportQuery` (copiar la conversión
`from = From.ToDateTime(MinValue, Utc)`, `to = To.AddDays(1).ToDateTime(MinValue, Utc)`).

### Tests (TDD)
`tests/GymForge.Application.Tests/Handlers/GetRevenueBreakdownQueryTests.cs` (fakes):
- `ByMethod` agrupa y suma por método; `Count` correcto.
- `ByConcept` atribuye membresía al **plan** y producto/clase/mora a su concepto; bucket "A cuenta" recibe
  `Amount − Σ allocations`; venta sin socio → "Productos (venta directa)".
- **Reconciliación**: Σ `ByMethod` == Σ `ByConcept` == Σ `BySite` == `Total` (en todos los escenarios).
- `BySite` con `SiteId = null` trae varias sedes; con `SiteId` fijo, una sola.
Integración (`GetForBreakdownAsync`): respeta período y `Completed`; con `siteId` dado no trae otra sede; con
`siteId = null` trae todas las de la empresa y **ninguna de otra empresa** (sumar caso en
`TenantAdversarialIntegrationTests`).

---

## Pieza E — Altas y bajas del período

### Diseño

**Lecturas nuevas** (siteId null = toda la empresa; `IgnoreQueryFilters()` + `CompanyId` explícito):

```csharp
// IMemberRepository
/// <summary>Conteo de socios por estado (foto de hoy). siteId null = toda la empresa.</summary>
Task<IReadOnlyDictionary<MemberStatus,int>> CountByStatusAsync(
    Guid companyId, Guid? siteId, CancellationToken ct = default);
/// <summary>Socios cuya JoinDate cae en [from, to] (altas del período). siteId null = toda la empresa.</summary>
Task<IReadOnlyList<Member>> GetJoinedBetweenAsync(
    Guid companyId, Guid? siteId, DateOnly from, DateOnly to, CancellationToken ct = default);

// IMembershipRepository
/// <summary>Membresías canceladas con CancelDate en [from, to] (bajas). Incluye Member y MembershipType.</summary>
Task<IReadOnlyList<Membership>> GetCancelledBetweenAsync(
    Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default);
```

**Query MediatR** (`src/GymForge.Application/UseCases/Reports/MemberFlowQuery.cs`):

```csharp
public record MemberFlowJoin(string FullName, string Document, DateOnly JoinDate);
public record MemberFlowLeave(string FullName, string Plan, DateOnly CancelDate, string? Reason);
public record MemberFlow(
    IReadOnlyDictionary<MemberStatus,int> StatusCounts,   // foto de hoy
    int ActiveNow,                                         // StatusCounts[Active]
    IReadOnlyList<MemberFlowJoin> Joins,
    IReadOnlyList<MemberFlowLeave> Leaves,
    int Net);                                              // Joins.Count - Leaves.Count
public record GetMemberFlowQuery(Guid CompanyId, Guid? SiteId, DateOnly From, DateOnly To)
    : IRequest<MemberFlow>;
```

Handler: `CountByStatusAsync` → `StatusCounts`/`ActiveNow`; `GetJoinedBetweenAsync` → `Joins` (ordenar por
`JoinDate`); `GetCancelledBetweenAsync` → `Leaves` (ordenar por `CancelDate`; `Plan` = `MembershipType.Name`,
`CancelDate` desde `DateTime` → `DateOnly`); `Net = Joins.Count − Leaves.Count`.

### Tests (TDD)
`tests/GymForge.Application.Tests/Handlers/GetMemberFlowQueryTests.cs` (fakes):
- Alta contada solo si `JoinDate ∈ [from, to]` (borde inclusivo en ambos extremos).
- Baja contada solo si `CancelDate ∈ [from, to]`; trae plan y motivo.
- `StatusCounts` refleja la foto de hoy (no depende del rango); `Net` = altas − bajas.
Integración: `GetJoinedBetweenAsync`/`GetCancelledBetweenAsync`/`CountByStatusAsync` con `siteId` filtran una
sede; con `siteId = null` toman todas las de la empresa y **ninguna de otra** (tenant adversarial).

---

## Pieza F — Hub de Reportes (Desktop)

### Diseño
Convertir `ReportsView`/`ReportsViewModel` en un **hub**: selector de reporte + el contenido del reporte
elegido + "Exportar ▾" (PDF / Excel) de ese reporte. Reutiliza el flujo de fechas y la grilla actuales.

- `ReportsViewModel`: `enum ReportKind { Payments, RevenueBreakdown, Overdue, MemberFlow }` + `SelectedReport`;
  `From`/`To` (ya existen), toggle `CompanyWide` (para desagregada y altas/bajas), y por reporte su carga
  (reutiliza las queries). Mantiene `ReprintReceiptCommand` en el reporte de pagos.
- `ExportCommand(ReportFormat format)`: arma el `ReportDocument` del reporte activo con un **builder puro**
  (ver abajo) y llama `ReportExportService.ExportAsync(doc, format)`. Deshabilitado mientras `IsLoading` o sin
  datos.
- **Builders puros de documento** en Application (`src/GymForge.Application/UseCases/Reports/ReportBuilders.cs`),
  uno por reporte: `(datos del query + ReportBranding) -> ReportDocument`. Son los que arman columnas/tipos,
  totales y el `%` de la desagregación. **Testeables sin UI.** La marca (`ReportBranding`) la compone el VM
  desde los mismos datos que usa el recibo (nombre, CUIT, sede, logo, color de acento del `SessionContext`/
  empresa); `dev` reutiliza esa fuente, no la duplica.
- `ReportsView.axaml`: agregar el selector (ComboBox o lista) y el `SplitButton`/menú "Exportar" (PDF/Excel).
  El layout fino y los estados (vacío, cargando, toggle de alcance) los define **`ux`**; esta pieza fija la
  estructura del VM y los comandos, no el diseño visual.

### Tests (TDD)
`tests/GymForge.Desktop.Tests/ReportsViewModelTests.cs` (ampliar):
- Cambiar `SelectedReport` dispara la carga correcta.
- `ExportCommand(Pdf/Xlsx)` arma el documento del reporte activo y llama a `ReportExportService`/`IReportExporter`
  (fakes) con el formato pedido; deshabilitado sin datos.
- Builders: `tests/GymForge.Application.Tests/Reports/ReportBuildersTests.cs` — Morosos arma columnas/totales;
  desagregada calcula `%` por fila y suma 100% por corte; altas/bajas arma las 3 tablas.

---

## Orden de implementación (TDD)

Primero lo puro y de bajo riesgo; la UI al final. Un commit por paso (regla 7), `dotnet test` verde antes de
cada uno. **Sin migraciones EF** en toda la sesión.

1. **Pieza B — export genérico.** Modelo `ReportDocument` + `IReportExporter`; extraer helpers de marca/moneda
   de `ReceiptPdfGenerator`; `ReportPdfGenerator` + `ReportXlsxGenerator` (**agregar `ClosedXML`**) +
   `ReportExporter`; tests de round-trip XLSX y PDF. Registrar en DI.
   `feat: [reportes] exportador PDF/Excel generico (ClosedXML)`
2. **Pieza C + Morosos (A.3).** `ReportExportService` (diálogo) en Desktop + builder de Morosos
   (`OverdueMemberDto` → `ReportDocument`) reutilizando `GetOverdueMembersQuery`; test del builder.
   `feat: [reportes] exportar morosos a PDF/Excel`
3. **Pieza D — recaudación desagregada.** `GetForBreakdownAsync` + `GetRevenueBreakdownQuery` + builder (con
   `%` y reconciliación) + tests (incluida la invariante de reconciliación y el caso tenant).
   `feat: [reportes] recaudacion desagregada por metodo, concepto y sede`
4. **Pieza E — altas y bajas.** Repos de lectura (`CountByStatusAsync`, `GetJoinedBetweenAsync`,
   `GetCancelledBetweenAsync`) + `GetMemberFlowQuery` + builder + tests (bordes de fecha, tenant).
   `feat: [reportes] altas y bajas del periodo`
5. **Pieza F — hub de Reportes.** `ReportsViewModel` (selector + export + toggle de alcance) + detalle de
   pagos exportable (A.1) + `ReportsView` (estructura; `ux` pule después) + tests de VM.
   `feat: [reportes] selector de reportes y exportacion en Reportes`

> `ux` entra en el paso 5 (layout del selector, "Exportar ▾", toggle de alcance, estados vacío/cargando y la
> nota del límite de "altas y bajas"). Verificación de runtime (`qa`): abrir un `.xlsx` y un `.pdf` reales en
> Windows, el diálogo de guardado, y que `scripts/publish.ps1` single-file siga generando el Excel (ClosedXML
> sin nativas). Checks visuales con `tools/GymForge.Screenshots` en light y dark.

## Piezas que tocan UI (entra `ux`)
- **Hub de Reportes (F):** selector de reporte, "Exportar ▾" (PDF/Excel), toggle "esta sede / toda la
  empresa", estados y la nota de límite de altas/bajas. El resto de las piezas no tiene UI.

## Decisiones a confirmar con el usuario
1. **Excel = ClosedXML (sumar dependencia) vs. CSV plano (cero deps).** Recomendado **ClosedXML** (multi-hoja,
   celdas numéricas, es-AR sin romper columnas, MIT, sin nativas). Es el único cambio al stack congelado y
   **requiere tu OK**. Si lo rechazás, se entrega PDF + CSV mínimo (una tabla por archivo). EPPlus descartado
   por licencia.
2. **Guardado por diálogo nativo (recomendado) vs. carpeta fija** `%LOCALAPPDATA%\GymForge\reportes\AAAA-MM\`.
   Recomendado el diálogo (el dueño se lleva el archivo). Cambiarlo es trivial.
3. **"Por concepto/plan":** ¿alcanza el corte propuesto (plan por nombre + concepto para el resto + bucket "A
   cuenta" + "Productos (venta directa)" para POS), o querés el detalle de líneas de producto del POS? (lo
   segundo, fuera de esta parte).
4. **"Altas y bajas":** confirmar que para v1 va **sin % de retención cohort** (no hay histórico temporal) y
   que **bajas = cancelaciones de membresía** (`CancelDate`), sin "vencidas sin renovar" (churn por
   vencimiento). Si querés churn por vencimiento, se suma como cuarta métrica (más trabajo, posible 2.ª parte).
5. **Alcance "toda la empresa" (cruza sedes):** es un reporte de dueño. ¿Lo puede ver cualquier recepción o se
   limita a admin? Hoy la UI no tiene roles finos; si hace falta gatearlo, decidilo acá (si no, queda visible
   para quien tenga sesión en la empresa, siempre dentro de la misma empresa).
6. **PII en el archivo exportado (ligado a D11, Ley 25.326):** el PDF/Excel lleva nombre, celular y deuda. Es
   un archivo **local** que vos elegís guardar; no se transmite a ningún lado. ¿Alcanza con eso o querés algún
   recaudo extra (p. ej. aviso al exportar)?

---

## Consecuencias
- **Stack:** una dependencia nueva, `ClosedXML` (MIT, manejada), en `GymForge.Infrastructure`. QuestPDF ya
  estaba. Es la modificación al stack que este ADR autoriza (CLAUDE.md: "NO modificar sin ADR").
- **Sin migración EF**: las tres familias leen el esquema actual. Menos riesgo, nada que versionar.
- `ReceiptPdfGenerator` cede sus helpers de marca/moneda/logo a un helper compartido (refactor sin cambio de
  salida; sus tests siguen verdes).
- `IPaymentRepository`, `IMemberRepository`, `IMembershipRepository` suman métodos de **solo lectura**
  (ninguno muta). Reportes "por sede"/"toda la empresa" usan `IgnoreQueryFilters()` + `CompanyId` explícito:
  patrón ya establecido (`GetOverdueAsync`), acotado a una empresa.
- `GymForge.Desktop` suma un `ReportExportService` (diálogo nativo + abrir archivo, `Process.Start` ya en uso).
- Los archivos exportados contienen PII (nombres, celulares, deudas). Son locales y elegidos por el usuario;
  se cruza con la decisión abierta D11 (política de datos personales), sin bloquearla.

## Fuera de alcance (deuda explícita → `docs/TASKS.md` como F4-D*)
- **% de retención / churn cohort real**: necesita una tabla de snapshots diarios de membresías (histórico
  temporal) que hoy no existe. Fase 5+.
- **Churn por vencimiento** ("vencidas sin renovar") en altas/bajas: v1 solo cuenta cancelaciones explícitas.
- **Detalle de líneas de producto del POS** en la desagregación: v1 agrupa POS como "Productos (venta
  directa)".
- **Reportes de efectividad de cobranza** (recuperado por etapa/canal con `NotificationLog`): mencionado como
  Fase 4 en ADR-005; no entra en esta parte.
- **Comisiones de staff, editar socio desde la ficha, rutinas en la ficha**: Fase 4 parte 2 (roadmap).
- **Agendado/envío automático de reportes** (por mail, programado): no hay mailer; fuera de offline-first.
- **CSV** como formato: solo si el usuario rechaza ClosedXML (decisión 1).

---

## Referencias
- Reporte existente: `src/GymForge.Application/UseCases/Reports/PaymentsReportQuery.cs` ·
  `src/GymForge.Desktop/ViewModels/Reports/ReportsViewModel.cs` · `src/GymForge.Desktop/Views/Reports/ReportsView.axaml`
- Morosos (Fase 2): `src/GymForge.Application/UseCases/Dunning/GetOverdueMembersQuery.cs` ·
  `IChargeRepository.GetOverdueBySiteAsync`
- Molde PDF + guardado: `src/GymForge.Infrastructure/Services/ReceiptPdfGenerator.cs` ·
  `src/GymForge.Application/Interfaces/IReceiptPdfWriter.cs` · `src/GymForge.Desktop/Services/ReceiptService.cs`
- Diálogo nativo de guardado: `src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs:479`
  (`SaveFilePickerAsync`)
- Dominio de altas/bajas: `src/GymForge.Domain/Entities/Member.cs` (`JoinDate`, `Status`, `Activate`,
  `Cancel`) · `src/GymForge.Domain/Entities/Membership.cs` (`CancelDate`, `CancelReason`, `Cancel`) ·
  `src/GymForge.Domain/Enums/MemberStatus.cs`
- Grafo de recaudación: `src/GymForge.Domain/Entities/Payment.cs` · `.../Charge.cs` (`ConceptType`) ·
  `.../MembershipType.cs` (`Name`) · `src/GymForge.Domain/Enums/ChargeStatus.cs` (`ConceptType`)
- Repos/interfaces: `src/GymForge.Application/Interfaces/IRepositories.cs` ·
  `src/GymForge.Infrastructure/Repositories/PaymentRepository.cs`
- Patrón tenant company-wide: `GetOverdueAsync` en `src/GymForge.Infrastructure/Repositories/ChargeRepository.cs`
- Roadmap/estado: `docs/PROGRESS.md` · `docs/TASKS.md` · `docs/adr/README.md`
