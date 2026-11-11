# ADR-015 — Canal de releases y auto-update: GitHub Releases (repo público) + CI por tag `v*`

**Status:** Accepted
**Date:** 2026-11-11
**Deciders:** **usuario** decidió *canal = GitHub Releases sobre el MISMO repo público `ManuelHidalgo26/GymForge`, Velopack con `accessToken: null` (sin token embebido), publicación automatizada por push de un tag `v*` con GitHub Actions* · **arqui** diseña el flujo y el workflow · implementa **dev** · veta **qa**
**Sesión:** Montaje del canal de releases que **extiende ADR-004 (pieza 2, auto-update Velopack)** y **cierra la parte diferida de ADR-014** (`BuildDefaultUrl` VACÍA "hasta montar el canal"). Reusa el `UpdateFeed`/`VelopackUpdateService` de ADR-014 sin tocar su política (https obligatorio, mask de logs, `accessToken: null`).
**Piso:** no bajar los tests verdes vigentes (1676). Este ADR es casi todo **operación** (workflow + scripts + docs); el único cambio de código de la app es **1 línea** (`BuildDefaultUrl`), que **sí** mueve 2 tests de `VelopackUpdateService` (ver §7/§Tests). Sin migración EF.

---

## Context

### Qué quedó diferido en ADR-014 (y este ADR resuelve)

ADR-014 dejó el origen de actualizaciones **configurable tras PIN**, con `https` obligatorio en dos lados y
mask de logs, pero con el default de fábrica **VACÍO a propósito**:

> "`BuildDefaultUrl` queda `""` hasta montar el canal de releases — sin inventar URL ni embeber token, se
> recomienda repo público sin token / host propio / UNC." (ADR-014 §1 y Consecuencias)

Con `BuildDefaultUrl = ""`, el auto-update está **decidido pero INACTIVO en campo**: una instalación nueva no
tiene a dónde consultar hasta que alguien cargue un origen a mano. Lo que faltaba no era una decisión de
política sino **operación de distribución**: dónde viven los releases y cómo se publican. `docs/PROGRESS.md`
(ítems 67/181) y `docs/TASKS.md` ("Deuda de F1", condición de uso 2) lo tienen anotado explícitamente como
*"montar el canal de releases"*.

### Hechos ya decididos por el usuario (no se re-litigan en este ADR)

- **Repo `ManuelHidalgo26/GymForge` es PÚBLICO** → canal = **GitHub Releases sobre el MISMO repo público**,
  Velopack con `accessToken: null`. **Sin token embebido en el cliente.** Ningún secreto va a la app.
- `BuildDefaultUrl` se completa con `https://github.com/ManuelHidalgo26/GymForge`.
- La publicación se automatiza con **GitHub Actions disparado por push de un tag `v*`**. El `GITHUB_TOKEN` del
  Actions es server-side (no se embebe). `vpk upload github` manual desde la PC del dev queda como fallback.

### Hechos del código/infra verificados al 2026-11-11 (no asumidos)

- **El cliente ya llama sin token.** `VelopackUpdateService.CreateManager` arma
  `new GithubSource(feedUrl, accessToken: null, prerelease: false)` para hosts `*.github.com`
  (`VelopackUpdateService.cs:112-119`). `CheckAsync` resuelve el origen con `UpdateFeed.Resolve(_settings)`,
  rechaza no-https con `FeedInsecure` y enmascara la URL en los logs con `UpdateFeed.ForLog` (ADR-014). **Nada
  de esto cambia.**
- **`BuildDefaultUrl` es el único punto a tocar en la app:** `UpdateFeed.cs:11` (`public const string
  BuildDefaultUrl = "";`). `Resolve` ya hace `override ?? BuildDefaultUrl ?? null` (`UpdateFeed.cs:18-24`).
