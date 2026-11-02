# ADR-005 — WhatsApp gratis + cobranzas (Fase 2)

**Status:** Accepted
**Date:** 2026-10-29
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` · veta `qa`
**Sesión:** 4 — Fase 2 del roadmap (`docs/PROGRESS.md`)

---

## Context

Fase 2 "WhatsApp gratis + cobranzas": darle al gimnasio una forma concreta de cobrar la mora sin pagar
una API de mensajería. Son **cuatro piezas**:

1. **Pantalla de morosos** (nueva sección del sidebar): lista de socios con cargos vencidos de la sede
   activa. Muestra quién debe, cuánto, desde cuándo y cuántos días; permite disparar el recordatorio por
   WhatsApp y cobrar.
2. **`WhatsAppLinkSender`** (enfoque GRATIS): arma un link `wa.me` con el mensaje prearmado y lo abre en
   WhatsApp Desktop/Web. **No** es la API paga (Twilio/Wassenger), que queda descartada para esta fase.
3. **`DunningConfig` editable** en Configuración: activar/desactivar el cobro automático, editar las etapas
   (días de vencido) y las plantillas de mensaje con placeholders `{nombre}` / `{monto}` / `{vencimiento}`.
4. **`NotificationLog`**: registro **append-only** y multi-tenant de los recordatorios enviados (a quién,
   cuándo, por qué etapa, qué canal).

Esta es la guía de implementación para `dev` en **TDD**: no inventar arquitectura, seguir las firmas y el
orden de abajo. Alcance acotado a estas 4 piezas; nada más entra en Fase 2.

Reglas del proyecto que condicionan el diseño (CLAUDE.md): **offline-first** (todo persiste en SQLite antes
que nada; `wa.me` solo abre una app externa, nunca es dependencia de cloud), **multi-tenant** por
`CompanyId` + `SiteId`, **AccessLog/AuditLog append-only** (y por extensión todo log nuevo), `dotnet test`
verde antes de cada commit (regla 6). Piso actual declarado por el equipo: **646 tests verdes**.

### Hechos del código verificados al 2026-10-29

Ya hay mucha cobranza hecha en la Sesión 2. **Se reutiliza, no se duplica.**

- **`DunningService`** (`src/GymForge.Application/UseCases/Dunning/DunningService.cs`): recorre los cargos
  vencidos de la empresa y, para los que cumplen **exactamente** una etapa a la fecha (`daysOverdue ==
  stage.DaysOverdue`), arma el mensaje y lo envía por `INotificationSender`. Idempotente por "día exacto".
  Depende de `DunningConfig` (inyectado). Busca cada socio en la sede de su cargo con `_tenant.BeginScope`.
- **`DunningConfig`** (`.../Dunning/DunningConfig.cs`): hoy es un **singleton de DI con defaults
  hardcodeados** (`Enabled=false`; etapas 1/3/7/15/30 con un `Tone` string). **No es editable** ni se
  persiste. Registrado en `InfrastructureServiceExtensions.cs:75` como singleton.
- **`DunningTemplates`** (`.../Dunning/DunningTemplates.cs`): `static Build(stage, firstName, amount,
  dueDate, gymName)` con un `switch` por `Tone` y 5 strings interpolados en es-AR. Los textos están
  **hardcodeados**.
- **El job** (`src/GymForge.Application/UseCases/Maintenance/DailyMaintenanceService.cs` +
  `DailyMaintenanceRunner.cs`): `DailyMaintenanceService.RunForCompanyDayAsync` corre, en orden,
  unfreeze → vencer → marcar mora → **`_dunning.RunAsync`** → limpiar mora saldada, bajo
  `_tenant.BeginScope(company.Id, Guid.Empty)`. El `Runner` hace **catch-up** de hasta 35 días desde un
  checkpoint en archivo, al arrancar y cada 6 h (`DailyMaintenanceHost`). **Límite conocido** (documentado
  en `docs/NOTIFICATIONS-DUNNING.md`): si el checkpoint obliga a repetir un día, los avisos de ese día se
  **reenvían**; el paso siguiente previsto era, justamente, un `NotificationLog` para no duplicar.
- **`INotificationSender`** (`src/GymForge.Application/Interfaces/INotificationSender.cs`): abstracción de
  envío (`NotificationMessage` = `ToPhone` + `Body` + `NotificationChannel`). El enum
  `NotificationChannel { WhatsApp, Sms }` vive **acá**, en `Application.Interfaces`. La única implementación
  es **`LogNotificationSender`** (`Infrastructure/Services`): **registra el mensaje en el log, no lo envía.**
  No hay proveedor real: hoy el job "manda" a un log.
- **Mora.** `ChargeRepository.GetOverdueAsync(companyId, asOf)` (`src/GymForge.Infrastructure/Repositories/
  ChargeRepository.cs:50`) es **company-wide** a propósito: `IgnoreQueryFilters()` + `CompanyId` explícito,
  excluye `Paid/WrittenOff/Refunded`, incluye `Member`. Es la **deuda D1**: la lista es de todas las sedes,
  pero el cobro exige sede + turno de caja abiertos. Para la **pantalla de morosos la lista debe ser por
  sede**. `Charge.IsInArrears(asOf)` (`src/GymForge.Domain/Entities/Charge.cs:84`) es la definición única de
  mora (fecha + saldo). `Charge.AmountOutstanding`, `DueDate`, `Status`.
- **Teléfono.** `Member.Mobile` es un **`string?` crudo** (no el value object). `PhoneNumber`
  (`src/GymForge.Domain/ValueObjects/PhoneNumber.cs`) solo normaliza espacios/guiones/paréntesis; **no**
  produce E.164 ni sabe de Argentina. El `DunningService` ya saltea socios sin `Mobile`.
- **Patrón de configuración por empresa (el molde de `DunningConfig`).** `GatekeeperConfig`
  (`src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs:26`, propiedades con `set`) es un
  **singleton** que se **hidrata al arrancar** en `SessionContext.InitializeAsync`
  (`src/GymForge.Desktop/Services/SessionContext.cs:122`) desde `AccessSettings.FromJson(company.SettingsJson)`
  y se **aplica en caliente** al guardar (`UpdateAccessSettingsCommandHandler`,
  `src/GymForge.Application/UseCases/Settings/SettingsCommands.cs:202`). `CurrentLicense`
  (`LicenseModels.cs:62`) es el mismo patrón. `Company` ya tiene **varias columnas JSON**
  (`FiscalConfigJson`, `SettingsJson`, `LicenseKey`; `src/GymForge.Domain/Entities/Company.cs:13-17`) con
  setters de dominio (`SetSettings`, `SetFiscalConfig`).
- **Patrón de log append-only (el molde de `NotificationLog`).** `AccessLog`
  (`src/GymForge.Domain/Entities/AccessLog.cs`) y `MemberCreditMovement`
  (`src/GymForge.Domain/Entities/MemberCreditMovement.cs`) son `BaseEntity, ISiteScoped`, con
  **constructor privado + factory estático**, **sin** `Update`/`Remove` en su repo dedicado
  (`IAccessLogRepository`, `IMemberCreditRepository`), y **no** implementan `IAuditable` ("el log es su
  propia auditoría"). El `AuditSaveChangesInterceptor` nunca audita logs. Config EF: `HasKey`, enums a
  string, índices por `(…, fecha)`, `ToTable`.
- **Navegación.** `NavSection` enum + router en `MainWindowViewModel`
  (`src/GymForge.Desktop/Views/Shell/MainWindowViewModel.cs:17` y `Navigate` L113). Agregar una sección
  nueva = (a) valor en `NavSection`, (b) botón `NavButton` en `MainWindow.axaml` (grupos OPERACIÓN/GESTIÓN),
  (c) rama en el `switch` de `Navigate`, (d) registro del VM en `ServiceCollectionExtensions.cs`, (e)
  `DataTemplate` VM→View en `App.axaml` (L40-95). El cobro reutiliza `PaymentModalViewModel`
  (`src/GymForge.Desktop/ViewModels/Charges/PaymentModalViewModel.cs`), que exige turno de caja abierto.

---

## Decisión — resumen

| # | Pieza | Decisión clave | Dónde vive | UI | Tamaño |
|---|---|---|---|---|---|
| A | Plantillas editables | Mover el texto del mensaje a la etapa (`Template` con placeholders) + renderer puro compartido por el job y el link | `Application/UseCases/Dunning` | No | S |
| B | `NotificationLog` | Entidad append-only `BaseEntity, ISiteScoped` + repo dedicado (sin Update/Remove), igual que `AccessLog` | Domain + Infra, tabla nueva | No | M |
| C | `DunningConfig` persistido | **Columna JSON nueva `Company.DunningJson`** (no `SettingsJson`, no `ILocalSettings`, no tabla propia). Singleton hidratado al arrancar + aplicado en caliente, igual que `GatekeeperConfig` | Domain + Infra + Desktop | Sí | M |
| D | Integración con el job | `DunningService` usa la config editable + el renderer, **escribe `NotificationLog`** y **deduplica** por `(cargo, etapa)`. No hay job nuevo | `Application/UseCases/Dunning` | No | S |
| E | Link `wa.me` + envío manual | `WhatsAppLink.Build` puro (Application) + `IWhatsAppLinkSender` (Desktop, `Process.Start`) + `SendWhatsAppReminderCommand` que registra el `NotificationLog` y devuelve la URL | Application + Desktop | Sí | M |
| F | Pantalla de morosos | `NavSection.Collections` + `GetOverdueMembersQuery` **por sede** + `CollectionsViewModel`/`CollectionsView`; cobra reutilizando `PaymentModal` | Application + Desktop | Sí | M |

### Decisiones transversales (leer antes que el detalle)

- **`DunningConfig.Enabled` gobierna SOLO el job automático.** El recordatorio manual por `wa.me` (que el
  operador dispara a mano desde la pantalla de morosos) funciona **siempre**, con dunning encendido o
  apagado. Son dos caminos distintos que **comparten** plantillas, renderer y `NotificationLog`.
- **El canal gratis real de Fase 2 es el MANUAL (`wa.me`).** No existe WhatsApp automático gratis: `wa.me`
  necesita que un humano abra la app y toque "enviar". Por eso el **job automático sigue siendo log-only**
  (via `LogNotificationSender`) en esta fase: registra lo que mandaría, pero el envío real lo hace el
  operador desde la pantalla de morosos. El envío automático real queda explícitamente **fuera de fase**.
- **`DunningConfig` viaja con los datos** (es config de negocio por empresa) → va en la DB (`Company`),
  **no** en `ILocalSettings` (que es de la máquina y no viaja; ver ADR-004). Contraste deliberado con el
  destino de backup off-site.
- **La pantalla de morosos es por sede** (resuelve el síntoma de la deuda D1 en la UI): cada sede ve y
  cobra su propia mora. El job automático sigue siendo company-wide. No se arregla D1 de fondo acá.

---

## Pieza A — Plantillas de recordatorio editables (renderer compartido)

### Qué falta
El texto vive hardcodeado en `DunningTemplates` por `Tone`. Hay que (1) poder editarlo y (2) **compartir**
el mismo mensaje entre el job automático y el link `wa.me`, para que el socio reciba lo mismo por los dos
caminos.

### Diseño

Cambiar la etapa para que lleve la plantilla editable:

```csharp
// .../Dunning/DunningConfig.cs
public record DunningStage(int DaysOverdue, string Label, string Template);

