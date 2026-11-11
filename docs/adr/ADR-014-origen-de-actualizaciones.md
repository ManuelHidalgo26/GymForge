# ADR-014 — Origen de las actualizaciones: default en el build + edición protegida por PIN + https obligatorio (UX11, F1-D7, F1-D3)

**Status:** Accepted
**Date:** 2026-11-11
**Deciders:** **usuario** decidió *default en el build (A) + campo editable en Config protegido por PIN de admin, exigiendo https (B)* (sobre "solo editable tras PIN" o "dejar inactivo") · **arqui** diseña el mecanismo · implementa **dev** · veta **qa**
**Sesión:** Cierre de **UX11** y **F1-D7** (y, incluido, **F1-D3**) de ADR-004 ("Robustez de producción", pieza 2 auto-update Velopack). Extiende ADR-004 §auto-update. Reusa el gate de PIN de ADR-012 (frase de respaldo) y ADR-013 (export).
**Piso:** no bajar los tests verdes vigentes (1649). Sin migración EF (no toca esquema).

---

## Context

### El problema (UX11 + F1-D7, `docs/TASKS.md`)

La auto-actualización (Velopack, ADR-004 pieza 2) toma su origen de `ILocalSettings.UpdateFeedUrl`, que hoy
**solo se cambia a mano** editando `%LOCALAPPDATA%\GymForge\settings.json`. Consecuencias:

1. **UX11 (configurabilidad).** No hay URL por defecto y la recepción no puede fijarla desde la app: sin
   origen, la auto-actualización queda **"sin configurar"** (`VelopackUpdateService.CheckAsync` →
   `UpdateCheckResult.FeedNotConfigured`, `VelopackUpdateService.cs:35-36`). La card de Config solo la
   muestra **de solo lectura** (`SettingsView.axaml:712-720`; VM `SettingsViewModel.cs:205-207`). En campo,
   nadie puede activar ni corregir el origen sin tocar un archivo.
2. **F1-D7 (seguridad).** `CreateManager` acepta **cualquier** string, incluido `http://`, sin chequeo
   (`VelopackUpdateService.cs:106-113`). Velopack valida el SHA del paquete contra el `RELEASES` del **mismo
   origen**: si ese origen viaja por HTTP, un **MITM** sirve su propio `RELEASES` + paquete y la app lo
   instala. **Un feed HTTP equivale a ejecución remota de código.** Hay que exigir `https://` (o UNC/ruta
   local, que no viajan por red insegura).

### Qué decidió el usuario

- **(A)** Una **URL por defecto en el build** para el piloto, de modo que una instalación nueva ya apunte al
  canal de releases sin tocar nada.
- **(B)** Un **campo editable en Config**, que **solo guarda tras validar el PIN de admin** (`AuthenticateStaffCommand`),
  **exigiendo `https://`**. Así soporte/recepción corrige el origen sin editar archivos.

Este ADR define: **dónde vive el default** y su valor, **la precedencia** override vs. default, **la validación**
(defensa en los dos lados), **si F1-D3 entra en este circuito**, y **el estado de activación** del piloto.

### Hechos del código verificados al 2026-11-11 (no asumidos)

- **El feed se lee en cada `CheckAsync`** desde `ILocalSettings.UpdateFeedUrl` (trim + vacío → `FeedNotConfigured`),
  `VelopackUpdateService.cs:33-49`. `CreateManager` decide GitHub vs. carpeta por host `*.github.com`
  (`:106-113`) y arma `GithubSource(feedUrl, accessToken: null, prerelease: false)` (`:108`) — **sin token**.
- **No hay ningún default** hoy: `LocalSettingsService.Load()` lee `UpdateFeedUrl` del JSON y, si falta, queda
  `null` (`LocalSettingsService.cs:48,79`). El persistido es `StoredSettings(... UpdateFeedUrl)` (`:88-89`).