- **El auto-update solo corre en instalaciones Velopack.** `CheckFeedAsync` corta con
  `if (!VelopackLocator.IsCurrentSet) return NotInstalled;` y `if (!manager.IsInstalled) return NotInstalled;`
  (`VelopackUpdateService.cs:96-99`). Un `dotnet run`, y **el exe único portable de `publish.ps1`
  (`dist/GymForge.exe`)**, **no** están instalados por Velopack → **no se auto-actualizan** (devuelven
  `NotInstalled`, sin red). El canal sirve a lo que se instaló con el **`Setup.exe` de `pack.ps1`**.
- **`pack.ps1` ya produce el artefacto correcto para Velopack:** publica self-contained **sin** single-file
  (`-p:PublishSingleFile=false`, porque Velopack empaqueta el **directorio**, no un exe único), luego
  `vpk pack --packId GymForge --packVersion <v> --packDir publish --mainExe GymForge.exe --icon ... --outputDir
  releases` → deja en `releases\`: `GymForge-win-Setup.exe`, el `.nupkg` full y el manifiesto `RELEASES`
  (`pack.ps1:26-64`). Hoy su default `-Version` es **`0.2.0` (stale)**; el `.csproj` ya está en **`0.3.0`**
  (`GymForge.Desktop.csproj:23`). **`pack.ps1` NO pasa `-p:Version` al `dotnet publish`:** el ensamblado toma
  la versión del `.csproj`, no la de `-Version` (que solo va a `vpk pack`). Es una deriva a corregir (§1/§7).
- **`publish.ps1` es OTRO artefacto:** el exe único portable (`dist\GymForge.exe`) para pendrive/prueba. **No**
  entra en el canal de auto-update (ver arriba). No se toca salvo documentación.
- **Firma de código es opt-in por env var** (`GYMFORGE_SIGN_THUMBPRINT` / `GYMFORGE_SIGN_PFX`), con certificado
  **autofirmado** que vive en el store del dev / `%LOCALAPPDATA%\GymForge\vendor\` (NO en el repo, NO en
  Actions). Sin esas variables, `pack.ps1`/`publish.ps1` empaquetan **sin firmar** y avisan (`pack.ps1:32-47`,
  `publish.ps1:27-39`). `docs/CODE-SIGNING.md` y `docs/DISTRIBUCION.md`.
- **CI ya existe y es hermano del de release:** `.github/workflows/ci.yml` corre `restore → build → test` en
  `windows-latest` (obligatorio: Desktop y sidecars son `net9.0-windows`) en push a `main`/`master` y en PRs.
- **Velopack (runtime) está pineado en `1.2.0`** (`Directory.Packages.props:89`). La CLI `vpk` debe ir alineada.

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Tests antes de publicar (regla 6):** el workflow de release **corre `dotnet test` como gate** antes de
  empaquetar/subir. Si los tests fallan, **no** se publica release.
- **Offline-first / best-effort:** el canal es infraestructura externa; el cliente ya tolera feed inexistente,
  sin red o respuesta inválida (`Failed`, nunca rompe el arranque). Montar el canal no cambia eso.
- **Simplicidad > cleverness:** se reusa `pack.ps1` (ya probado) en el workflow; no se reescribe el empaquetado.
  Se difiere la optimización de **deltas** (que exige un paso extra) hasta que haga falta.

---

## Decisión

Numeración de piezas (estilo ADR-004/ADR-014):

### 1) Flujo de release de punta a punta — SemVer, el **tag** manda, `vpk pack` + `vpk upload github`

**Versionado: SemVer; la fuente de verdad es el TAG, no el `.csproj`.** Convención de tag: `vMAJOR.MINOR.PATCH`
(p. ej. `v0.3.0`, `v0.4.1`). El workflow deriva `ver = tag.TrimStart('v')` y lo usa para **ambos**:

1. **La versión del ensamblado**, pasando `-p:Version=$ver` al `dotnet publish` (hoy `pack.ps1` NO lo hace; se
   agrega — §7). Así `VelopackUpdateService.CurrentVersion` (lee `AssemblyInformationalVersion`,
   `VelopackUpdateService.cs:131-137`) coincide con la versión publicada.
2. **`vpk pack --packVersion $ver`** (ya lo recibe vía `pack.ps1 -Version`).

**Por qué el tag y no el `.csproj`:** evita la deriva actual (`.csproj` 0.3.0 vs. `pack.ps1` default 0.2.0). El
`<Version>` del `.csproj` queda como default para correr/compilar local (`dotnet run`); **en un release
etiquetado, el tag gobierna** el número del ensamblado y del paquete. Dev mantiene el `.csproj` razonablemente
en sync para las corridas locales, pero el release no depende de eso. El workflow **valida el formato del tag**
(regex SemVer) y aborta si no matchea (evita publicar un `v-final` sin querer).

**Qué produce `vpk pack`** (en `releases\`, vía `pack.ps1`):
- `GymForge-win-Setup.exe` — instalador (acceso directo + menú inicio).
- `GymForge-<ver>-full.nupkg` — paquete completo de la versión.
- `RELEASES` / `releases.win.json` — el **manifiesto del feed** que `GithubSource` consulta para saber si hay
  versión nueva y validar el SHA del paquete.
- (Opcional, hoy **fuera de alcance**) `*-delta.nupkg` — incrementales; requieren un paso previo (§6).

**Qué hace `vpk upload github`:** crea/asocia el **GitHub Release del tag** y **sube esos assets** usando el
`GITHUB_TOKEN`. Como `GithubSource(..., prerelease: false)` solo sirve el **último release publicado y no
prerelease**, la decisión es:

- **El workflow publica un release FINAL, publicado y no-prerelease** (`vpk upload github ... --publish`).
  Fundamento: **pushear un tag `v*` ya es el acto deliberado de "esto es un release"**, y el **gate de tests**
  (§2) protege la calidad. No hay motivo para dejarlo en draft en el camino feliz.
- **Primera corrida cautelosa (recomendado una sola vez):** correr `vpk upload github` **sin** `--publish` →
  crea el release como **draft** para inspeccionar los assets en la UI de GitHub y luego publicarlo a mano
  (o borrarlo). `GithubSource(prerelease:false)` **ignora drafts**, así que un draft no dispara updates en
  campo: es un dry-run seguro.
- **Prerelease (`v0.4.0-beta.1`):** el cliente usa `prerelease: false`, así que **no** se entregarían a los
  gimnasios aunque se publiquen marcados como pre. Quedan **fuera de alcance v1** (no se diseñan canales beta
  acá); el workflow puede publicarlos marcados `--pre` para uso interno, pero no llegan al piloto.

### 2) Workflow `.github/workflows/release.yml` — trigger por tag, tests como gate, sin secretos en la app

Nuevo workflow **hermano de `ci.yml`**, `windows-latest` (mismo motivo: `net9.0-windows`). Orden:
`checkout → setup .NET 9 → derivar versión del tag → restore → build → **test (GATE)** → instalar vpk pineado
→ pack (reusa `pack.ps1`) → upload a GitHub Releases`. Permisos mínimos `contents: write` (crear release + subir
assets). El `GITHUB_TOKEN` es el **del Actions (server-side)**; se pasa a `vpk upload github --token` y **nunca**
se escribe en un asset ni llega al binario.

```yaml
name: Release