public class DunningConfig
{
    public bool Enabled { get; set; }
    public IReadOnlyList<DunningStage> Stages { get; set; } = DefaultStages;

    // Los 5 textos actuales de DunningTemplates, con placeholders en vez de interpolación.
    public static readonly IReadOnlyList<DunningStage> DefaultStages =
    [
        new(1,  "Aviso amable",
            "¡Hola {nombre}! Te recordamos que tu cuota de {gimnasio} venció el {vencimiento} y quedó un saldo de {monto}. Cuando puedas, acercate a abonarla. ¡Gracias!"),
        new(3,  "Recordatorio",
            "Hola {nombre}, seguimos sin registrar el pago de tu cuota de {gimnasio} (venció el {vencimiento}, saldo {monto}). Te esperamos para regularizarla."),
        new(7,  "Firme",
            "Hola {nombre}, tu cuota de {gimnasio} está vencida hace {dias} días (saldo {monto}). Para seguir ingresando necesitás ponerte al día."),
        new(15, "Urgente",
            "{nombre}, tu deuda con {gimnasio} lleva {dias} días (saldo {monto}, venció el {vencimiento}). Regularizá tu situación para no perder el acceso."),
        new(30, "Último aviso",
            "{nombre}, último aviso: tu cuota de {gimnasio} está vencida hace {dias} días (saldo {monto}). Comunicate con recepción para no dar de baja tu membresía."),
    ];
}
```

Renderer **puro y testeable** (reemplaza `DunningTemplates.Build`):

```csharp
// .../Dunning/DunningMessageRenderer.cs
public readonly record struct DunningContext(
    string FirstName, string GymName, decimal Outstanding, DateOnly DueDate, int DaysOverdue);

