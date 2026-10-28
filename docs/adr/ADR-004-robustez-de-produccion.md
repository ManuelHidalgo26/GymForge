# ADR-004 — Robustez de producción (Fase 1)

**Status:** Accepted
**Date:** 2026-10-28
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` · veta `qa`
**Sesión:** 3 (continuación) — Fase 1 del roadmap (`docs/PROGRESS.md`), ítem **F1** de `docs/TASKS.md`

---

## Context

Fase 1 "Robustez de producción": lo que falta para instalar y operar en el gimnasio piloto sin perder
datos, poder actualizar la app en campo y tener el licenciamiento atado al cliente. Son **5 piezas**.
Esta es la guía de implementación para `dev` en TDD: no inventar arquitectura, seguir las firmas y el
orden de abajo. Alcance acotado a estas 5 piezas; nada más entra en F1.

Reglas del proyecto que condicionan el diseño (CLAUDE.md): offline-first (todo persiste local primero),
multi-tenant (`CompanyId`+`SiteId`), **AuditLog/AccessLog append-only**, `dotnet test` verde antes de cada
commit (regla 6). Piso actual: **504 tests verdes**.

### Hechos del código verificados al 2026-10-28

- **Backup.** `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs` hace copia consistente
  (SQLite backup API) a `backups/` **junto a la DB** (`%LOCALAPPDATA%\GymForge\backups\`), rota a `MaxBackups = 10`,
  y en `EnsureHealthy()` archiva la base corrupta y restaura el último backup. Es best-effort (nunca tira).
  La interfaz `IDatabaseBackup` (`src/GymForge.Application/Interfaces/IRepositories.cs:227`) **solo** expone
  `BackupNow(string reason)`. Se dispara en: arranque (`InfrastructureServiceExtensions.cs:89,100`:
  `EnsureHealthy` + `BackupIfDue`), cierre de caja (`CashCommands.cs:120`) y pre-import (`DataTransferService.cs:214`).
  **No hay ninguna copia off-site.**
- **Velopack.** `src/GymForge.Desktop/Program.cs:13` solo tiene `VelopackApp.Build().Run()` (hooks de
  install/update). El paquete `Velopack` está referenciado **solo** por `GymForge.Desktop`
  (`GymForge.Desktop.csproj:46`). `scripts/pack.ps1` genera el Setup y deja en `releases\` los artefactos
  `vpk upload github` (repo `https://github.com/ManuelHidalgo26/GymForge`). **No hay `UpdateManager` en runtime.**
- **Licencia.** `LicensePayload` (`src/GymForge.Application/UseCases/Licensing/LicenseModels.cs:4`) ya tiene
  `Cuit`, `MaxSites`, `MaxMembers`. `LicenseCodec` firma/verifica JSON con ECDSA P-256 (formato `GYMF.payload.firma`).
  `LicenseService.Resolve(licenseKey, today)` devuelve `LicenseState` (que **no** incluye `Cuit`). El emisor es
  `tools/GymForge.LicenseGen/Program.cs` (construye `new LicensePayload(Guid, gym, cuit, tier, sites, members, issued, expires)`).
  **Verificado:** `ActivateLicenseCommandHandler` (`ActivateLicenseCommand.cs:32-51`) **NO** compara `payload.Cuit`
  contra `company.TaxId` — solo chequea `Expired`/`IsPaid`, carga la empresa y persiste la clave. **No hay
  fingerprint de máquina** en el payload ni en la verificación.
- **Logs.** `src/GymForge.Desktop/Program.cs:33` y `src/GymForge.Api/Program.cs:14` usan
  `.WriteTo.File(..., rollingInterval: RollingInterval.Day)` **sin `retainedFileCountLimit`**: los diarios se
  acumulan sin tope. La API además usa ruta relativa `logs/api-.log`.