on:
  push:
    tags: ['v*']          # solo tags vMAJOR.MINOR.PATCH disparan publicación

permissions:
  contents: write          # crear el GitHub Release y subir assets con GITHUB_TOKEN

jobs:
  release:
    runs-on: windows-latest   # Desktop + sidecars son net9.0-windows (igual que ci.yml)
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 9
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Versión desde el tag
        id: ver
        shell: pwsh
        run: |
          $v = "${{ github.ref_name }}".TrimStart('v')
          if ($v -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
            throw "Tag inválido '${{ github.ref_name }}': se espera vMAJOR.MINOR.PATCH"
          }
          "version=$v" >> $env:GITHUB_OUTPUT

      - name: Restore
        run: dotnet restore GymForge.sln

      - name: Build
        run: dotnet build GymForge.sln -c Release --no-restore

      - name: Test (GATE — no se publica si falla)
        run: dotnet test GymForge.sln -c Release --no-build --verbosity normal

      - name: Instalar Velopack CLI (alineada al runtime)
        shell: pwsh
        run: |
          dotnet tool install -g vpk --version 1.2.0   # = Velopack en Directory.Packages.props
          "$env:USERPROFILE\.dotnet\tools" >> $env:GITHUB_PATH

      - name: Empaquetar (reusa scripts/pack.ps1)
        shell: pwsh
        run: .\scripts\pack.ps1 -Version ${{ steps.ver.outputs.version }}

      - name: Publicar en GitHub Releases
        shell: pwsh
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          vpk upload github `
            --repoUrl https://github.com/ManuelHidalgo26/GymForge `
            --token $env:GITHUB_TOKEN `
            --tag ${{ github.ref_name }} `
            --releaseName "GymForge ${{ steps.ver.outputs.version }}" `
            --publish
```