public static class DunningMessageRenderer
{
    // es-AR: {monto} => "$35.000", {vencimiento} => "dd/MM/yyyy". Placeholder desconocido: se deja literal.
    public static string Render(string template, DunningContext ctx);
}
```

Placeholders soportados (documentar en la UI de Config): `{nombre}`, `{gimnasio}`, `{monto}`,
`{vencimiento}`, `{dias}`. `DunningService.RunAsync` pasa a usar `DunningMessageRenderer.Render(stage.Template,
ctx)` en lugar de `DunningTemplates.Build(...)`. `DunningTemplates.cs` se **elimina** (su contenido migró a
`DefaultStages` + el renderer).

### Decisiones
- **Un solo set de plantillas** (las de dunning) para los dos caminos. No hay plantillas separadas para el
  link manual: el link usa la etapa que corresponde a la antigüedad de la deuda del socio (ver Pieza E).
- **Placeholder desconocido → se deja literal** (no se rompe el mensaje por un typo del gimnasio).

### Tests (TDD)
`tests/GymForge.Application.Tests/Dunning/DunningMessageRendererTests.cs` (nuevo):
- `Render_SustituyeTodosLosPlaceholders` (nombre/gimnasio/monto/vencimiento/dias).
- `Render_FormateaMontoYFechaEnEsAr` ("$35.000", "21/07/2026").
- `Render_PlaceholderDesconocido_QuedaLiteral`.
- Adaptar `DunningServiceTests` (ya existe): el body sigue conteniendo el nombre del gimnasio y el monto;
  cambia solo la fuente del texto, no el comportamiento del envío. **Todos los casos actuales siguen verdes.**

---

## Pieza B — `NotificationLog` (append-only, multi-tenant)

### Qué falta
No hay registro de qué recordatorio se envió a cada socio/cargo. Se necesita para (1) mostrar "último
recordatorio" en la pantalla de morosos, (2) **deduplicar** los envíos del job (límite conocido del
catch-up) y (3) dejar traza de los envíos manuales.

### Diseño

**Entidad** (molde exacto: `AccessLog` / `MemberCreditMovement`).

```csharp
// src/GymForge.Domain/Entities/NotificationLog.cs
/// <summary>Append-only. Never update or delete rows. Registra cada recordatorio de cobranza enviado.</summary>
public class NotificationLog : BaseEntity, ISiteScoped
{
    public Guid MemberId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }
    /// <summary>Cargo que disparó el aviso. Null = aviso a nivel socio (sin un cargo puntual).</summary>
    public Guid? ChargeId { get; private set; }
    public NotificationChannel Channel { get; private set; }     // Domain.Enums (ver abajo)
    public NotificationTrigger Trigger { get; private set; }     // Manual | Automatic
    /// <summary>Etapa de dunning (días de vencido) que generó el aviso. Null = manual sin etapa mapeada.</summary>
    public int? StageDaysOverdue { get; private set; }
    /// <summary>Teléfono al que se dirigió (snapshot: el Mobile del socio puede cambiar).</summary>
    public string? ToPhone { get; private set; }
    public DateTime SentAt { get; private set; }

    public Member? Member { get; private set; }

    private NotificationLog() { }

    public static NotificationLog Manual(
        Guid companyId, Guid siteId, Guid memberId, Guid? chargeId,
        NotificationChannel channel, int? stageDaysOverdue, string? toPhone) => new()
    {
        CompanyId = companyId, SiteId = siteId, MemberId = memberId, ChargeId = chargeId,
        Channel = channel, Trigger = NotificationTrigger.Manual,
        StageDaysOverdue = stageDaysOverdue, ToPhone = toPhone, SentAt = DateTime.UtcNow
    };

    public static NotificationLog Automatic(
        Guid companyId, Guid siteId, Guid memberId, Guid chargeId,
        NotificationChannel channel, int stageDaysOverdue, string? toPhone) => new()
    {
        CompanyId = companyId, SiteId = siteId, MemberId = memberId, ChargeId = chargeId,
        Channel = channel, Trigger = NotificationTrigger.Automatic,
        StageDaysOverdue = stageDaysOverdue, ToPhone = toPhone, SentAt = DateTime.UtcNow
    };
}
```

- **NO** implementa `IAuditable` (como `AccessLog`/`MemberCreditMovement`). **NO** se guarda el cuerpo del
  mensaje: es reconstruible desde la plantilla y evita duplicar datos personales. Se guarda el teléfono
  destino como snapshot para la traza.

**Enums** en `src/GymForge.Domain/Enums/` (Domain no puede depender de Application):

```csharp
public enum NotificationChannel { WhatsApp, Sms }      // canónico, en Domain.Enums
public enum NotificationTrigger { Manual, Automatic }
```

> **Refactor chico:** hoy `NotificationChannel { WhatsApp, Sms }` vive en
> `src/GymForge.Application/Interfaces/INotificationSender.cs`. Moverlo a `Domain.Enums` y que
> `NotificationMessage` referencie el de Domain (Application ya depende de Domain). Elimina el tipo
> duplicado. Verificar que `LogNotificationSender` y los tests sigan compilando.

**EF config** (en un `NotificationLogConfiguration` nuevo o junto a los de charges):

```csharp
builder.HasKey(n => n.Id);
builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20);
builder.Property(n => n.Trigger).HasConversion<string>().HasMaxLength(20);
builder.Property(n => n.ToPhone).HasMaxLength(30);
builder.HasIndex(n => new { n.MemberId, n.SentAt }).HasDatabaseName("IX_NotificationLogs_MemberSentAt");
builder.HasIndex(n => new { n.CompanyId, n.ChargeId, n.StageDaysOverdue })
       .HasDatabaseName("IX_NotificationLogs_Dedup");   // soporta WasSentAsync