- **`UpdateFeedUrl` no tiene lock** (a diferencia de los setters off-site, que corren bajo `_saveGate`,
  `LocalSettingsService.cs:36-46`). Es la deuda **NEW-48**: lo escribe la UI y lo lee el chequeo en `Task.Run`.
- **El gate de PIN ya está resuelto y es el patrón a calcar:** `ConfirmImportAsync`
  (`SettingsViewModel.cs:618-623`) y `SetBackupPassphraseAsync` (`:679-684`) hacen
  `await _mediator.Send(new AuthenticateStaffCommand(_session.CompanyId, pin.Trim()))` y, si es `null`,
  cortan con "PIN incorrecto." sin tocar nada. `AuthenticateStaffCommand` devuelve el staff activo cuyo PIN
  coincide (`AuthenticateStaffCommand.cs:21-28`); gate = cualquier PIN de staff activo (no hay roles finos hoy,
  igual que O10/ADR-013).
- **El Desktop NO tiene pipeline de configuración** (`appsettings.json`/`IConfiguration`): `Program.cs` arma un
  `ServiceCollection` plano (`Program.cs:52-54`) y el connection string se construye con una interpolación
  (`ServiceCollectionExtensions.cs:24-32`). No hay de dónde "leer" un appsettings sin agregar infra nueva.
- **La versión del build es un MSBuild prop** (`<Version>0.3.0</Version>`, `GymForge.Desktop.csproj:23`); la
  identidad del exe ya se define ahí. Es el lugar natural si se quisiera parametrizar por build, pero ver §1.
- **F1-D3 (prerequisito):** el snapshot diario (`backup.BackupIfDue()`) se toma **después** de `MigrateAsync`
  (`InfrastructureServiceExtensions.cs:117,127`). `BackupNow(string reason)` es best-effort (no lanza, `false`
  si falla el local; `DatabaseBackupService.cs:91-98`) y dispara la copia off-site en segundo plano.
- **El canal de releases NO existe aún** (CLAUDE.md: "Distribución web: GitHub Releases ... primero" está
  **sin hacer**). `pack.ps1:68` deja escrito, como ejemplo del comando de publicación, un repo candidato
  (`vpk upload github --repoUrl https://github.com/ManuelHidalgo26/GymForge`), pero no hay un feed de releases
  público verificado. **No se inventa ni se hardcodea una URL de release en este ADR.**

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first / best-effort del update:** buscar/descargar/aplicar **nunca** rompe una operación ni el
  arranque (hoy todo es tolerante a fallo). La validación y el default no pueden cambiar eso.
- **Simplicidad > cleverness:** no se agrega `IConfiguration`/`appsettings` ni un framework de config por un
  solo string. Una **constante compilada** + un **resolver** + un **validador**, bien testeados.

---

## Decisión

### 1) Default en el build (A) — una constante compilada, **valor vacío hasta montar el canal**

**Dónde vive:** una **constante** en Desktop, en un único helper nuevo
`GymForge.Desktop.Services.UpdateFeed` (misma carpeta que `VelopackUpdateService`):

```csharp
public static class UpdateFeed
{
    /// <summary>
    /// Origen por defecto horneado en el build. VACÍO hasta montar el canal de releases público
    /// (GitHub Releases / host propio). Cuando exista, poner acá la URL https del feed y recompilar.
    /// NUNCA incrustar un token acá (viaja dentro del exe). Ver ADR-014.
    /// </summary>
    public const string BuildDefaultUrl = "";   // ← pendiente: se completa al montar el canal
    ...
}
```

**Valor concreto: queda VACÍO en esta entrega.** El canal de releases todavía no está montado (CLAUDE.md), así
que no hay una URL real que poner sin inventarla. Con `BuildDefaultUrl = ""`, el piloto arranca con la
auto-actualización **inactiva pero configurable** (ver §5). Cuando el canal exista, un dev pone la URL `https`
en esa constante y recompila. El trabajo de montar el canal queda **pendiente y explícito**:

- **Riesgo de embeber token (repo privado):** un `GithubSource` con `accessToken` requiere el token **dentro
  del exe**; cualquiera con el binario lo extrae → el repo "privado" es de lectura pública de facto **y**
  queda una credencial filtrada. **Recomendación: repo de releases PÚBLICO (sin token)** — que es exactamente
  como el código ya llama hoy (`accessToken: null`, `VelopackUpdateService.cs:108`) — **o** un host estático
  propio por `https`, o una ruta **UNC** interna. **No embeber token en el build.** Si el negocio exigiera un
  repo privado, el token debe venir del `settings.json` de la máquina (operativo), nunca de la constante.

**Por qué una `const` y no `appsettings.json`/`IConfiguration`:**

- El Desktop no tiene hoy pipeline de configuración (`Program.cs:52-54`); agregarlo por un string es infra
  nueva para nada (Simplicidad > cleverness).
- La `const` **se compila dentro del exe** y viaja con el publish single-file (`scripts/publish.ps1`): no hay
  archivo suelto que se pierda, ni que el gimnasio edite por accidente.
- **Trade-off aceptado:** cambiar el default exige recompilar. Es aceptable: el default solo cambia cuando se
  **mueve el canal de releases**, que ya es un evento de dev (se recompila para publicar de todos modos).