Notas de diseño:
- **Tests como gate: SÍ** (CLAUDE.md regla 6). El `build` compila la solución (incl. tests) para que
  `dotnet test --no-build` corra; luego `pack.ps1` hace su propio `dotnet publish -c Release` del Desktop (una
  recompilación acotada del ejecutable, aceptable). Si el `test` falla, el job se corta antes de pack/upload.
- **`vpk` pineado a `1.2.0`** (= `Velopack` del `Directory.Packages.props`): alinear CLI y runtime evita
  incompatibilidades de formato del feed.
- **Reusar `pack.ps1`** mantiene una sola fuente de verdad del empaquetado (packId, mainExe, icono, signParams).
  Como en CI **no** están seteadas las env vars de firma, `pack.ps1` empaqueta **sin firmar** y avisa (§4).
- **`GITHUB_TOKEN`** nunca se imprime ni se guarda; GitHub lo enmascara en los logs del runner. No hay token en
  la app (eso es §5).

### 3) Activación de `BuildDefaultUrl` — 1 línea, el resto de la política de ADR-014 intacto

`src/GymForge.Desktop/Services/UpdateFeed.cs:11`:

```csharp
// antes:  public const string BuildDefaultUrl = "";
public const string BuildDefaultUrl = "https://github.com/ManuelHidalgo26/GymForge";
```

Confirmaciones (todo esto **ya existe**, no se toca):
- `https://github.com/...` **pasa** `IsAllowedSource` (es https con host) → no es `FeedInsecure`.
- El **rechazo de `http://`** (UX11/F1-D7) sigue aplicando al guardar en Config y en `CheckAsync` (defensa en
  los dos lados).
- La **mask de logs** (`UpdateFeed.ForLog`, NEW-62) sigue aplicando: un feed solo se loguea como esquema+host.
- La **precedencia** no cambia: `override (settings.json) ?? BuildDefaultUrl ?? null`. Soporte puede seguir
  apuntando **una** PC a otro feed (staging/UNC) por Config tras PIN, sin recompilar.
- `GithubSource(..., accessToken: null, prerelease: false)` **no cambia** (repo público, sin token).

**Efecto:** toda instalación Velopack **nueva** (build con esta constante) consulta el repo público al arrancar,
en background y best-effort. Sin releases todavía → `Failed` silencioso (un log enmascarado); con releases →
auto-update. `dotnet run` y el exe portable siguen devolviendo `NotInstalled` (no salen a la red).

### 4) Firma de código en CI — el release automatizado sale **SIN firmar** (y está bien, por ahora)