builder.HasOne(n => n.Member).WithMany().HasForeignKey(n => n.MemberId)
       .OnDelete(DeleteBehavior.Restrict);              // el log sobrevive al socio
builder.ToTable("NotificationLogs");
```

`DbSet<NotificationLog> NotificationLogs` en `GymForgeDbContext`. **Migración** `dotnet ef migrations add
AddNotificationLog` (ver comando en CLAUDE.md). La entidad es `ISiteScoped` → el filtro global de tenant la
cubre automáticamente en lecturas normales.

**Repositorio dedicado** (sin `Update`/`Remove`, igual que `IAccessLogRepository`):

```csharp
// src/GymForge.Application/Interfaces/IRepositories.cs
public interface INotificationLogRepository
{
    Task AppendAsync(NotificationLog log, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Avisos del socio, del más reciente al más antiguo (pantalla de morosos). Filtrado por tenant.</summary>
    Task<IReadOnlyList<NotificationLog>> GetByMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default);

    /// <summary>Último aviso al socio (para "último recordatorio: hace N días"). Filtrado por tenant.</summary>
    Task<NotificationLog?> GetLastForMemberAsync(Guid memberId, CancellationToken ct = default);

    /// <summary>
    /// Dedup del job: ¿ya se envió ESTA etapa para ESTE cargo? Company-wide a propósito
    /// (el job corre con SiteId = Guid.Empty): IgnoreQueryFilters() + CompanyId explícito, igual que
    /// ChargeRepository.GetOverdueAsync.
    /// </summary>
    Task<bool> WasSentAsync(Guid companyId, Guid chargeId, int stageDaysOverdue, CancellationToken ct = default);
}
```

Registrar `services.AddScoped<INotificationLogRepository, NotificationLogRepository>();` en
`InfrastructureServiceExtensions`.

### Decisiones
- **`ISiteScoped` (CompanyId + SiteId)**, como `AccessLog`. El aviso se ata a la sede del cargo/socio. Las
  lecturas de la UI (por sede) usan el filtro global; la lectura de dedup del job usa `IgnoreQueryFilters`
  + `CompanyId` explícito (patrón ya establecido en `GetOverdueAsync`).
- **Append-only por contrato**: el repo no expone `Update`/`Remove`. Nunca se edita ni se borra una fila.

### Tests (TDD)
`tests/GymForge.Integration.Tests/NotificationLogRepositoryTests.cs` (nuevo):
- `Append_YLuego_GetByMember_DevuelveElAviso`.
- `GetLastForMember_DevuelveElMasReciente`.
- `WasSentAsync_True_SoloParaMismoCargoYEtapa` (distinta etapa / distinto cargo → false).
- `WasSentAsync_EsCompanyWide_IgnoraSedeDelScope` (escribir con una sede, consultar con el scope en otra →
  sigue true).
- Aislamiento multi-tenant: un aviso de la empresa A no aparece al leer como empresa B
  (sumar un caso en `TenantAdversarialIntegrationTests` si encaja).

---

## Pieza C — `DunningConfig` persistido y editable

### Qué falta
`DunningConfig` es un singleton con defaults en código; no hay forma de que el gimnasio lo edite ni de que
sobreviva a un reinicio. Hay que persistirlo por empresa y exponerlo en Configuración.

### Diseño (patrón `GatekeeperConfig` / `AccessSettings`, idéntico)

**1. Columna de persistencia: `Company.DunningJson`** (nueva, nullable TEXT). **No** se mete en
`SettingsJson` (hoy guarda solo `AccessSettings` como objeto entero; combinarlos acopla y arriesga los datos
ya persistidos). `Company` ya tiene varias columnas JSON; una más es lo consistente.

```csharp
// src/GymForge.Domain/Entities/Company.cs
public string? DunningJson { get; private set; }
public void SetDunningConfig(string dunningJson) { DunningJson = dunningJson; UpdatedAt = DateTime.UtcNow; }
```

Migración `dotnet ef migrations add AddCompanyDunningConfig` (solo agrega la columna; nullable, sin default).

**2. Record serializable** (molde `AccessSettings`):

```csharp
// .../Dunning/DunningSettings.cs
public record DunningStageSettings(int DaysOverdue, string Label, string Template);