- **"Sedes: 2 de 1".** `SettingsViewModel.RefreshLicenseAsync` (`SettingsViewModel.cs:142-144`) arma
  `LicenseUsageDisplay = $"Socios: {members} de {state.MaxMembers} · Sedes: {sites} de {state.MaxSites}"`.
  Con Free (`MaxSites=1`) y 2 sedes preexistentes (demo), muestra "2 de 1". El alta de sede ya corta en
  `SettingsCommands.cs:102` (`CountActiveSitesAsync >= MaxSites`), así que el excedente solo puede venir de
  sedes creadas **antes** de que el plan bajara (downgrade o vencimiento a Free).
- **Patrón de estado local en archivo (reutilizable).** `FileMaintenanceCheckpoint`
  (`src/GymForge.Infrastructure/Services/FileMaintenanceCheckpoint.cs`) guarda estado de ESTA PC en un archivo
  de `%LOCALAPPDATA%\GymForge\`, best-effort, nunca tira. Es el molde para `ILocalSettings` (pieza 1).
- **Portabilidad.** `DataTransferService` exporta la base + logo + fotos a un `.zip` y, al importar, **reapunta
  rutas absolutas** a la PC nueva. Un destino de backup off-site (p. ej. `F:\`) es **específico de la máquina** y
  **no debe** viajar con ese export.

---

## Decisión — resumen

| # | Pieza | Qué falta | Default recomendado | UI | Tamaño |
|---|---|---|---|---|---|
| 4 | Retención de logs | `retainedFileCountLimit` | **30** archivos (≈30 días), Desktop + API | No | XS |
| 5 | Fix "Sedes: 2 de 1" | mostrar excedente sin confundir | clamp **"2 de 1 (excede el plan)"** + aviso; no tocar sedes preexistentes | Sí (chica) | XS |
| 1 | Backup off-site | copiar el último backup a destino externo | **carpeta configurable** (pendrive/red/Dropbox local); ruta en settings locales de la PC; best-effort | Sí | M |
| 3 | Licencia CUIT + fingerprint | validar CUIT al activar + atar a la máquina | CUIT **bloquea al activar**; fingerprint **avisa, no bloquea** | Sí (chica) | M |
| 2 | Auto-update Velopack | `UpdateManager` en runtime | feed **GitHub Releases** configurable; chequeo al arrancar no bloqueante; aplica con confirmación | Sí | M |

Pieza transversal nueva: **`ILocalSettings`** (preferencias de ESTA PC que no viajan con la base), introducida
en la pieza 1 y reutilizada por la pieza 2.

---

## Pieza 4 — Retención de logs

### Qué falta
Acotar la cantidad de archivos de log diarios. Hoy crecen sin tope.

### Diseño
- `src/GymForge.Desktop/Program.cs:33`: agregar `retainedFileCountLimit: 30` al `.WriteTo.File(...)`.
- `src/GymForge.Api/Program.cs:14`: ídem, y de paso apuntar a `%LOCALAPPDATA%\GymForge\logs\api-.log`
  (absoluto, consistente con el Desktop; la ruta relativa `logs/` depende del working dir).
- Opcional (recomendado, no obligatorio): `fileSizeLimitBytes: 50 * 1024 * 1024` + `rollOnFileSizeLimit: true`
  para que un día ruidoso no genere un archivo gigante. El sidecar/otros `Serilog` quedan fuera de F1.

### Decisiones
- **Default: 30 archivos** (un gimnasio con diagnósticos de ~1 mes es suficiente). Confirmable.
- No se parametriza por config: es constante en código. Simplicidad > flexibilidad innecesaria.

### Tests (TDD)
- Config-only: no hay test unitario razonable para `retainedFileCountLimit` (built-in de Serilog). La regla 6
  se cumple porque el cambio no rompe nada. Verificación manual: correr la app varios "días" y ver que no pasa
  de 30 archivos. **No se agrega test.**

---

## Pieza 5 — Fix "Sedes: 2 de 1"

### Qué falta
Mostrar el uso vs. el tope del plan sin el texto confuso "2 de 1" cuando hay más sedes/socios que el límite
(situación real tras un downgrade o vencimiento a Free con sedes preexistentes).

### Diseño
Extraer el formateo a un helper **puro y testeable** en Application y que la VM lo consuma (hoy el string se
arma inline en la VM, difícil de testear por sus muchas dependencias):

- Nuevo: `src/GymForge.Application/UseCases/Licensing/LicenseUsage.cs`
  ```csharp
  public sealed record LicenseUsageLine(string Label, int Used, int Max)
  {
      public bool OverLimit => Used > Max;
      // "Socios: 60 de 50 (excede el plan)" | "Socios: 40 de 50"
      public string Display => OverLimit ? $"{Label}: {Used} de {Max} (excede el plan)"
                                         : $"{Label}: {Used} de {Max}";
  }
  public static class LicenseUsage
  {
      public static LicenseUsageLine Members(int used, LicenseState s) => new("Socios", used, s.MaxMembers);
      public static LicenseUsageLine Sites(int used, LicenseState s)   => new("Sedes",  used, s.MaxSites);
  }
  ```
- `SettingsViewModel`: exponer dos líneas (`MembersUsage`, `SitesUsage`) con su `Display` + un bool
  `IsOverLimit` por línea para que la vista las pinte con `WarningBrush`/pill de aviso (patrón `StatusTone` de P-1).
  `LicenseUsageDisplay` (string único) puede quedar como `"{MembersUsage.Display} · {SitesUsage.Display}"`
  para no romper el binding actual, o partirse en dos `TextBlock`.
- **No** se tocan sedes preexistentes sobre el tope: desactivarlas o borrarlas sería destructivo y perdería
  historial. El alta de sede nueva ya está bloqueada (`SettingsCommands.cs:102`). Solo se visibiliza el exceso.

### Decisiones
- **Default: clamp textual "N de M (excede el plan)" + color de aviso**, no bloqueo. Para regularizar, el gimnasio
  activa una licencia superior o baja una sede a mano.
- Mismo tratamiento para socios (ej. "60 de 50"), que hoy tiene el mismo problema latente.

### Tests (TDD)
- `tests/GymForge.Application.Tests/Licensing/LicenseUsageTests.cs` (nuevo):
  - `Sites_DentroDelPlan_NoMarcaExceso` (1 de 1 → `OverLimit=false`, sin sufijo).
  - `Sites_SobreElPlan_MarcaExcesoYLoDice` (2 de 1 → `OverLimit=true`, Display contiene "excede el plan").
  - `Members_SobreElPlan_MarcaExceso` (60 de 50).
  - `Display_FormateaUsadoYMax` (texto exacto esperado).

---

## Pieza 1 — Backup off-site

### Qué falta
Copiar el último backup local a un **destino externo** (pendrive / carpeta de red / carpeta sincronizada tipo
Dropbox) para sobrevivir a que se rompa el disco de la PC de recepción. Sin credenciales de nube en v1.

### Diseño

**1. `ILocalSettings` — preferencias de ESTA PC (pieza transversal).**
- Nuevo: `src/GymForge.Application/Interfaces/ILocalSettings.cs`
  ```csharp
  /// <summary>
  /// Preferencias locales de ESTA PC: destino de backup externo, feed de updates, etc.
  /// NO viajan con la base ni con el export (son específicas de la máquina).
  /// Best-effort: leer/guardar nunca lanza.
  /// </summary>
  public interface ILocalSettings
  {
      string? OffsiteBackupDir { get; set; }
      DateTime? LastOffsiteBackupUtc { get; set; }
      string? UpdateFeedUrl { get; set; }   // lo usa la pieza 2
      void Save();
  }
  ```
- Nuevo: `src/GymForge.Infrastructure/Services/LocalSettingsService.cs` — JSON en
  `%LOCALAPPDATA%\GymForge\settings.json`; carga en el ctor (si falta/corrupto → defaults), `Save()` escribe
  atómico (tmp + `File.Move` overwrite), catch `IOException`/`UnauthorizedAccessException` → log Warning y seguir.
  Mismo molde que `FileMaintenanceCheckpoint`. Ctor con `path` opcional para tests.
- Registro: `InfrastructureServiceExtensions`, `services.AddSingleton<ILocalSettings, LocalSettingsService>();`.
  **Decisión clave:** la ruta va en **settings locales, NO en `Company`/DB** — es específica de la máquina
  (`F:\` en una PC puede no existir en otra) y no debe viajar con el `DataTransferService` export.

**2. Copia off-site dentro de `DatabaseBackupService`.**
- Ctor: agregar `ILocalSettings? settings = null` como **parámetro opcional al final** (null → off-site
  deshabilitado). Así los dos tests actuales (`new DatabaseBackupService(connStr, logger)`) y la wiring del
  `DataTransferService` siguen compilando sin cambios.
- Al final de `BackupNow(...)`, después de `Rotate()`, invocar `CopyOffsite()`:
  ```csharp
  private void CopyOffsite()
  {
      var dir = _settings?.OffsiteBackupDir;
      if (string.IsNullOrWhiteSpace(dir)) return;          // no configurado: nada que hacer
      try
      {
          var latest = LatestBackup();
          if (latest is null) return;
          var destDir = Path.Combine(dir, "GymForge-backups");
          Directory.CreateDirectory(destDir);
          File.Copy(latest, Path.Combine(destDir, Path.GetFileName(latest)), overwrite: true);
          RotateOffsite(destDir);                          // mismo MaxBackups que local
          _settings.LastOffsiteBackupUtc = DateTime.UtcNow;
          _settings.Save();
          _logger.LogInformation("Backup externo copiado a {Dir}", destDir);
      }
      catch (Exception ex)   // pendrive desenchufado, red caída, permisos...
      {
          _logger.LogWarning(ex, "No se pudo copiar el backup al destino externo {Dir}", dir);
          // best-effort: NO re-lanzar. El backup local ya quedó hecho.
      }
  }
  ```
- `RotateOffsite(destDir)`: igual que `Rotate()` pero sobre `destDir` (reutilizar la lógica de `OrderByDescending(LastWriteTime).Skip(MaxBackups)`).
- Subcarpeta `GymForge-backups` en el destino: evita ensuciar la raíz del pendrive y permite compartir el drive.

**3. UI — Configuración → Datos (sección "Copia de seguridad").**
- `SettingsViewModel`: inyectar `ILocalSettings`; exponer `OffsiteBackupDir` (display), `LastOffsiteBackupDisplay`
  ("Último resguardo externo: 28/10 14:03" o "nunca"), y un aviso si está vencido (p. ej. > 7 días o si el dir
  no existe hoy). Comandos: `PickOffsiteFolderAsync` (folder picker de Avalonia), `ClearOffsite`,
  `BackupOffsiteNowAsync` (llama `BackupNow("externo manual")`).

### Decisiones (con default)
- **Dónde va la ruta: settings locales de la PC** (no `Company`/DB). *Default recomendado.* (Ver "confirmar".)
- **Cuándo se copia:** tras cada `BackupNow`/`BackupIfDue` (arranque diario, cierre de caja, pre-import) y con el
  botón "Copiar ahora". *Default recomendado.*
- **Rotación en destino:** últimos **10** (mismo `MaxBackups` que local). Confirmable (un pendrive chico podría
  querer menos).
- **Destino no disponible:** best-effort → log Warning + el aviso en Config muestra que el resguardo externo está
  desactualizado. **Nunca** rompe una operación de negocio ni el arranque. *Default recomendado.*
- **Sin credenciales de nube en v1.** Dropbox/OneDrive se cubren apuntando a su carpeta local sincronizada.

### Tests (TDD)
- `tests/GymForge.Integration.Tests/DatabaseBackupServiceTests.cs` (ampliar):
  - `BackupNow_ConDestinoExterno_CopiaElUltimoBackup` (dir temp como off-site → aparece el archivo en `GymForge-backups`).
  - `BackupNow_RotaLasCopiasExternas_AlLimite` (crear >10 → quedan 10).
  - `BackupNow_DestinoInaccesible_NoLanzaYConservaElLocal` (dir en una unidad inexistente tipo `Z:\nope` → no tira, backup local intacto).
  - `BackupNow_SinDestinoConfigurado_NoCopiaNiFalla` (`OffsiteBackupDir=null`).
  - `BackupNow_ConExito_ActualizaLastOffsiteBackupUtc`.
- `tests/GymForge.Integration.Tests/LocalSettingsServiceTests.cs` (nuevo):
  - round-trip (set + Save + re-leer).
  - archivo inexistente → defaults (todo null).
  - JSON corrupto → defaults, no lanza.

---

## Pieza 3 — Licencia: CUIT + fingerprint de máquina

### Qué falta
(a) El CUIT de la clave **no** se valida contra `Company.TaxId` al activar. (b) La licencia **no** está atada a una
máquina: una misma clave activa infinitas PCs.

### Diseño

**(a) Validación de CUIT al activar.**
- `LicenseState` (record): agregar `string? Cuit = null` como parámetro **opcional al final** (default `null`),
  para no romper las construcciones existentes en tests (`new LicenseState(Status, Tier, Max, Max, Expires, Gym)`)
  ni su igualdad por valor. `LicenseService.Resolve` lo completa desde `payload.Cuit`.
- `ActivateLicenseCommandHandler.Handle`: tras cargar `company`, comparar CUIT **normalizado**:
  ```csharp
  if (!string.IsNullOrWhiteSpace(state.Cuit) && !CuitMatches(state.Cuit, company.TaxId))
      throw new InvalidOperationException(
          "La clave es para otro CUIT. Verificá que sea la licencia de este gimnasio.");
  ```
  `CuitMatches` normaliza ambos con el value object `Cuit` (`src/GymForge.Domain/ValueObjects/Cuit.cs`) y compara
  los 11 dígitos; si alguno no parsea como CUIT válido → **no** bloquea (log + skip), para no dejar afuera a un
  cliente por un typo del vendedor o una clave vieja sin CUIT.
- **Backward compat:** claves emitidas sin CUIT (`payload.Cuit` vacío → el asistente del LicenseGen lo permite) →
  se saltan el chequeo. Solo bloquea cuando la clave trae CUIT y difiere.
- **Por qué bloquear (y no avisar):** el CUIT se chequea **una sola vez, al activar** (no en cada arranque), así
  que bloquear no puede dejar colgado a un gimnasio en operación: solo obliga a pegar la clave correcta. Es un
  error de "clave equivocada", distinto del hardware que cambia.

**(b) Fingerprint de máquina.**
- `IMachineFingerprint` nuevo en `src/GymForge.Application/Interfaces/IMachineFingerprint.cs`:
  ```csharp
  /// <summary>Huella estable de ESTA PC para atar la licencia. Formato corto y legible
  /// (para que el gimnasio la dicte al vendedor). Nunca lanza: si no puede, devuelve "".</summary>
  public interface IMachineFingerprint { string Current(); }
  ```
- Implementación `WindowsMachineFingerprint` en `src/GymForge.Infrastructure/Services/`:
  - **Fuente estable recomendada:** `MachineGuid` del registro
    (`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`, vía `Microsoft.Win32.Registry`). Sobrevive reinicios y
    cambios de disco de datos; cambia al reinstalar Windows. Fallback: serial del volumen del disco de sistema.
  - Hash: `SHA-256(machineGuid)`, tomar 8 bytes y formatear `XXXX-XXXX-XXXX-XXXX` (hex) — corto y dictable.
  - Nunca lanza: cualquier excepción → `""` (equivale a "no se pudo obtener").
  - Registro singleton en `InfrastructureServiceExtensions`.
- `LicensePayload`: agregar `string? MachineId = null` como **parámetro opcional al final**. Claves viejas (JSON de
  8 campos) deserializan con `MachineId = null` → "no atada". Claves nuevas pueden traerlo.
- `LicenseState`: agregar `bool MachineMismatch = false` (opcional al final).
- `LicenseService`: inyectar `IMachineFingerprint? fingerprint = null` (null = tooling/tests que no chequean
  máquina). En `Resolve`, tras decodificar:
  ```csharp
  var current = _fingerprint?.Current();
  var mismatch = !string.IsNullOrEmpty(payload.MachineId)
              && !string.IsNullOrEmpty(current)
              && !string.Equals(payload.MachineId, current, StringComparison.OrdinalIgnoreCase);
  ```
  `mismatch` se propaga a `LicenseState.MachineMismatch`. **El estado sigue siendo `Active`/`Grace`** (no se
  degrada a Free por mismatch): **avisar, no bloquear**.
- **Emisor (`tools/GymForge.LicenseGen`):** agregar opción `--machine <código>` (y pregunta opcional en el
  asistente, "enter para no atar"). Si se omite → `MachineId = null` (clave portable, comportamiento actual).
  El flujo de atado es: el gimnasio copia su "Código de esta PC" desde Config → Licencia y se lo pasa al vendedor;
  el vendedor emite con `--machine`. *Para el piloto, el default es emitir SIN atar* (claves portables) y habilitar
  el atado más adelante; así el fingerprint entra como infraestructura sin arriesgar la puesta en marcha.

**UI — Configuración → Licencia.**
- Mostrar "Código de esta PC: XXXX-XXXX-XXXX-XXXX" (read-only + copiar), para dictárselo al vendedor.
- Si `_license.State.MachineMismatch` → `InfoBar` Warning: "Esta licencia fue emitida para otra PC. Seguís
  operando, pero avisá al proveedor para re-emitirla." (no bloquea).

### Decisiones (con default)
- **CUIT: bloquea al activar** (default). Mismatch imposible de parsear → no bloquea.
- **Fingerprint: avisa, no bloquea, sin auto-degradar** en v1 (default). Un cambio de hardware no debe dejar al
  gimnasio afuera. (La "gracia" acá es permanente: siempre opera; solo avisa.) Confirmable si se quiere una
  ventana de gracia que luego degrade.
- **Fuente del fingerprint: `MachineGuid` del registro** con fallback a serial de volumen (default).
- **Firma ECDSA:** se respeta tal cual. `MachineId`/`Cuit` van **dentro del payload firmado** (no se pueden alterar
  sin invalidar la firma). No cambia el esquema `GYMF.payload.firma`.
- **Compatibilidad de la clave pública embebida:** este cambio **no** toca la clave privada ni la pública; solo
  agrega campos al payload. Las claves ya emitidas siguen verificando (campos nuevos = null).

### Tests (TDD)
- `tests/GymForge.Application.Tests/Licensing/LicenseCodecTests.cs` (ampliar o crear):
  - `RoundTrip_ConMachineId_SePreserva`.
  - `Decode_ClaveSinMachineId_DevuelveNull` (simular JSON viejo de 8 campos → `MachineId==null`). *(Backward compat.)*
- `tests/GymForge.Application.Tests/Licensing/LicenseServiceTests.cs` (ampliar):
  - `Resolve_MachineIdCoincide_SinMismatch`.
  - `Resolve_MachineIdDistinto_MarcaMismatchPeroSigueActive` (no degrada a Free).
  - `Resolve_PayloadSinMachineId_NuncaMarcaMismatch` (aunque haya fingerprint actual).
  - `Resolve_SinFingerprintInyectado_NuncaMarcaMismatch` (tooling/tests).
  - Usar un `IMachineFingerprint` fake + firmar con una clave de test (patrón ya usado: `new LicenseService(publicKeySpki)`).
- `tests/GymForge.Application.Tests/Handlers/ActivateLicenseCommandTests.cs` (ampliar):
  - `Handle_CuitCoincide_Activa`.
  - `Handle_CuitDistinto_RechazaConMensajeClaro` (no persiste).
  - `Handle_ClaveSinCuit_Activa` (backward compat).
  - `Handle_CuitConFormatoDistintoPeroMismoNumero_Activa` ("30712345678" vs "30-71234567-8").

---

## Pieza 2 — Auto-update Velopack

### Qué falta
Chequear/aplicar actualizaciones en runtime con `UpdateManager` contra un feed. Hoy solo están los hooks de
install/update (`VelopackApp.Build().Run()`).

### Diseño
- Wrapper testeable: `IUpdateService` en `src/GymForge.Desktop/Services/` (Velopack solo lo referencia Desktop):
  ```csharp
  public interface IUpdateService
  {
      /// <summary>Best-effort: null si no hay update, no está instalado por Velopack, o el feed no existe aún.</summary>
      Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);
      Task DownloadAsync(UpdateInfo update, CancellationToken ct = default);
      void ApplyAndRestart(UpdateInfo update);   // mgr.WaitExitThenApplyUpdates(update)
  }
  ```
  Implementación `VelopackUpdateService`:
  - Feed por defecto en constante `DefaultFeedUrl = "https://github.com/ManuelHidalgo26/GymForge"`, override por
    `ILocalSettings.UpdateFeedUrl` (reutiliza la pieza 1).
  - `var mgr = new UpdateManager(new GithubSource(feedUrl, accessToken: null, prerelease: false));`
  - **Guard clave:** `if (!mgr.IsInstalled) return null;` → en `dotnet run`/exe portátil Velopack no gestiona la
    instalación y `CheckForUpdatesAsync` tiraría. Así el dev no se rompe.
  - Todo envuelto en try/catch → si el feed **todavía no existe** (sin releases) o no hay red, loguea y devuelve
    null. **No rompe el arranque.**
- Arranque (`Program.cs`): tras `BuildAvaloniaApp().Start...`, el chequeo corre en **background, no bloqueante**
  (p. ej. un `Task.Run` disparado por el shell una vez que la UI está viva). No se hace en el camino crítico del
  startup.
- UX mínima: `MainWindowViewModel` expone `IsUpdateReady` + `UpdateVersion`; cuando hay update **descargado**,
  un `InfoBar`/banner discreto: "Hay una actualización lista (vX.Y.Z) — Reiniciar ahora / Después". "Reiniciar
  ahora" → `ApplyAndRestart`. "Después" → se aplica sola en el próximo cierre/reinicio.
- Config → (sección "Actualizaciones" o "Acerca de"): versión actual, botón "Buscar actualizaciones", y el feed
  URL (avanzado, normalmente oculto/solo lectura).

### Decisiones (con default)
- **Feed: GitHub Releases** `ManuelHidalgo26/GymForge` (default, de `pack.ps1`). Override por `ILocalSettings`.
- **Descarga automática y silenciosa; aplicación con confirmación** (default). No reiniciar de sorpresa una PC de
  recepción en medio de un cobro/check-in. Si el usuario ignora el aviso, se aplica en el próximo reinicio normal.
- **Chequeo al arrancar, 1 vez, en background best-effort.** (Opcional a futuro: re-chequear cada N horas; fuera de F1.)
- **Tolerante a feed inexistente:** mientras no haya releases publicados, `CheckAsync` devuelve null sin ruido.

### Tests (TDD)
- Velopack `UpdateManager` real necesita app instalada + feed: no se testea en unit. Se testea la **lógica del VM**
  contra un `IUpdateService` fake:
  - `tests/GymForge.Desktop.Tests/UpdateBannerTests.cs` (nuevo): `CheckAsync` devuelve update → `IsUpdateReady=true`
    y `UpdateVersion` seteada; devuelve null → banner oculto; "Reiniciar ahora" invoca `ApplyAndRestart`;
    excepción del service → banner oculto, sin romper.
- El `VelopackUpdateService` real se verifica manualmente una vez que exista el primer release (humo: publicar
  v0.3.0, instalar, publicar v0.3.1, confirmar que aparece el banner).

---

## Orden de implementación

Primero las dos chicas y seguras, después las medianas, auto-update al final por su dependencia del feed:

1. **Pieza 4 — retención de logs.** Trivial, sin riesgo, sin tests nuevos.
2. **Pieza 5 — fix "Sedes: 2 de 1".** Chica; introduce `LicenseUsage` (helper puro) + tests baratos. UI mínima.
3. **Pieza 1 — backup off-site.** Introduce `ILocalSettings` (reutilizado luego por la pieza 2). UI en Config → Datos.
4. **Pieza 3 — licencia CUIT + fingerprint.** CUIT (bloquea al activar) + fingerprint (avisa). Toca el emisor `LicenseGen`.
5. **Pieza 2 — auto-update Velopack.** Último: depende del feed y reutiliza `ILocalSettings`. Tolerante a que el feed aún no exista.

Commits sugeridos (uno por pieza, regla 7): `chore: [logs] retener 30 archivos`, `fix: [licencia] mostrar
excedente de sedes/socios`, `feat: [backup] copia off-site a carpeta configurable`, `feat: [licencia] validar CUIT
y atar a la máquina`, `feat: [update] auto-actualización Velopack desde GitHub Releases`. `dotnet test` verde antes
de cada uno.

## Piezas que tocan UI (entra `ux`)
- **Pieza 1 (backup off-site):** Config → Datos — folder picker del destino + estado "último resguardo externo" + botón "Copiar ahora".
- **Pieza 3 (licencia):** Config → Licencia — "Código de esta PC" (read-only + copiar) + `InfoBar` de mismatch.
- **Pieza 5 (sedes):** Config → Licencia — texto de uso con color de aviso al exceder.
- **Pieza 2 (update):** banner/`InfoBar` de "actualización lista" en el shell + versión/botón en Config.
- **Pieza 4 (logs):** sin UI.

## Decisiones a confirmar con el usuario
1. **Destino de backup off-site:** ¿pendrive/carpeta de red/Dropbox local alcanza para el piloto, o hace falta
   nube con credenciales (fuera de v1)? ¿Rotación de 10 en destino o menos para pendrives chicos?
   ¿La ruta va en settings locales (recomendado) o preferís que viaje con la empresa (DB)?
2. **Repo del feed de updates:** confirmar `https://github.com/ManuelHidalgo26/GymForge` y si el release es
   público (sin token) o privado (requiere token en el feed).