**Decisión: el release de CI NO se firma en el runner.** El certificado autofirmado (su `.pfx`/thumbprint) vive
en la máquina del dev y en `%LOCALAPPDATA%\GymForge\vendor\`; **no** está en GitHub Actions y **no** se sube como
secret en v1. Como `pack.ps1` firma solo si `GYMFORGE_SIGN_THUMBPRINT`/`GYMFORGE_SIGN_PFX` están seteadas
(`pack.ps1:32-47`), en CI empaqueta **sin firmar**.

**Trade-off (explícito y aceptado):**
- La firma autofirmada **solo** quita el cartel "Editor desconocido" en PCs donde se instaló el `.cer` a mano
  (`trust-cert.ps1`), y **igual** SmartScreen puede avisar al bajar del navegador (reputación en la nube, no
  conoce el cert) — ya documentado en `docs/CODE-SIGNING.md`/`docs/DISTRIBUCION.md`. O sea: firmar el release de
  CI **no** elimina el aviso en la PC de un gimnasio que descarga de GitHub. Aporta poco y mete un secreto en CI.
- **Lo que SÍ protege al auto-update no es Authenticade sino Velopack:** `GithubSource` valida el **SHA del
  paquete** contra el `RELEASES` del mismo origen https (§5). Un instalador sin firmar instalado por Velopack
  sigue teniendo integridad verificada por el canal.
- **Camino firmado = pack local**, cuando importa (pendrive del piloto): `GYMFORGE_SIGN_THUMBPRINT=<huella>` +
  `pack.ps1` + `make-usb.ps1` → el `Setup.exe` del pendrive aparece como "GymForge" en las PCs con el `.cer`
  instalado. Ese flujo no cambia.
- **Futuro (fuera de este ADR):** si se quiere CI firmado, (a) subir el `.pfx` como **secret** de Actions y usar
  `GYMFORGE_SIGN_PFX`/`GYMFORGE_SIGN_PFX_PASS` (aún autofirmado: SmartScreen sigue avisando en PCs ajenas), o
  (b) **Azure Trusted Signing** (CODE-SIGNING.md v2), que sí saca el aviso sin instalar nada. Es la decisión
  real de firma; este ADR no la fuerza.

### 5) Seguridad — sin tokens, feed https, superficie de ataque, PCs del piloto

- **Cero secretos en la app.** Repo público + `accessToken: null`: no hay token que extraer del exe. El único
  secreto del pipeline es el `GITHUB_TOKEN` **del Actions** (server-side, efímero, con `contents: write`), que
  **nunca** toca el binario ni un asset. (Esto es exactamente lo que ADR-014 §1 exigía: "NUNCA incrustar un
  token"). NEW-62 ya enmascara la URL en logs del cliente; el `GITHUB_TOKEN` lo enmascara GitHub en el runner.
- **Feed https de punta a punta.** `BuildDefaultUrl` es https; `IsAllowedSource` rechaza `http`; `GithubSource`
  descarga por https de `*.github.com` / `objects.githubusercontent.com`.
- **Superficie de ataque del auto-update.** Velopack verifica el **SHA256 de cada paquete** contra el `RELEASES`
  servido por el **mismo** origen https: sin romper TLS **y** controlar el repo, no se puede inyectar un paquete.
  El vector MITM=RCE que ADR-014 cerró (exigir https) sigue cerrado. Límite aceptado heredado (**NEW-64**): un
  `https` arbitrario cargado por soporte tras PIN es un origen de código válido; endurecerlo (firma de paquetes
  / allowlist de hosts) queda como deuda futura, no entra acá.
- **PCs del piloto que YA tienen un build con `BuildDefaultUrl` vacía: NO se auto-actualizan** hasta recibir un
  build nuevo (con la URL compilada) **instalado por `Setup.exe`**. La constante se hornea en el exe; un binario
  viejo no la tiene. **Por eso la activación en campo es:** (1) publicar/entregar un build con esta constante
  (por pendrive o descarga) e instalarlo sobre el piloto; (2) **a partir de ahí**, los tags siguientes
  (`v0.4.1`, …) llegan solos. Alternativa sin reinstalar: cargar el origen en Config → Actualizaciones tras PIN
  (ADR-014 opción B) — útil para destrabar una PC puntual sin esperar el build nuevo. **Se documenta en la nota
  de release.**
- **El exe portable (`dist/GymForge.exe`) nunca se auto-actualiza** (no es install Velopack): quien lo use se
  actualiza corriendo el exe nuevo. Documentado en `docs/DISTRIBUCION.md`.

### 6) Qué NO entra (alcance cerrado)

- **Landing / descarga web como estrategia de largo plazo.** v1 = GitHub Releases del repo público. Una landing
  con dominio propio, instalador "bonito" y métricas es un paso posterior; `vpk upload github` sigue siendo el
  mecanismo. (CLAUDE.md "Distribución web: GitHub Releases primero; landing después" — se hace lo primero.)
- **Paquetes delta (incrementales).** v1 publica **solo paquetes full**. Habilitar deltas requiere, antes de
  `vpk pack`, un `vpk download github --repoUrl ... --token ...` para traer el release anterior y computar el
  diff — paso extra que acopla el workflow a releases previos y obliga a partir `pack.ps1`. Para un gimnasio que
  actualiza de vez en cuando, bajar el full (~decenas de MB) es aceptable. **Deuda futura documentada.**
- **Release de CI firmado** (requiere subir el `.pfx` como secret) y **firma v2** (Azure Trusted Signing) — §4.
- **Canales beta/prerelease hacia el piloto** — el cliente usa `prerelease:false` (§1).
- **Licenciamiento v2 online / Mercado Pago, re-chequeo periódico de updates** — ya fuera de F1 (ADR-004).
- **Pushear el PRIMER tag `v*`** (activar el canal de verdad): lo hace **el usuario**. Este ADR **monta** el
  mecanismo; la activación es un acto deliberado del usuario (como en ADR-014, "activar es deliberado").

### 7) Plan de implementación para dev (ordenado)

1. **`.github/workflows/release.yml` (NUEVO).** El workflow de §2, tal cual. Permisos `contents: write`, trigger
   `tags: ['v*']`, tests como gate, `vpk` pineado a `1.2.0`, reusa `pack.ps1`, `vpk upload github --publish`.
2. **`scripts/pack.ps1` (AJUSTE).** Dos cambios para alinear con `vpk`/el tag:
   - Pasar la versión al `dotnet publish` para que el **ensamblado** coincida con el paquete:
     agregar `-p:Version=$Version -p:FileVersion="$Version.0"` (y, si se quiere, `-p:InformationalVersion=$Version`)
     al `dotnet publish` de `pack.ps1:26-29`. Hoy solo va a `vpk pack`, así que el exe quedaría con la versión del
     `.csproj` y no la del tag.
   - Default de `-Version` **stale** (`0.2.0`): corregir a `0.3.0` o, mejor, hacer que lea el `<Version>` del
     `.csproj` cuando no se pasa. Menor (el workflow siempre pasa el tag), pero evita sorpresas en pack local.
3. **`src/GymForge.Desktop/Services/UpdateFeed.cs` (1 LÍNEA).** `BuildDefaultUrl =
   "https://github.com/ManuelHidalgo26/GymForge"` (§3).
4. **`tests/GymForge.Desktop.Tests/VelopackUpdateServiceTests.cs` (AJUSTE OBLIGATORIO).** Al dejar de ser vacía
   la constante, **sin override ya NO resuelve a "sin configurar"**, sino al default de GitHub. Rompe dos casos:
   - `Check_SinFeedConfigurado_EsNoOp` (null/""/"   " → esperaba `FeedNotConfigured`): ahora, sin override,
     `Resolve` devuelve el default de fábrica y `CheckAsync` sigue hasta `NotInstalled` (el host de tests no es
     install Velopack). Renombrar a algo tipo *"sin override cae al default de fábrica"* y esperar `NotInstalled`.
   - `Check_LeeElFeedEnCadaConsulta_ParaTomarCambiosDeConfiguracion` (primera aserción con feed null esperaba
     `FeedNotConfigured`): ídem → `NotInstalled`.
   - El camino puro `FeedNotConfigured` (override **y** default ambos vacíos) ya **no es alcanzable por el
     servicio** (la const es no-vacía); queda cubierto a nivel `Resolve` en `UpdateFeedTests` (que pasa
     `buildDefault` explícito — esos tests **no** se tocan). Opcional: cubrir NEW-65 (variantes `HTTP://` y con
     espacios) de paso.