public record DunningSettings(bool Enabled, IReadOnlyList<DunningStageSettings> Stages)
{
    public static DunningSettings? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<DunningSettings>(json); }
        catch (JsonException) { return null; }   // corrupto → defaults (como AccessSettings)
    }
    public static DunningSettings Default() =>
        new(false, DunningConfig.DefaultStages.Select(s => new DunningStageSettings(s.DaysOverdue, s.Label, s.Template)).ToList());
}
```

**3. Hidratación al arrancar** en `SessionContext.InitializeAsync` (junto al bloque de `AccessSettings`):

```csharp
if (DunningSettings.FromJson(company.DunningJson) is { } dunning)
{
    _dunning.Enabled = dunning.Enabled;
    _dunning.Stages  = dunning.Stages.Select(s => new DunningStage(s.DaysOverdue, s.Label, s.Template)).ToList();
}
```

(`SessionContext` recibe el singleton `DunningConfig _dunning` por constructor, como ya recibe
`GatekeeperConfig`.)

**4. Comando de guardado** (molde `UpdateAccessSettingsCommand`): persiste JSON + **aplica en caliente** al
singleton.

```csharp
// .../Dunning/UpdateDunningSettingsCommand.cs
public record DunningStageInput(int DaysOverdue, string Label, string Template);
public record UpdateDunningSettingsCommand(Guid CompanyId, bool Enabled, IReadOnlyList<DunningStageInput> Stages)
    : IRequest;
// Validator: DaysOverdue >= 0 y sin repetir; Template NotEmpty, MaximumLength(1000); Label NotEmpty.
// Handler: company.SetDunningConfig(JsonSerializer.Serialize(new DunningSettings(...))); SaveChanges();
//          luego _config.Enabled = ...; _config.Stages = ...;  (mismo _config que inyecta el job)
```

### Decisiones
- **Vive en `Company.DunningJson`** (DB, por empresa, viaja con el export). Justificación arriba
  ("decisiones transversales"). Descartado: `SettingsJson` (acople/riesgo), `ILocalSettings` (no viaja y es
  config de negocio, no de máquina), tabla propia (es un documento chico por empresa, igual que
  `AccessSettings`).
- **`Enabled` apaga solo el job.** El guardado lo refleja en el singleton en caliente; el próximo ciclo del
  job lo respeta. El recordatorio manual ignora `Enabled`.
- **UI v1: editar los 5 escalones y el toggle.** Agregar/quitar etapas es *nice-to-have*; para Fase 2 basta
  editar `Label` + `DaysOverdue` + `Template` de las etapas existentes y el `Enabled`. Si resulta trivial,
  permitir agregar/quitar; si no, queda para después.

### Tests (TDD)
- `tests/GymForge.Application.Tests/Dunning/DunningSettingsTests.cs`: round-trip `ToJson`/`FromJson`; `null`
  → `null` (cae a `Default()` en el caller); JSON corrupto → `null` sin lanzar; `Default()` trae 5 etapas y
  `Enabled=false`.
- `tests/GymForge.Application.Tests/Handlers/UpdateDunningSettingsCommandTests.cs`: persiste el JSON en la
  empresa **y** actualiza el singleton `DunningConfig` (verificar ambas cosas, con fake repo + instancia
  real de `DunningConfig`); validator rechaza días repetidos / template vacío.
- Integración: `SessionContext`/arranque hidrata el singleton desde `DunningJson` (puede cubrirse en un test
  de wiring si ya existe el patrón; si no, test del bloque de hidratación aislado).

---

## Pieza D — Integración con el `DunningService` y el job (reutilizar, no duplicar)

### Qué falta
Que el job (1) use la config editable (ya resuelto por C: es el mismo singleton), (2) deje traza en
`NotificationLog` y (3) **no reenvíe** un aviso ya enviado cuando el catch-up repite un día.

### Diseño
- **No hay job nuevo.** Se mantiene `DailyMaintenanceService.RunForCompanyDayAsync` →
  `_dunning.RunAsync(...)` tal cual.
- `DunningService` recibe por constructor `INotificationLogRepository _log`. En el loop de `RunAsync`, para
  cada cargo que cumple una etapa:
  1. **Dedup:** `if (await _log.WasSentAsync(charge.CompanyId, charge.Id, stage.DaysOverdue, ct)) continue;`
  2. Render + `_sender.SendAsync(...)` (hoy = log).
  3. Si el sender aceptó: `await _log.AppendAsync(NotificationLog.Automatic(charge.CompanyId, charge.SiteId,
     charge.MemberId, charge.Id, NotificationChannel.WhatsApp, stage.DaysOverdue, member.Mobile), ct);` +
     `SaveChangesAsync`.
- Se **conserva** el disparo por "día exacto" (`daysOverdue == stage.DaysOverdue`) como gatillo; el dedup es
  el cinturón de seguridad para el catch-up/reproceso. Esto cierra el límite conocido de
  `docs/NOTIFICATIONS-DUNNING.md` sin cambiar la semántica de las etapas.
- El `NotificationLog` se escribe dentro del `tenantScope` del cargo que ya abre
  `FindMemberInChargeSiteAsync` (así `SiteId` es el del cargo).

### Decisiones
- **Canal `WhatsApp` aunque hoy el sender sea log-only:** el log refleja la *intención* del canal
  configurado. Cuando se enchufe un proveedor real (fuera de fase) no cambia el esquema.
- **Dedup por `(CompanyId, ChargeId, StageDaysOverdue)`**, company-wide. Dos etapas distintas del mismo
  cargo se registran por separado (son avisos distintos).

### Tests (TDD)
Ampliar `tests/GymForge.Application.Tests/Handlers/DunningServiceTests.cs`:
- `Envia_Y_RegistraNotificationLog` (se llamó `AppendAsync` con la etapa y el cargo correctos).
- `NoReenvia_SiYaSeRegistroEsaEtapaParaEseCargo` (`WasSentAsync` → true ⇒ no `SendAsync`, no `Append`).
- `DistintaEtapaDelMismoCargo_SeEnviaYRegistra` (dedup no bloquea otra etapa).
- Los casos actuales (día exacto, sin mobile, saldo 0, deshabilitado) **siguen verdes** (agregar el
  substitute de `INotificationLogRepository` al setup).

---

## Pieza E — Link `wa.me` + envío manual

### Qué falta
Abrir WhatsApp con el mensaje prearmado, gratis, y dejar traza del envío manual.

### Diseño

**1. Armado del link — puro y testeable, en Application** (lo necesita el handler para devolver la URL):

```csharp
// .../Dunning/WhatsAppLink.cs
public static class WhatsAppLink
{
    // https://wa.me/<E164 sin +>?text=<url-encoded>
    public static string Build(string rawPhone, string message, string countryCode = "54", string mobilePrefix = "9");