> Alternativa considerada y rechazada para el default: un MSBuild prop + `AssemblyMetadata` leído por reflexión.
> Da parametrización por build sin tocar C#, pero suma complejidad (atributo + lectura) por un valor que casi
> nunca cambia. Se prefiere la `const` directa. (El override por máquina —§2— cubre el caso "esta PC necesita
> otro feed" sin recompilar.)

### 2) Campo editable tras PIN (B) — override en `settings.json`, precedencia sobre el default

En la card **"Versión y actualizaciones"** de Config, el bloque "Origen de las actualizaciones" pasa de
solo-lectura a **editable**: un `TextBox` ligado a `UpdateFeedInput`, un campo de **PIN**, y un botón
**"Guardar origen"**. El flujo (calcado de `SetBackupPassphraseAsync`/`ConfirmImportAsync`):

1. **Validar el esquema** del input (ver §3) **antes** de pedir PIN. Si es inválido (p. ej. `http://`),
   mensaje claro y **no** se pide PIN ni se guarda.
2. `AuthenticateStaffCommand(_session.CompanyId, pin.Trim())`. Si es `null` → "PIN incorrecto." y **no** se
   guarda.
3. Con PIN válido: `_localSettings.UpdateFeedUrl = input.Trim();` (o `null` si el input quedó vacío, ver
   abajo) + `_localSettings.Save();`. Mensaje de éxito y refresco del display.

**Precedencia (recomendado): el override de Config PISA al default del build.** El valor **efectivo** que usa
el servicio y que muestra Config es:

```
override (UpdateFeedUrl de settings.json, trim, no vacío)  ??  BuildDefaultUrl (no vacío)  ??  null
```

Motivo: permite a soporte apuntar **una** PC a otro feed (staging, un fix puntual, un UNC interno) **sin
recompilar**, y permite al gimnasio configurar el origen **antes** de que exista el default del build (que es
el caso del piloto). El default es el piso; el override manda.

**"Vaciar" el override (volver al default):** si se guarda el campo **vacío** con PIN válido, se setea
`UpdateFeedUrl = null` y el efectivo vuelve al `BuildDefaultUrl` (o a "sin configurar" si éste está vacío). Es
la forma de "deshacer" un override manual.

**"Sin configurar" vs. "configurado":** el display muestra el valor **efectivo**:
- efectivo `null`/vacío → **"sin configurar (la auto-actualización está desactivada)"**;
- efectivo con valor → se muestra la URL. Opcional (nice-to-have): una etiqueta "(configurado en esta PC)"
  cuando hay override, para distinguirlo del default del build. El `TextBox` editable se **pre-carga con el
  override** (vacío si solo rige el default), para que "vaciar" sea significativo; además una línea de solo
  lectura "En uso: &lt;efectivo&gt;" deja claro qué va a consultar el servicio.

Para que servicio y VM coincidan, **ambos** resuelven con el mismo helper (§4): `UpdateFeed.Resolve(...)`.

### 3) Validación (F1-D7) — `https` / UNC / ruta local; `http` rechazado; defensa en los DOS lados

**Esquemas permitidos:**

| Entrada | ¿Permitida? | Por qué |
|---|---|---|
| `https://host/...` | **SÍ** | Canal cifrado/autenticado. Es el caso de GitHub Releases (`*.github.com` es https) y de un host propio. |
| UNC `\\servidor\share\releases` | **SÍ** | Carpeta de red; no viaja por HTTP inseguro. |
| Ruta local `D:\releases`, `C:\...` | **SÍ** | Carpeta de archivos de Velopack en disco/pendrive. |
| `http://...` | **NO** | MITM = ejecución de código (F1-D7). |
| Cualquier otro (`ftp://`, `file://http...`, basura) | **NO** | Fuera del contrato. |

**Helper único** (en `UpdateFeed`), usado por el VM (al guardar) y por el servicio (al resolver):

```csharp
public static bool IsAllowedSource(string? value, out string? error);
```

Lógica sugerida (dev afina la forma final):
- `null`/vacío → **permitido** (significa "sin configurar"; no es un error de validación, es "desactivado").
- Trim. Si empieza con `\\` → **UNC** → permitido.
- `Uri.TryCreate(value, UriKind.Absolute, out var uri)`:
  - `uri.Scheme == "https"` con `Host` no vacío → permitido.
  - `uri.IsFile` (cubre UNC y ruta local `X:\...`, que `Uri` mapea a `file`) → permitido.
  - en otro caso (incluido `http`) → **rechazado** con `error` claro.
- Mensaje de error (es-AR): *"El origen debe empezar con `https://`, o ser una carpeta de red (`\\servidor\…`)
  o una carpeta local. No se admite `http://` por seguridad."*

**Defensa en profundidad — se valida en los dos lados:**

1. **Al guardar en Config** (`SettingsViewModel`): rechaza `http://` antes de persistir (no escribe un
   `settings.json` inseguro).
2. **Al resolver el feed en `VelopackUpdateService.CheckAsync`**: si el efectivo **no** pasa `IsAllowedSource`
   (porque alguien editó el `settings.json` a mano), **no** se construye el `UpdateManager` ni se sale a la
   red. Devuelve un resultado que la UI muestra con mensaje claro.
   - **Recomendado:** agregar `UpdateCheckStatus.FeedInsecure` (+ `UpdateCheckResult.FeedInsecure` estático) y
     su caso en `UpdateViewModel.MessageFor` (*"El origen de actualizaciones no es seguro (debe ser https): se
     ignoró."*). Es barato y da un mensaje accionable para soporte ("por qué no actualiza: el origen es http").
   - **Alternativa aceptable** si se quiere evitar tocar el enum: reusar `UpdateCheckResult.Failed` + una línea
     de log específica. Pierde el mensaje explicativo. Se prefiere el status nuevo.

`GithubSource` y la rama "carpeta de archivos" de `CreateManager` quedan **igual**: la validación ocurre
**antes**; un `https://…github.com` sigue cayendo en `GithubSource(..., accessToken: null, ...)`.

### 4) El resolver (una sola fuente de verdad), testeable

En `UpdateFeed`:

```csharp
// buildDefault es parámetro con default = BuildDefaultUrl para poder testear la precedencia
public static string? Resolve(ILocalSettings settings, string? buildDefault = BuildDefaultUrl)
{
    var over = settings.UpdateFeedUrl?.Trim();
    if (!string.IsNullOrEmpty(over)) return over;
    return string.IsNullOrWhiteSpace(buildDefault) ? null : buildDefault.Trim();
}
```

- `VelopackUpdateService.CheckAsync` usa `UpdateFeed.Resolve(_settings)` en lugar de leer `_settings.UpdateFeedUrl`
  directo; vacío → `FeedNotConfigured` (igual que hoy); no permitido → `FeedInsecure` (§3); si pasa → sigue el
  flujo actual (`Task.Run(CheckFeedAsync)`).
- `SettingsViewModel` usa `UpdateFeed.Resolve(_localSettings)` para el display "En uso" y pre-carga el input
  con `_localSettings.UpdateFeedUrl` (el override, no el efectivo).

### 5) Estado de activación del piloto (decisión 5)

Con `BuildDefaultUrl = ""` y sin override, el efectivo es `null` → `FeedNotConfigured` → **la auto-actualización
sigue inactiva**, pero ahora **configurable desde Config tras PIN**. **Nada peligroso se activa por defecto**:
no hay feed al que conectarse hasta que alguien (dev, poniendo el default y recompilando; o soporte/recepción,
cargando un `https` en Config con PIN) lo configure explícitamente. La activación en campo es **un acto
deliberado**, y recién tras cargar un origen válido empieza a buscar updates. Esto se documenta en la nota de
release: "el piloto se entrega con la auto-actualización inactiva pero lista para configurarse".

### 6) F1-D3 — **SÍ se incluye en este mismo circuito** (recomendación)

**Recomendación: incluir F1-D3 acá.** Es chico, y es la **última** red de seguridad que falta para que
*activar* el auto-update en campo sea seguro. El riesgo que cubre es propio del auto-update: la app nueva
**migra la base en el primer arranque** (`MigrateAsync`, `InfrastructureServiceExtensions.cs:117`), y hoy el
único snapshot sano podría ser el del **día anterior** (el diario se toma **después** de migrar, `:127`). Si
una migración sale mal, se pierde hasta un día de operación. Entregar UX11+F1-D7 **sin** F1-D3 dejaría
documentada una ventana de pérdida de datos justo en el camino que este ADR habilita. Hacer los tres juntos
cierra "activar auto-update" de forma completa y revisable en un solo cambio.

**Fix (exactamente el propuesto en TASKS):** antes de `MigrateAsync`, si hay migraciones pendientes, tomar un
backup rotulado `"pre-migracion"`. En `InitialiseDatabaseAsync`:

```
EnsureHealthy()                         // (sin cambios)
BaselineLegacyDatabaseAsync(db)         // (sin cambios) — deja el historial consistente antes de contar pendientes
MemberDocumentMigrationGuard...         // (sin cambios)
+  if ((await db.Database.GetPendingMigrationsAsync()).Any())
+      backup.BackupNow("pre-migracion");   // best-effort: no bloquea el arranque, no lanza
await db.Database.MigrateAsync()        // (sin cambios)
EnableWriteAheadLoggingAsync(db)        // (sin cambios)
MemberStatusReconciliation...           // (sin cambios)
seeder.SeedAsync()                      // (sin cambios)
backup.BackupIfDue()                    // (sin cambios)
```

Detalles:
- **Después de `BaselineLegacyDatabaseAsync`** (no antes): una base legacy ya baselined no debe disparar un
  backup espurio; tras el baseline, `GetPendingMigrationsAsync` refleja migraciones **realmente** sin aplicar.
- **Base nueva/inexistente:** `GetPendingMigrationsAsync` devuelve todas como pendientes, pero `BackupNow` →
  `CreateLocalBackup` hace `if (!File.Exists(_dbPath)) return null` (`DatabaseBackupService.cs:124`): **no**
  crea backup (no hay nada que respaldar). El pre-migración solo se materializa cuando la base **ya existe** y
  tiene migraciones sin aplicar — exactamente el escenario de auto-update.
- **Interacción con `BackupIfDue()` (borde benigno):** si el pre-migración se tomó hoy, `BackupIfDue` lo ve y
  es no-op ese día (`:80`). Correcto: el pre-migración (esquema viejo, pre-cambio) **es** el punto de
  restauración que F1-D3 pide. El diario "normal" se retoma al día siguiente. No se agrega un post-migración
  para no inflar el alcance.
- **Best-effort:** no se chequea el `bool` de `BackupNow` ni se bloquea el arranque; dispara su copia off-site
  en segundo plano como cualquier otro backup.

---

## Diseño para dev (resumen de componentes)

- **`UpdateFeed`** (nuevo, `src/GymForge.Desktop/Services/UpdateFeed.cs`): `const BuildDefaultUrl = ""`;
  `Resolve(ILocalSettings, string? buildDefault = BuildDefaultUrl)`; `IsAllowedSource(string?, out string?)`.
  Es el único lugar que conoce el default y las reglas de esquema.
- **`VelopackUpdateService.CheckAsync`** (`:33-49`): leer el efectivo con `UpdateFeed.Resolve(_settings)`;
  vacío → `FeedNotConfigured`; `!IsAllowedSource` → `FeedInsecure` (+ log); si pasa → flujo actual. `CreateManager`
  y `GithubSource(..., accessToken: null, ...)` **no cambian**.
- **`IUpdateService` / `UpdateCheckStatus`** (`src/GymForge.Application/Interfaces/IUpdateService.cs:6-16`):
  agregar `FeedInsecure` al enum + `UpdateCheckResult.FeedInsecure` estático.
- **`UpdateViewModel.MessageFor`** (`src/GymForge.Desktop/ViewModels/Shared/UpdateViewModel.cs:123-131`):
  caso para `FeedInsecure`.
- **`SettingsViewModel`** (`:112,205-207` y un comando nuevo): `UpdateFeedInput`, `UpdateFeedPin`,
  `UpdateFeedMessage`/`UpdateFeedSaved`, display del efectivo con `UpdateFeed.Resolve`; comando `SaveUpdateFeed`
  con el orden validar-esquema → PIN (`AuthenticateStaffCommand`) → `UpdateFeedUrl = … / null` + `Save()`.
  Calcar `SetBackupPassphraseAsync` (`:661-705`).
- **`SettingsView.axaml`** (card "Versión y actualizaciones", `:712-720`): reemplazar el bloque de solo-lectura
  por `TextBox` editable + PIN + botón "Guardar origen" + línea "En uso: &lt;efectivo&gt;" + mensaje inline.
- **`LocalSettingsService`** (`:48`): **cerrar NEW-48** de paso — poner el get/set de `UpdateFeedUrl` bajo
  `_saveGate` como los setters off-site (`:36-46`), porque ahora lo escribe la UI y lo lee el chequeo en
  `Task.Run`. Contrato público sin cambios.
- **`InfrastructureServiceExtensions.InitialiseDatabaseAsync`** (`:104-128`): el guard de F1-D3 (§6), una línea
  antes de `MigrateAsync`. Para testear sin fricción, extraer un seam
  `BackupBeforePendingMigrationsAsync(db, backup)` que se pueda invocar con un `IDatabaseBackup` doble.

**No tocar:**
- `CreateManager`/`GithubSource`/`IsGitHubRepository` (la validación va **antes**; el shape no cambia).
- `EnsureHealthy`/`LatestBackup`/`BackupIfDue`/el patrón de backups locales y la copia off-site cifrada
  (ADR-012): F1-D3 solo **agrega** un `BackupNow("pre-migracion")`.
- El flujo de aviso/descarga/aplicar del update (banner, "Después", `ApplyOnExit`).
- El esquema EF (sin migración). El pipeline de arranque de `Program.cs` (no se agrega `IConfiguration`).
- La frase de respaldo / import / passphrase (ADR-012). La redacción PII / PIN de export (ADR-013).
- **No hardcodear una URL de release ni un token.** `BuildDefaultUrl` queda `""` hasta montar el canal.

---

## Tests / verificación

**Unit — `UpdateFeed` (validación y resolver):**
- `IsAllowedSource`: `http://...` → **rechazado** con mensaje; `https://host/...`, `\\srv\share`,
  `D:\releases`, `C:\...\releases` → **permitidos**; `null`/`""`/`"   "` → permitidos (= "sin configurar");
  `ftp://...`/basura → rechazados.
- `Resolve`: override no vacío + default no vacío → **override gana**; override vacío + default no vacío →
  **default**; ambos vacíos → **null**. (El `buildDefault` entra por parámetro para no depender de la `const`.)

**Unit — `VelopackUpdateService` (defensa en el servicio; extiende `VelopackUpdateServiceTests.cs`):**
- `UpdateFeedUrl = "http://updates.gymforge.invalid/..."` → `FeedInsecure`, **sin** salir a la red (sin
  `BootVelopackLikeProgramDoes`, igual que el patrón existente que no hace requests).
- `https://`, `\\servidor\share`, `C:\releases` con la app sin instalar → `NotInstalled` (pasan el guard de
  esquema; replican los `InlineData` ya existentes `:34-47`, que deben seguir en verde).
- override vacío + (si se simula) default no vacío → resuelve al default (vía `Resolve`; el servicio real usa
  la `const` vacía, así que este caso se cubre en el test de `Resolve`).

**Unit — `SettingsViewModel` (comando con PIN; calcar `SettingsViewModelBackupPassphraseTests.cs`):**
- **https + PIN correcto** → `_localSettings.UpdateFeedUrl` queda en la URL, `Save()` llamado, display "En uso"
  = la URL, `UpdateFeedSaved == true`.
- **PIN incorrecto** (`AuthenticateStaffCommand` → `null`) → **no** persiste (`UpdateFeedUrl` sin cambios,
  `Save()` no llamado), mensaje "PIN incorrecto.".
- **`http://` como input** → rechazado **antes** del PIN: `AuthenticateStaffCommand` **no** se envió
  (`_mediator.DidNotReceive().Send(Arg.Any<AuthenticateStaffCommand>())`), mensaje de https, no persiste.
- **UNC / ruta local válida + PIN correcto** → persiste (confirma que el gate acepta no-https seguros).
- **Input vacío + PIN correcto** → setea `UpdateFeedUrl = null` (vuelve al default/"sin configurar"),
  `Save()` llamado.

**Integration — F1-D3 (junto a `DatabaseBackupOffsiteTests.cs`):**
- **Base existente con una migración pendiente** → al correr el arranque (o el seam
  `BackupBeforePendingMigrationsAsync`) se produce un backup con slug **`pre-migracion`** **antes** de migrar
  (assert por nombre de archivo `gymforge-*-pre-migracion.db`, o `IDatabaseBackup` doble que registre
  `BackupNow("pre-migracion")`).
- **Base al día (sin migraciones pendientes)** → **no** se toma pre-migración (solo el diario).
- **Base inexistente** → `BackupNow("pre-migracion")` no crea archivo (`CreateLocalBackup` corta por
  `!File.Exists`): no rompe el primer arranque limpio.

**Humo manual (una vez exista el canal, fuera de CI):** poner `BuildDefaultUrl` a la URL real, publicar un
release, verificar que una instalación nueva detecta/descarga/instala; y que un override por Config a un feed
distinto manda sobre el default.

---

## Consecuencias

- **UX11 resuelto:** el origen se configura desde Config (tras PIN), sin editar archivos. El piloto puede
  activar/corregir el auto-update en campo.
- **F1-D7 resuelto:** `http://` se rechaza al guardar **y** al resolver (defensa en profundidad), aunque el
  `settings.json` se haya editado a mano. Se elimina el vector MITM = RCE.
- **F1-D3 resuelto:** hay un snapshot pre-migración cuando el auto-update trae una migración nueva; se cierra
  la ventana de pérdida de hasta un día.
- **NEW-48 cerrado de paso:** `UpdateFeedUrl` bajo lock.
- **Activación segura por defecto:** con el default vacío, nada se conecta solo; activar es deliberado.
- **Pendiente explícito:** montar el canal de releases y completar `BuildDefaultUrl` con la URL `https` real
  (repo público sin token / host propio / UNC). Hasta entonces, auto-update inactivo-pero-configurable.
- **Sin dependencias nuevas. Sin migración EF.**

## Riesgos

- **El default queda vacío** → en el piloto el auto-update no corre hasta configurarlo. **Aceptable y buscado**
  (no hay canal todavía); documentado.
- **Frase/URL mal tipeada en Config** → la validación de esquema la ataja; un `https` a un host inexistente
  cae en el best-effort existente (`Failed`, sin romper nada).
- **Tentación de embeber un token** para un repo privado → **prohibido** por este ADR (viaja en el exe). Si se
  necesita repo privado, el token va por `settings.json`, no por el build.
- **Ruta UNC/local como feed** → válida, pero si la carpeta no está, el update simplemente no encuentra nada
  (best-effort). No hay riesgo de seguridad (no viaja por HTTP).
- **F1-D3 best-effort** → si el pre-migración falla (disco lleno), el arranque sigue y migra igual (como hoy);
  queda el warning en el log. No se bloquea la operación por un backup.

## Alternativas rechazadas

- **Solo editable tras PIN, sin default (opción del enunciado descartada por el usuario):** deja al piloto sin
  auto-update hasta que alguien lo configure a mano en cada PC; el default (aunque hoy vacío) deja el mecanismo
  listo para cuando exista el canal.
- **Dejar el auto-update inactivo (opción descartada por el usuario):** no cumple el objetivo de poder
  actualizar en campo.
- **`appsettings.json`/`IConfiguration` para el default:** infra nueva por un string; el Desktop no la tiene.
  Se prefiere la `const` + override por máquina.
- **Reusar `Failed` para un feed inseguro:** funciona, pero pierde el mensaje accionable; se prefiere
  `FeedInsecure`.
- **Embeber un token de repo privado en el build:** credencial filtrable dentro del exe; se exige repo público
  o secreto por máquina.
- **Validar solo en Config (no en el servicio):** dejaría pasar un `settings.json` editado a mano; F1-D7 pide
  defensa en los dos lados.

## Referencias

- Servicio de update: `src/GymForge.Desktop/Services/VelopackUpdateService.cs:33-49,106-113` (feed + `GithubSource` sin token)
- Contrato: `src/GymForge.Application/Interfaces/IUpdateService.cs:6-16` (enum a extender) · `ILocalSettings.cs:16-20`
- VM de update (mensajes): `src/GymForge.Desktop/ViewModels/Shared/UpdateViewModel.cs:123-131`
- Config: `src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs:112,205-207,618-623,661-705` ·
  `src/GymForge.Desktop/Views/Settings/SettingsView.axaml:673-722`
- Settings locales: `src/GymForge.Infrastructure/Services/LocalSettingsService.cs:36-48,88-89` (NEW-48)
- Gate de PIN: `src/GymForge.Application/UseCases/Staff/AuthenticateStaffCommand.cs:21-28`
- F1-D3: `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:104-128` ·
  `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs:77-98,120-145`
- Build/distribución: `src/GymForge.Desktop/GymForge.Desktop.csproj:23` · `scripts/pack.ps1:68` (repo candidato) ·
  `scripts/publish.ps1`
- Deuda / políticas: `docs/TASKS.md` (UX11, F1-D7, F1-D3, NEW-48) · `docs/PROGRESS.md` · ADR-004 (pieza 2 auto-update)