5. **`docs/DISTRIBUCION.md` (DOC).** Reescribir la sección "Actualizaciones": el auto-update **ya está activo**
   contra GitHub Releases; publicar = pushear un tag `v*` (CI hace pack+upload). Dejar claro: (a) el canal sirve
   a installs de `Setup.exe`, **no** al exe portable; (b) el piloto con builds viejos (URL vacía) no se
   actualiza hasta recibir un build nuevo o cargar el origen en Config tras PIN; (c) `vpk upload github` manual
   sigue siendo el fallback. Actualizar el comando a `--tag v<ver>` consistente con el workflow.
6. **`docs/CODE-SIGNING.md` (DOC, opcional).** Una línea: "el release automatizado por CI sale **sin firmar**;
   el pack firmado es el flujo local para el pendrive del piloto" (§4).

**Commit sugerido (uno, regla 7):** `feat: [release] canal de releases GitHub + CI por tag v* (ADR-015)`.

---

## Tests / verificación

**Automáticos (gate del workflow):** `dotnet test GymForge.sln` **verde** en `windows-latest` antes de
pack/upload. El cambio de código (1 línea + ajuste de 2 tests) **debe dejar la suite verde** (piso 1676).

**Impacto conocido (no sorpresa):** el único cambio de comportamiento testeable es el de §7.4
(`VelopackUpdateServiceTests`: sin override, ahora se resuelve el default de fábrica). `UpdateFeedTests` queda
**intacto** (sus `Resolve` pasan `buildDefault` explícito; sus `IsAllowedSource`/`ForLog` no dependen de la
const). `DesktopWiringTests` (que `IUpdateService` es `VelopackUpdateService`) no se ve afectado.