    // Normaliza un celular AR a dígitos wa.me (54 + 9 + área + número, sin 0 ni 15). Best-effort.
    // Devuelve null si no quedan dígitos usables.
    public static string? ToWhatsAppDigits(string? rawPhone, string countryCode = "54", string mobilePrefix = "9");
}
```

Reglas de `ToWhatsAppDigits` (best-effort, AR por defecto; documentadas y testeadas):
1. Quitar todo lo no numérico.
2. Si ya empieza con el `countryCode` ("54"), tomar el resto como núcleo nacional; si no, el total es el
   núcleo.
3. Sacar un `0` inicial (prefijo troncal) y un `15` inicial (celular legacy) del núcleo.
4. Anteponer `countryCode + mobilePrefix` ("549") si el núcleo no lo trae ya.
5. Si no quedan dígitos → `null`.

> **Límite explícito:** la numeración móvil AR es ambigua (el `15` puede venir después del código de área;
> no hay forma genérica infalible sin una librería tipo libphonenumber, fuera de alcance). Es **best-effort**:
> números mal cargados pueden producir un link incorrecto que el operador corrige a mano. Recomendación de
> producto (no de código): cargar los móviles en formato internacional. `countryCode`/`mobilePrefix` quedan
> parametrizados con default AR; hacerlos configurables por empresa queda fuera de fase.

**2. Apertura del link — `IWhatsAppLinkSender` en Desktop** (abre un proceso del SO; es concern de Desktop,
no de Application pura):

```csharp
// src/GymForge.Desktop/Services/IWhatsAppLinkSender.cs
public interface IWhatsAppLinkSender { void Open(string url); }

// WhatsAppLinkSender: Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
// UseShellExecute=true => Windows abre WhatsApp Desktop si está instalado, si no el navegador (WhatsApp Web).
// Best-effort: envolver en try/catch + log (mismo criterio que SettingsViewModel.RestartApp).
```

Registrar `services.AddSingleton<IWhatsAppLinkSender, WhatsAppLinkSender>();` en
`ServiceCollectionExtensions` (Desktop).

**3. Comando que arma el mensaje, registra y devuelve la URL** (Application pura: **no** hace `Process.Start`).

```csharp
// .../Dunning/SendWhatsAppReminderCommand.cs
public record SendWhatsAppReminderResult(bool PhoneUsable, string? WhatsAppUrl, string? Reason);
public record SendWhatsAppReminderCommand(Guid CompanyId, Guid SiteId, Guid MemberId) : IRequest<SendWhatsAppReminderResult>;
```

Handler:
1. Carga el socio (sede activa) y sus cargos en mora por sede (`GetOverdueBySiteAsync`, ver Pieza F).
2. Si `string.IsNullOrWhiteSpace(member.Mobile)` o `ToWhatsAppDigits` devuelve null →
   `new(false, null, "El socio no tiene un celular válido.")` (no registra nada).
3. Calcula `outstanding` (suma de `AmountOutstanding`), `oldestDue` (menor `DueDate`), `daysOverdue = asOf -
   oldestDue`.
4. Elige la etapa aplicable: la de **mayor `DaysOverdue` que sea <= daysOverdue** (la etapa "vigente" del
   deudor); si ninguna aplica (deuda más nueva que la primera etapa), usa la **primera** etapa como texto
   genérico, con `StageDaysOverdue = null` en el log.
5. `message = DunningMessageRenderer.Render(stage.Template, ctx)`.
6. `url = WhatsAppLink.Build(member.Mobile!, message)`.
7. `AppendAsync(NotificationLog.Manual(CompanyId, SiteId, MemberId, chargeId: oldest?.Id,
   NotificationChannel.WhatsApp, stageDaysOverdue, member.Mobile))` + `SaveChanges`.
8. Devuelve `new(true, url, null)`.

El VM de morosos recibe el resultado y, si `PhoneUsable`, llama `_whatsapp.Open(result.WhatsAppUrl)`.

### Decisiones
- **El handler registra, el VM abre.** Mantiene Application sin `Process.Start` (testeable) y Desktop fino.
- **Se registra al iniciar el recordatorio, no al confirmar entrega.** `wa.me` no puede confirmar que el
  operador haya tocado "enviar". El `NotificationLog` manual significa **"recordatorio iniciado por el
  operador"**, no entrega garantizada. Límite explícito y aceptable para Fase 2.
- **Sin mobile usable → no se registra y se avisa** (coherente con que el job saltea a los sin mobile).

### Tests (TDD)
- `tests/GymForge.Application.Tests/Dunning/WhatsAppLinkTests.cs`: `Build` arma `https://wa.me/<digits>?text=`
  con el texto URL-encoded; `ToWhatsAppDigits` para casos AR: `"11 1234-5678"`, `"011 15 2345-6789"`,
  `"+54 9 11 2345 6789"`, `"1512345678"`, vacío/`null` → `null`. (Fijar el contrato exacto con qa.)
- `tests/GymForge.Application.Tests/Handlers/SendWhatsAppReminderCommandTests.cs` (fakes): elige la etapa
  correcta por antigüedad; arma el mensaje con el saldo/fecha del socio; registra `NotificationLog.Manual`
  con la etapa; devuelve la URL; **sin mobile ⇒ `PhoneUsable=false`, no registra, no URL**.
- `IWhatsAppLinkSender` real **no** se testea en unit (abre proceso); verificación manual.

---

## Pieza F — Pantalla de morosos (por sede)

### Qué falta
Una sección del sidebar que liste la mora de la sede activa y permita recordar/cobrar.