3. **Estrictez del fingerprint:** confirmar "avisar, no bloquear, sin auto-degradar" para el piloto. ¿Emitir claves
   SIN atar por ahora (portables) y atar más adelante? ¿CUIT debe bloquear al activar (recomendado) o solo avisar?

---

## Consecuencias
- Nueva abstracción `ILocalSettings` (settings de máquina) separada de `Company`/DB: queda disponible para futuras
  preferencias locales. Se documenta que **no** viaja con el export de `DataTransferService`.
- `LicensePayload`/`LicenseState` crecen con campos opcionales → sin ruptura de claves emitidas ni de tests
  existentes (defaults). El `LicenseGen` cambia (nueva opción `--machine`): queda registrado para no sorprender.
- Auto-update acopla `GymForge.Desktop` a Velopack en runtime (ya referenciado); el resto de las capas no se entera.
- Deuda/alcance explícito **fuera de F1:** licenciamiento v2 (validación/revocación online + Mercado Pago),
  re-chequeo periódico de updates, backup a nube con credenciales, firma de código v2 (Azure Trusted Signing).

---

## Referencias
- Backup/recuperación: `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs`
- Licencia: `src/GymForge.Application/UseCases/Licensing/` · emisor `tools/GymForge.LicenseGen/Program.cs`
- Velopack pack: `scripts/pack.ps1` · hooks `src/GymForge.Desktop/Program.cs:13`
- Patrón de estado local: `src/GymForge.Infrastructure/Services/FileMaintenanceCheckpoint.cs`
- Roadmap/estado: `docs/PROGRESS.md` · `docs/TASKS.md` (F1)