**Validación del workflow (una vez, sin arriesgar el piloto):**
- **Lint/YAML:** push del workflow a una rama; que GitHub lo acepte (no dispara: el trigger es por tag).
- **Dry-run de publicación:** correr `vpk upload github` **sin** `--publish` (o con un tag de prueba
  `v0.3.0-rc.1`) → crea un **draft**/prerelease que `GithubSource(prerelease:false)` **ignora**; inspeccionar que
  subieron `Setup.exe` + `*-full.nupkg` + `RELEASES`; borrarlo. No afecta a ninguna PC en campo.
- **Humo end-to-end (manual, lo hace el usuario al activar):** (1) build con `BuildDefaultUrl` seteada, instalar
  con `Setup.exe` (vN); (2) push de tag `vN+1` → el workflow publica; (3) abrir la app instalada → aparece el
  banner "actualización lista (vN+1)"; "Reiniciar ahora" la aplica. Confirmar también que un **override** por
  Config a otro feed **pisa** el default.

---

## Consecuencias

- **Canal de releases montado:** publicar = `git tag vX.Y.Z && git push --tags`. CI compila, **corre tests como
  gate**, empaqueta con Velopack y publica el GitHub Release con sus assets. Sin pasos manuales en el camino
  feliz; `vpk upload github` local queda como fallback.
- **Auto-update ACTIVADO a nivel build:** `BuildDefaultUrl` deja de estar vacía. Cierra la parte diferida de
  ADR-014 y completa la pieza 2 de ADR-004. La condición de uso 2 de F1 pasa de "resuelta a nivel mecanismo" a
  **"canal montado"**; falta solo el acto operativo de pushear el primer tag (usuario).
- **Sin secretos en la app** (repo público, `accessToken: null`); el único secreto es el `GITHUB_TOKEN` del
  Actions, server-side. Feed https; integridad del paquete por SHA (Velopack).
- **El tag es la fuente de verdad de la versión;** se corrige la deriva `.csproj`/`pack.ps1` pasando `-p:Version`.
- **Sin dependencias nuevas en la app. Sin migración EF.** Cambio de código mínimo (1 línea + 2 tests).
- **Deuda explícita (futuro):** deltas (requiere `vpk download` + partir `pack.ps1`), release de CI firmado
  (secret `.pfx`), firma v2 (Azure Trusted Signing), landing web, canales beta. Ver §6.

## Riesgos

- **Build viejo del piloto (URL vacía) no se actualiza solo.** Mitigación documentada: reinstalar con un
  `Setup.exe` nuevo, o cargar el origen en Config tras PIN. **Esperado**, no es un bug.