### Diseño

**1. Lectura por sede.** Nuevo método en `IChargeRepository` (no tocar `GetOverdueAsync`, que es company-wide
para el job):

```csharp
/// <summary>Mora de UNA sede a la fecha. IgnoreQueryFilters() + CompanyId && SiteId explícitos; incluye Member.</summary>
Task<IReadOnlyList<Charge>> GetOverdueBySiteAsync(Guid companyId, Guid siteId, DateOnly asOf, CancellationToken ct = default);
```

**2. Query MediatR** (agrupa cargos por socio):

```csharp
// .../Dunning/GetOverdueMembersQuery.cs
public record OverdueMemberDto(
    Guid MemberId, string FullName, string? Mobile, bool HasUsableMobile,
    decimal TotalOutstanding, DateOnly OldestDueDate, int DaysOverdue, int ChargeCount,
    string CurrentStageLabel, DateTime? LastReminderAt);
public record GetOverdueMembersQuery(Guid CompanyId, Guid SiteId, DateOnly AsOf) : IRequest<IReadOnlyList<OverdueMemberDto>>;
```

Handler: `GetOverdueBySiteAsync` → agrupa por `MemberId` → por socio calcula total/oldest/days/count;
`HasUsableMobile = WhatsAppLink.ToWhatsAppDigits(mobile) is not null`; `CurrentStageLabel` desde
`DunningConfig` (misma lógica de "etapa vigente" que Pieza E); `LastReminderAt` desde
`INotificationLogRepository.GetLastForMemberAsync`. Ordenar por `DaysOverdue` desc (o por monto; confirmar).

**3. Desktop.**
- `NavSection.Collections` (nuevo valor del enum) en el grupo **GESTIÓN** del sidebar (`MainWindow.axaml`),
  rama en el `switch` de `Navigate`, registro del VM, `DataTemplate` en `App.axaml`.
- `CollectionsViewModel` (`src/GymForge.Desktop/ViewModels/Collections/`): `LoadAsync` corre la query para
  `_session.CompanyId` + `_session.CurrentSite` + `ICompanyCalendar.TodayFor(company)` (día local, no UTC).
  Expone `ObservableCollection<OverdueMemberRow>`, total de mora de la sede y contador de deudores.
  Comandos por fila:
  - `RemindWhatsAppCommand(row)` → `SendWhatsAppReminderCommand` → si `PhoneUsable`, `_whatsapp.Open(url)`;
    refresca "último recordatorio" de esa fila. `CanExecute = row.HasUsableMobile`.
  - `ChargeCommand(row)` → abre `PaymentModalViewModel` para ese socio (reutiliza el flujo de cobro
    existente). **Depende de turno de caja abierto**: si `!_session.HasOpenShift`, mostrar hint "Abrí la caja
    para cobrar" (no romper). Tras cobrar, recargar la lista.
- `CollectionsView.axaml`: `DataGrid` (socio · deuda · desde · días · etapa · último recordatorio · acciones),
  siguiendo el estilo de `ChargesView`/`ReportsView`. `ux` define el detalle visual y los estados (pill de
  etapa, "sin celular" cuando `!HasUsableMobile`).

### Decisiones
- **Por sede**, con el día local de la empresa. Es el fix de UX de la deuda D1 (sin resolver D1 de fondo).
- **Cobro = reutilizar `PaymentModal`.** No se diseña un cobro nuevo; se respeta que exige turno abierto.
- **El botón de WhatsApp se deshabilita** si el socio no tiene celular usable (no abrir un link roto).

### Tests (TDD)
- `tests/GymForge.Application.Tests/Handlers/GetOverdueMembersQueryTests.cs`: agrupa varios cargos de un
  socio en una fila (suma, oldest, days, count); no mezcla sedes (solo la pedida); marca
  `HasUsableMobile` bien; `CurrentStageLabel` según antigüedad; `LastReminderAt` desde el log.
- Integración: `GetOverdueBySiteAsync` devuelve solo la mora de esa sede (otra sede no filtra); excluye
  `Paid/WrittenOff/Refunded`.
- `tests/GymForge.Desktop.Tests/CollectionsViewModelTests.cs`: carga arma filas; `RemindWhatsApp` llama al
  command y abre el link (fake `IWhatsAppLinkSender`); botón deshabilitado sin mobile; refresco tras cobrar.

### Config card (parte de la UI de Pieza C)
Config → nueva sección **"Cobranzas y recordatorios"** en `SettingsView.axaml` + campos en
`SettingsViewModel` (patrón de las otras secciones: `[ObservableProperty]`, `[RelayCommand] SaveDunningAsync`
→ `UpdateDunningSettingsCommand`, mensaje `success`/`danger` con `TextBlock.Feedback`). Incluye el toggle
`Enabled`, el editor de las 5 etapas (día + label + plantilla) y un recuadro de ayuda con los placeholders
disponibles. `ux` define el layout.

---

## Orden de implementación (TDD)

Primero lo puro y de bajo riesgo; la UI al final. Un commit por paso (regla 7), `dotnet test` verde antes de
cada uno.

1. **Pieza A — plantillas + renderer.** Sin schema. Refactor de `DunningStage`/`DunningConfig` + renderer
   puro; adaptar `DunningService` y `DunningServiceTests`. Borrar `DunningTemplates`.
   `feat: [cobranzas] plantillas de recordatorio editables con placeholders`
2. **Pieza B — `NotificationLog`.** Entidad + enums (+ refactor del `NotificationChannel` a Domain) + EF +
   migración `AddNotificationLog` + repo dedicado + tests de integración.
   `feat: [cobranzas] NotificationLog append-only`
3. **Pieza C — `DunningConfig` persistido.** Columna `Company.DunningJson` + migración
   `AddCompanyDunningConfig` + `DunningSettings` + hidratación en `SessionContext` +
   `UpdateDunningSettingsCommand` + tests.
   `feat: [cobranzas] DunningConfig editable persistido en Company`