- **Primer tag mal numerado** (menor o igual a lo instalado) → no se detecta update. Mitigación: el workflow
  valida el **formato** del tag; el **orden** SemVer lo cuida el que etiqueta (documentar: el primer tag
  auto-actualizable debe ser > lo instalado en los pilotos).
- **`vpk upload github` sin `vpk download` previo** → solo paquetes full (sin deltas). Aceptado en v1.
- **Release de CI sin firma** → en PCs sin el `.cer`, SmartScreen avisa (ya pasa hoy). Mitigación: pack firmado
  local para el pendrive; firma v2 cuando se venda a gimnasios que instalan solos.
- **Desalineación `vpk` CLI vs. Velopack runtime** → feed con formato raro. Mitigación: `vpk` pineado a `1.2.0`
  (= `Directory.Packages.props`); al subir Velopack, subir el pin del workflow en el mismo commit.
- **El exe portable no se auto-actualiza** → quien lo use debe bajar el nuevo. Documentado; es un artefacto
  aparte (prueba/pendrive), no el canal de updates.

## Alternativas rechazadas

- **Versión desde el `.csproj` (tag solo como etiqueta):** mantiene la deriva actual (0.3.0 vs 0.2.0) y obliga a
  sincronizar dos lugares. Se prefiere **el tag como fuente de verdad** + `-p:Version`.
- **Workflow que reescribe el empaquetado inline (sin `pack.ps1`):** duplica packId/mainExe/icono/signParams y se
  desincroniza del pack local. Se **reusa `pack.ps1`** (una sola fuente de verdad, ya probada).
- **Publicar como draft y requerir publicación manual en el camino feliz:** agrega un paso humano a cada release
  sin beneficio (el tag ya es deliberado y los tests son el gate). Draft queda solo como **dry-run** opcional.
- **Deltas en v1:** suma un `vpk download` + partir `pack.ps1` por un ahorro de ancho de banda que un gimnasio
  casi no nota. Diferido.
- **Firmar en CI (subir `.pfx` como secret):** mete un secreto en Actions para un cert autofirmado que **igual**
  no saca el aviso en PCs ajenas. Bajo retorno; se difiere a firma v2 (CA reconocida).
- **Repo privado con token embebido:** prohibido por ADR-014 (credencial filtrable en el exe). El repo es
  público; no aplica.

## Referencias

- Cliente de update (no cambia): `src/GymForge.Desktop/Services/VelopackUpdateService.cs:96-119,131-137`
  (`VelopackLocator`, `GithubSource(accessToken: null)`, `CurrentVersion`)
- Origen/validación (ADR-014, no cambia salvo la const): `src/GymForge.Desktop/Services/UpdateFeed.cs:11,18-55`
- Único cambio de código: `src/GymForge.Desktop/Services/UpdateFeed.cs:11` (`BuildDefaultUrl`)
- Tests a ajustar: `tests/GymForge.Desktop.Tests/VelopackUpdateServiceTests.cs:21-32,60-71` ·
  intactos: `tests/GymForge.Desktop.Tests/UpdateFeedTests.cs`
- Empaquetado: `scripts/pack.ps1:26-29,51-68` (publish + `vpk pack` + comando de upload) ·
  `src/GymForge.Desktop/GymForge.Desktop.csproj:23` (`<Version>`) ·
  `Directory.Packages.props:89` (`Velopack 1.2.0` → pin de `vpk`)
- CI hermano: `.github/workflows/ci.yml` (restore/build/test en windows-latest)
- Firma / distribución: `docs/CODE-SIGNING.md` · `docs/DISTRIBUCION.md` (secciones "Actualizaciones" y "3.
  Descarga web")
- ADRs relacionados: `docs/adr/ADR-004-robustez-de-produccion.md` (pieza 2 auto-update) ·
  `docs/adr/ADR-014-origen-de-actualizaciones.md` (`BuildDefaultUrl` diferida, https, mask)
- Roadmap/deuda: `docs/PROGRESS.md` (ítems 67/181) · `docs/TASKS.md` (Deuda de F1 cond. 2; NEW-62..65)