4. **Pieza D — integración con el job.** `DunningService` escribe `NotificationLog` + dedup; ampliar
   `DunningServiceTests`.
   `feat: [cobranzas] registrar y deduplicar los envios del job`
5. **Pieza E — link `wa.me` + envío manual.** `WhatsAppLink` + `IWhatsAppLinkSender` +
   `SendWhatsAppReminderCommand` + tests.
   `feat: [cobranzas] recordatorio por link wa.me`
6. **Pieza F — pantalla de morosos.** `GetOverdueBySiteAsync` + `GetOverdueMembersQuery` + `NavSection`
   + VM/View + reuso de `PaymentModal` + tests.
   `feat: [cobranzas] pantalla de morosos por sede`
7. **UI de Config (parte de C).** Sección "Cobranzas y recordatorios" en `SettingsView` + binding del
   `UpdateDunningSettingsCommand`. (Puede ir pegada al paso 3 si `ux` está disponible antes; se deja al
   final para no bloquear el backend.)
   `feat: [config] configuracion de cobranzas`

Pasos 2 y 3 introducen migraciones EF: generarlas con `dotnet ef migrations add <Nombre> --project
src/GymForge.Infrastructure --startup-project src/GymForge.Infrastructure` (CLAUDE.md). El arranque las
aplica con `MigrateAsync()`.

## Piezas que tocan UI (entra `ux`)
- **Pantalla de morosos (F):** `DataGrid` de deudores + acciones por fila (Recordar WhatsApp / Cobrar), pill
  de etapa, estado "sin celular", total de mora de la sede. Botón nuevo en el sidebar (grupo GESTIÓN).
- **Config → Cobranzas y recordatorios (C):** toggle + editor de etapas/plantillas + ayuda de placeholders.
- Resto de las piezas: sin UI.

## Decisiones a confirmar con el usuario
1. **Normalización telefónica AR:** validar el set de casos de `ToWhatsAppDigits` (sobre todo el manejo del
   `15` y de números ya internacionales). ¿Alcanza el best-effort AR por defecto o algún gimnasio necesita
   otro país / configurable por empresa? (lo segundo, fuera de fase salvo pedido).
2. **Dunning automático:** confirmar que en Fase 2 el job **sigue log-only** (sin envío real) y el canal
   gratis es el manual. El envío automático real necesita API paga → fuera de fase.
3. **Texto del `NotificationLog` manual:** confirmar que registrar "recordatorio iniciado" (no "entregado")
   es aceptable, dado que `wa.me` no confirma entrega.
4. **Editor de etapas:** ¿basta editar las 5 etapas fijas (día+label+plantilla) o se quiere agregar/quitar
   etapas en v1?
5. **Orden de la lista de morosos:** por días de atraso (default propuesto) o por monto adeudado.

---

## Consecuencias
- `DunningConfig`/`DunningStage` cambian de forma (etapa con `Template`); `DunningTemplates` desaparece.
  Impacto acotado: solo `DunningService` y sus tests lo consumen.
- `Company` suma una columna JSON más (`DunningJson`), consistente con las existentes; migración trivial,
  sin default, nullable → empresas viejas caen a `DunningSettings.Default()` (dunning apagado, plantillas de
  fábrica).
- `NotificationChannel` se muda a `Domain.Enums` (dedup de tipos); `INotificationSender`/`NotificationMessage`
  pasan a referenciarlo.
- Nueva tabla `NotificationLogs` append-only: crece con el uso. Rotación/retención de logs de notificación
  queda fuera de fase (hoy no hay política para `AccessLog` tampoco).
- `GymForge.Desktop` suma `Process.Start` para abrir `wa.me` (ya se usa el patrón en `SettingsViewModel`).
  No agrega dependencias nuevas de paquetes.
- El límite de catch-up que reenviaba avisos (NOTIFICATIONS-DUNNING) queda cerrado por el dedup de la Pieza D.

## Fuera de alcance de Fase 2 (deuda explícita)
- **Envío automático real de WhatsApp/SMS** (Twilio/Wassenger, API paga): descartado para esta fase; el job
  sigue log-only. Reactivar `INotificationSender` con un proveedor real es trabajo futuro.
- **Confirmación de entrega/lectura/respuesta:** imposible con `wa.me`.
- **Opt-out "no molestar" por socio + respeto de horarios:** pendiente (ya anotado en NOTIFICATIONS-DUNNING).
- **Normalización telefónica infalible** (libphonenumber) y país configurable por empresa.
- **Cobro multi-sede (D1):** la pantalla es por sede; D1 de fondo sigue abierta.
- **Reportes de efectividad de cobranza** (recuperado por etapa/canal): es Fase 4.

---

## Referencias
- Dunning y job: `src/GymForge.Application/UseCases/Dunning/` · `src/GymForge.Application/UseCases/Maintenance/`
- Diseño previo: `docs/NOTIFICATIONS-DUNNING.md`
- Mora: `src/GymForge.Infrastructure/Repositories/ChargeRepository.cs` (`GetOverdueAsync`, deuda D1) ·
  `src/GymForge.Domain/Entities/Charge.cs` (`IsInArrears`)
- Patrón de config por empresa: `GatekeeperConfig` + `AccessSettings` en
  `src/GymForge.Application/UseCases/Settings/SettingsCommands.cs` + hidratación en
  `src/GymForge.Desktop/Services/SessionContext.cs`
- Patrón de log append-only: `src/GymForge.Domain/Entities/AccessLog.cs` ·
  `src/GymForge.Domain/Entities/MemberCreditMovement.cs` · `IAccessLogRepository`
- Navegación: `src/GymForge.Desktop/Views/Shell/MainWindowViewModel.cs` · `MainWindow.axaml` · `App.axaml`
- Teléfono: `src/GymForge.Domain/ValueObjects/PhoneNumber.cs` · `Member.Mobile`
- Roadmap/estado: `docs/PROGRESS.md` · `docs/TASKS.md`
