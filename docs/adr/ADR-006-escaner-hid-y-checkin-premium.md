# ADR-006 — Escáner HID + check-in premium (Fase 3)

**Status:** Accepted
**Date:** 2026-10-29
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` · veta `qa`
**Sesión:** 5 — Fase 3 del roadmap (`docs/PROGRESS.md`), ítem **F3** de `docs/TASKS.md`

> **Addendum de implementación (2026-11-02, al pasar a Accepted).** Se implementaron las 4 piezas,
> con una desviación decidida por el usuario: **la foto del socio quedó SOLO por archivo/pegar
> (Pieza B "fallback"), sin FlashCap ni cámara en vivo.** Por lo tanto la interfaz `IPhotoCapture` /
> `FlashCapPhotoCapture` **no existe** en el código; en su lugar hay `IImagePicker` /
> `AvaloniaImagePicker` + `MemberPhotoStore` + `PhotoPickerViewModel`/`PhotoPickerView`, sin ninguna
> dependencia NuGet nueva. La captura por webcam (FlashCap, `IPhotoCapture`) queda como opción futura,
> fuera de alcance. El resto del ADR (A scanner keyboard-wedge, C teclado numérico, D encuadre de
> hardware) se implementó como está descrito; el índice único `AddMemberTagSerialUniqueIndex` y el
> producto por barcode en el POS **sí** entraron. Deuda registrada en `docs/TASKS.md` (F3-D1 a F3-D7,
> UX18 a UX24).
>
> Tres ajustes de implementación surgidos en la verificación de qa (runtime), que refinan el diseño de la
> Pieza A sin cambiar su intención: (1) el glue del lector escucha a nivel de **ventana**
> (`WedgeKeyboardListener.ForWindowOf`), no de la vista, para que la ráfaga llegue aunque el foco esté en un
> control hermano (p. ej. el botón del modal del POS); (2) en el kiosk el Enter de ingreso manual se maneja
> con un **handler de túnel** que respeta el `Handled` del listener, en vez del `KeyBinding` del campo —si no,
> el Enter final del escaneo grababa además una carga manual "Desconocido" en el AccessLog (append-only); (3)
> la foto se decodifica reducida (`Bitmap.DecodeToWidth(480)`) para las previews, y la lectura al pegar se
> topa en 10 MB, para no agotar memoria con imágenes de varios MP.

---

## Context

Fase 3 "Escáner HID + check-in premium": darle al mostrador del gimnasio tres cosas de hardware barato que
hoy están mockeadas o no existen, y dejar encuadrada la arquitectura de hardware para no mezclarla con los
sidecars de Fase 6. Son **cuatro piezas**:

1. **Lector HID keyboard-wedge** en el kiosk de check-in y en el POS (Caja). Los lectores de código de
   barras / tarjetas tipo *keyboard-wedge* "tipean" el código como si fueran un teclado y mandan Enter al
   final. Hay que capturar esa ráfaga (varios caracteres muy rápidos + terminador) y **distinguirla de
   alguien tecleando a mano**, sin depender de que haya un campo de texto con foco, y mapear el código a un
   socio (o, en el POS, a un producto).
2. **Foto del socio por webcam.** Hoy el alta tiene foto "mock" (`CapturePhotoCommand` pone `PhotoUrl = null`).
   Hay que capturar de la cámara real, con una librería compatible con el **exe single-file self-contained**,
   y **degradar con elegancia** (elegir archivo) si no hay cámara.
3. **Teclado numérico en pantalla** (on-screen), para recepción táctil: cargar DNI/credencial y PIN sin
   teclado físico. `UserControl` reutilizable.
4. **Arquitectura de hardware.** Encuadrar estas piezas respecto de los sidecars del CLAUDE.md
   (FiscalBroker/BioBroker/AccessBroker) y del proyecto `GymForge.Hardware`. Dejar claro qué entra ahora
   (in-process, OS) y qué queda para la Fase 6 (biometría ZKTeco real, puertas físicas).

Esta es la guía de implementación para `dev` en **TDD**: no inventar arquitectura, seguir las firmas y el
orden de abajo. Alcance acotado a estas 4 piezas; nada más entra en Fase 3.

Reglas del proyecto que condicionan el diseño (CLAUDE.md): **offline-first** (todo persiste en SQLite antes
que nada; la webcam y el scanner son entrada local, nunca dependen de cloud), **multi-tenant** por
`CompanyId` + `SiteId`, **AccessLog append-only** (el swipe del scanner sigue pasando por el Gatekeeper, que
ya audita), `FingerprintTemplate` solo `byte[]` ZKTeco (la biometría real es Fase 6, no se toca acá),
`dotnet test` verde antes de cada commit (regla 6). Piso declarado por el equipo tras Fase 2: **862 tests
verdes**. **Toda dependencia nativa que se agregue tiene que funcionar con el publish single-file
self-contained win-x64** (`scripts/publish.ps1` → `dist/GymForge.exe`).

### Hechos del código verificados al 2026-10-29

Mucho de esto **ya existe**. Se reutiliza, no se duplica.

- **El modelo de credencial del socio YA EXISTE.** `Member.TagSerial` (`string?`,
  `src/GymForge.Domain/Entities/Member.cs:26`) con factory de escritura `Member.EnrollTag(string tagSerial)`
  (L119) y columna EF con índice: `builder.Property(m => m.TagSerial).HasMaxLength(50)` +
  `builder.HasIndex(m => m.TagSerial).HasDatabaseName("IX_Members_TagSerial")`
  (`MemberConfiguration.cs:19,50`). **No hace falta agregar campo ni migración para la credencial.** El único
  cambio de schema opcional de la fase es endurecer ese índice a único por empresa (ver Pieza A).
- **El Gatekeeper ya resuelve credencial → socio por dos caminos.** `ValidateSwipeUseCase.ValidateSwipeAsync`
  (`src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs:82-87`) hace un `switch` por
  `AccessMethod`: `KeypadPin`/`Manual` → `FindByDocumentAsync(DNI, credential)`, cualquier otro método →
  `FindByTagSerialAsync(credential)`. Ambos filtran por `CompanyId` y están cubiertos por los global query
  filters de tenant (ADR-002). El resto del algoritmo (membresía, deuda, horario, anti-passback, visitas) ya
  corre igual sea cual sea el método.
- **El kiosk ya tiene el punto de entrada.** `CheckInKioskViewModel.ProcessCredentialAsync(string credential,
  AccessMethod method)` (`src/GymForge.Desktop/ViewModels/Checkin/CheckInKioskViewModel.cs:67`) llama al
  Gatekeeper y pinta el resultado. Hoy lo dispara un `TextBox` enfocado (`DniInput`, `CheckInKioskView.axaml:60`)
  con `KeyBinding Enter → ManualCheckInCommand` (`AccessMethod.Manual`). El code-behind reenfoca ese `TextBox`
  al cargar y al volver a Idle (`CheckInKioskView.axaml.cs:37`). **Un wedge que tipea en ese TextBox ya
  "funciona" hoy, pero no se distingue de tecleo manual y queda registrado como `Manual` (DNI), no como tarjeta.**
- **El POS es el modal "Registrar cobro / venta".** `RegisterSaleViewModel`
  (`src/GymForge.Desktop/ViewModels/Cash/RegisterSaleViewModel.cs`) carga los socios de la sede
  (`GetPagedAsync(...,1,500)`), los planes y los productos; el operador elige `SelectedMember` /
  `SelectedProduct` a mano. El CLAUDE.md marca "scanner HID" como pendiente del POS. `Product` ya tiene
  `Sku` y `Barcode` (`src/GymForge.Domain/Entities/Product.cs:6-7`).
- **La foto hoy es mock y se persiste por RUTA EN DISCO, no en DB.** `CreateMemberViewModel.CapturePhoto()`
  pone `PhotoUrl = null` con el comentario "Sprint 2: wires webcam via AForge.NET or MediaCapture"
  (`CreateMemberViewModel.cs:167-172`). El modelo guarda `Member.PhotoUrl` (ruta absoluta) y `Member.UpdatePhoto(path)`
  (`Member.cs:22,107`). El patrón de referencia para escribir imágenes es el logo del gimnasio:
  `SettingsViewModel.SaveBrandingAsync` copia el archivo elegido a `%LOCALAPPDATA%\GymForge\brand\logo.ext`
  y guarda la ruta absoluta (`SettingsViewModel.cs:272-284`, `BrandDir()` L303). Las fotos van en la carpeta
  hermana `%LOCALAPPDATA%\GymForge\fotos\`.
- **La migración entre PCs YA contempla las fotos.** `DataTransferService` exporta las carpetas
  `["brand","fotos"]` en el zip y, al importar, **reapunta** `Members.PhotoUrl` (y los de `BodyMeasurements`)
  a la carpeta `fotos` de la PC nueva (`DataTransferService.cs:30,276`, `Reapuntar` L283). **Mientras la foto
  se guarde en `%LOCALAPPDATA%\GymForge\fotos\` con ruta absoluta, viaja sola.** No hay que tocar
  `DataTransferService`.
- **Hay file picker reutilizable.** `TopLevel.GetTopLevel(this).StorageProvider.OpenFilePickerAsync(...)`
  con filtro de imágenes ya se usa para el logo (`SettingsViewModel.cs:234-238`) y para el CSV
  (`MembersListView.axaml.cs:41-52`). Es la base del fallback "elegir archivo".
- **NO hay teclado numérico en pantalla.** El login de cajero por PIN es un `TextBox` con `PasswordChar`
  (`CashView.axaml:51`), lo mismo en Onboarding y en Config → Seguridad. El PIN se valida con PBKDF2
  (`ChangePinCommand`). No hay ningún control táctil reutilizable: la Pieza C lo crea de cero.
- **`GymForge.Hardware` son contratos de sidecar.** `IFiscalPrinter`, `IBiometricReader` (ZKTeco),
  `IAccessController` (`src/GymForge.Hardware/Interfaces/`). Son dispositivos **out-of-process** (sidecars
  :12000/:12001/:12002) que hoy son stubs y son trabajo de Fase 6. El proyecto `GymForge.Hardware` **no**
  referencia Avalonia. `GymForge.Desktop` referencia `GymForge.Hardware` (`GymForge.Desktop.csproj:31`).
- **Desktop es `net9.0-windows`, `WinExe`, `BuiltInComInteropSupport=true`, con `app.manifest`**
  (`GymForge.Desktop.csproj:3-10`). Hoy no tiene ninguna dependencia nativa propia (solo Avalonia, Fluent,
  QuestPDF, Velopack, Serilog, CommunityToolkit). El publish es single-file self-contained.
- **Patrón de registro de servicios Desktop:** `ServiceCollectionExtensions.AddGymForgeDesktop`
  (`src/GymForge.Desktop/ServiceCollectionExtensions.cs`). Servicios de infraestructura de UI como singleton
  (`ReceiptService`, `IWhatsAppLinkSender`, `IUpdateService`). Convenciones CLAUDE.md: records MediatR,
  factory methods + ctor privado en dominio, `[ObservableProperty]`/`[RelayCommand]`, namespaces file-scoped.

### Qué NO toca esta fase

- **NO** se agrega una sección nueva al sidebar (`NavSection`). Fase 3 mejora superficies existentes: kiosk,
  POS (Caja) y alta/ficha de socio.
- **NO** se implementa biometría real ni apertura de puerta física (Fase 6; ver Pieza D).
- **NO** se toca el algoritmo del Gatekeeper salvo agregar una rama de resolución de credencial aditiva.

---

## Decisión — resumen

| # | Pieza | Decisión clave | Dónde vive | UI | Tamaño |
|---|---|---|---|---|---|
| A | Lector HID keyboard-wedge | Detector de ráfaga **puro y testeable** por timing (`KeyboardWedgeDetector`) + glue de Avalonia a nivel de vista (tunnel, `handledEventsToo`); el código se mapea a socio por `TagSerial` y, si no hay, por DNI; nuevo `AccessMethod.Barcode`; credencial = `Member.TagSerial` (ya existe) | Desktop + Application (resolución) | Sí (enrolar credencial) | M |
| B | Foto por webcam | `IPhotoCapture` en Desktop con impl **FlashCap** (managed, sin DLL nativa → compatible single-file); **fallback obligatorio** "elegir archivo / pegar imagen"; foto a `%LOCALAPPDATA%\GymForge\fotos\{memberId}.jpg` (migración ya lo contempla) | Desktop | Sí (captura + preview) | M |
| C | Teclado numérico en pantalla | `NumericKeypad` `UserControl` con `StyledProperty` `Text` (two-way) + evento `Entered`; sin dependencia de VM; reutilizado en kiosk y login de caja | Desktop | Sí | S |
| D | Arquitectura de hardware | Frontera explícita: **in-process/OS (Fase 3)** = scanner + webcam, sin sidecar; **out-of-process/device (Fase 6)** = fiscal/bio/access via sidecars. `GymForge.Hardware` queda reservado a los sidecars | Doc + encuadre | No | S |

### Decisiones transversales (leer antes que el detalle)

- **El scanner keyboard-wedge NO necesita sidecar ni driver.** Es entrada de teclado del SO; se captura
  dentro del proceso de Desktop. No hay interfaz en `GymForge.Hardware` para esto.
- **La webcam es in-process, no sidecar.** Produce frames para la preview de Avalonia y termina en un archivo
  en disco (como el logo). Por eso `IPhotoCapture` vive en **Desktop**, no en `GymForge.Hardware` (que es
  para protocolos de dispositivos externos via sidecar y no referencia Avalonia).
- **El scanner reutiliza el Gatekeeper, no lo duplica.** En el kiosk, un escaneo entra por el mismo
  `ProcessCredentialAsync` → `ValidateSwipeAsync` que la carga manual; cambia solo el `AccessMethod`
  (`Barcode` en vez de `Manual`) y, por lo tanto, la resolución de credencial y la traza del AccessLog. Todo
  el resto (membresía, deuda, horario, anti-passback, visitas, append-only) es idéntico.
- **La credencial del socio es `Member.TagSerial`, que ya existe.** El scanner lee lo que el gimnasio imprima
  o grabe en el carnet/llavero. Sin migración para el campo (sí una opcional para el índice único).
- **El fallback de foto es el piso, la webcam es el extra.** "Elegir archivo / pegar imagen" no agrega
  dependencias y debe estar siempre disponible (no toda PC de recepción tiene cámara). La webcam en vivo
  (FlashCap) va detrás de `IPhotoCapture.IsAvailable` y es la única pieza que suma una dependencia nueva:
  por eso la librería es una **decisión a confirmar** y, si el usuario no la quiere, se puede enviar solo el
  fallback sin cambiar el resto del diseño.

---

## Pieza A — Lector HID keyboard-wedge (kiosk + POS)

### Qué falta

Capturar la ráfaga del wedge y distinguirla de tecleo manual, sin depender de un `TextBox` enfocado, y
mapear el código a un socio con la traza correcta.

### A.1 — Detector de ráfaga (puro, testeable) — `GymForge.Desktop/Services/`

La clave de "scan vs tecleo" es el **timing entre teclas**. Un wedge entrega cada carácter con 1–15 ms de
separación y cierra con Enter; una persona teclea con 80–300 ms entre teclas. El detector es una máquina de
estados pura que recibe `(carácter, ¿es terminador?, timestamp en ms)` y decide, por tecla, si hay que
**tragarla** (para que no caiga en el control enfocado) y si un terminador **cerró un escaneo válido**.

```csharp
// src/GymForge.Desktop/Services/KeyboardWedgeDetector.cs
public sealed record WedgeOptions(
    int InterKeyScanMaxMs = 50,   // gap máximo entre teclas DENTRO de una ráfaga de scanner
    int IdleResetMs       = 300,  // gap que reinicia el buffer (empieza una secuencia nueva)
    int MinScanLength     = 3,    // mínimo de caracteres para considerarlo un escaneo
    int MaxScanLength     = 64);  // tope de sanidad

public enum WedgeAction { Passthrough, Swallow }   // Passthrough = dejar que la tecla llegue al control
public readonly record struct WedgeResult(WedgeAction Action, string? CompletedScan);

public sealed class KeyboardWedgeDetector
{
    public KeyboardWedgeDetector(WedgeOptions? options = null);

    /// <summary>
    /// Alimenta una tecla. Devuelve si hay que tragarla y, si un terminador cerró un escaneo válido,
    /// el código acumulado (sin el terminador). Determinístico: el timestamp lo pasa el llamador.
    /// </summary>
    public WedgeResult Push(char c, bool isTerminator, long timestampMs);

    public void Reset();
}
```

Reglas de `Push` (documentadas y testeadas):
1. `gap = timestampMs - _lastTimestampMs`. Si `gap > IdleResetMs` → se descarta el buffer y empieza una
   secuencia nueva (la tecla actual es "la primera").
2. **Carácter normal:**
   - Si es la primera de la secuencia (buffer vacío) → se bufferea pero se deja **Passthrough** (todavía no
     sabemos si es ráfaga; que llegue al control enfocado como tecleo normal).
   - Si `gap <= InterKeyScanMaxMs` → es parte de una ráfaga: se bufferea y se **Swallow** (no debe duplicarse
     en el control enfocado).
   - Si `gap > InterKeyScanMaxMs` → tecleo manual: se reinicia el buffer con esta tecla y **Passthrough**.
3. **Terminador (Enter):** si `buffer.Length ∈ [MinScanLength, MaxScanLength]` **y** la secuencia fue rápida
   (todas las transiciones internas `<= InterKeyScanMaxMs`) → `WedgeResult(Swallow, buffer)` (se traga el
   Enter para que no dispare nada) y se limpia el buffer. Si no → `WedgeResult(Passthrough, null)` (Enter
   manual: que lo maneje el control/KeyBinding de siempre) y se limpia el buffer.
4. Actualiza `_lastTimestampMs`.

> **Límite explícito y aceptado:** el **primer** carácter de una ráfaga siempre es Passthrough (no hay gap
> previo para medir), así que puede "ensuciar" el control enfocado con 1 carácter antes de que empiece el
> swallow. En el kiosk esto es invisible porque el escaneo se procesa y la pantalla se resetea; igual, el
> glue (abajo) limpia el `TextBox` antes de procesar el scan. El tecleo manual **nunca** se traga (sus gaps
> superan el umbral), así que la recepción puede seguir cargando DNI a mano sin interferencia.

### A.2 — Glue de Avalonia (a nivel de vista, no global) — code-behind

Un comportamiento liviano conecta el `KeyDown` de la vista al detector y expone un evento. Vive **por
pantalla** (kiosk y POS), no como hook global: solo captura mientras esa vista está viva.

- Se suscribe en `OnLoaded` y se desuscribe en `OnUnloaded` con
  `AddHandler(InputElement.KeyDownEvent, handler, RoutingStrategies.Tunnel, handledEventsToo: true)` en la
  raíz de la vista (así ve las teclas aunque el `TextBox` tenga foco).
- Mapea `Avalonia.Input.Key` → carácter (dígitos `D0..D9`/`NumPad0..9`, letras `A..Z`; el resto se ignora) y
  detecta terminador (`Key.Enter`/`Key.Return`). Usa `e.Timestamp` (o `Stopwatch`) como timestamp en ms.
- Llama `detector.Push(...)`. Si `Action == Swallow` → `e.Handled = true`. Si `CompletedScan is {} code` →
  dispara `event Action<string> Scanned` en el hilo de UI.

Se materializa como un helper reutilizable (ambas vistas lo usan igual):
`src/GymForge.Desktop/Services/WedgeKeyboardListener.cs` (o un `Behavior`/attached helper; lo decide `dev`
con `ux`). El `KeyboardWedgeDetector` es lo testeable; el listener es glue fino sin lógica de timing.

### A.3 — Resolución del código a socio (Application, compartida)

**Qué lee el scanner (recomendación):** el código impreso/grabado en el carnet del gimnasio, que se guarda
en `Member.TagSerial`. Resolución en dos pasos, aditiva y testeable:

1. `FindByTagSerialAsync(code)` — carnet/llavero del gimnasio.
2. Si no hay match **y** el código tiene forma de DNI (7–8 dígitos ASCII) → `FindByDocumentAsync(DNI, code)`.
   Cubre a los gimnasios que simplemente imprimen el DNI como número de socio, o que usan un lector 2D que
   lee el PDF417 del DNI y del que se extrae el número (parseo del PDF417: fuera de alcance, ver "Decisiones
   a confirmar").

El criterio de forma de DNI es un helper puro:

```csharp
// src/GymForge.Application/UseCases/Access/CredentialMatch.cs
public static class CredentialMatch
{
    /// <summary>7–8 dígitos ASCII, sin otros caracteres.</summary>
    public static bool LooksLikeDni(string code);
}
```

**Kiosk:** nuevo valor `AccessMethod.Barcode` y una rama aditiva en el `switch` del Gatekeeper:

```csharp
// ValidateSwipeUseCase.cs (dentro del switch de resolución de credencial)
AccessMethod.KeypadPin or AccessMethod.Manual =>
    await _memberRepo.FindByDocumentAsync(DocumentType.DNI, request.Credential, request.CompanyId, ct),
AccessMethod.Barcode =>
    await _memberRepo.FindByTagSerialAsync(request.Credential, request.CompanyId, ct)
        ?? (CredentialMatch.LooksLikeDni(request.Credential)
            ? await _memberRepo.FindByDocumentAsync(DocumentType.DNI, request.Credential, request.CompanyId, ct)
            : null),
_ => await _memberRepo.FindByTagSerialAsync(request.Credential, request.CompanyId, ct)  // RfidCard, Qr, etc.
```

Es **aditivo**: `RfidCard`/`Manual`/`KeypadPin` no cambian de comportamiento, así que las 6+ pruebas de
`GatekeeperTests` quedan verdes. `AccessMethod` se persiste como string en `AccessLog` (ver
`AccessEnums.cs` + config EF): agregar `Barcode` **no requiere migración** (es un valor nuevo del enum que
simplemente aparece en filas nuevas).

El `CheckInKioskViewModel` suma el enganche del scanner: cuando el listener dispara `Scanned(code)`, llama
`ProcessCredentialAsync(code, AccessMethod.Barcode)`. El `TextBox` manual + el teclado numérico (Pieza C)
siguen siendo `AccessMethod.Manual`.

**POS (Caja):** el escaneo resuelve **producto o socio**, en ese orden (ambos campos ya existen):
1. `FindProductByBarcodeQuery(CompanyId, code)` → si hay producto, en concepto "Producto" selecciona
   `SelectedProduct` (o suma cantidad). `Product.Barcode` ya existe.
2. Si no es producto → `FindMemberByCredentialQuery(CompanyId, code)` (misma regla TagSerial→DNI que el
   kiosk, reutilizando `CredentialMatch`) → setea `SelectedMember`. Como `GetPagedAsync` trae solo los
   primeros 500, el handler devuelve el socio aunque no esté en la lista cargada; el VM lo agrega a
   `Members` si falta antes de seleccionarlo.

```csharp
// src/GymForge.Application/UseCases/Members/FindMemberByCredentialQuery.cs
public record FindMemberByCredentialQuery(Guid CompanyId, string Code) : IRequest<MemberDto?>;
// src/GymForge.Application/UseCases/Catalog/FindProductByBarcodeQuery.cs
public record FindProductByBarcodeQuery(Guid CompanyId, string Barcode) : IRequest<ProductDto?>;
```

> El soporte de **producto por barcode en el POS es secundario** y se incluye solo porque `Product.Barcode`
> ya existe y es barato. Si `ux`/`dev` lo ven pesado, se puede diferir sin afectar el resto: el core de la
> pieza es el mapeo a socio. Marcar como decisión a confirmar.

### A.4 — Enrolar la credencial en el socio

El scanner necesita que el socio tenga `TagSerial` cargado (salvo que se escanee el DNI). Hoy no hay UI para
eso. Se agrega:

- Campo **"Credencial / N° de tarjeta"** (opcional) en el alta (`CreateMemberView`), con un botón "Escanear"
  que captura el próximo escaneo en ese campo (reusa el listener en modo "capturar a campo").
- `CreateMemberCommand` suma un parámetro opcional `string? TagSerial = null`; el handler, si viene, hace
  `member.EnrollTag(serial)` tras validar que **no esté en uso por otro socio de la empresa**.
- Comando dedicado para asignar/reasignar desde la ficha sin abrir el editor completo del socio (el editor
  completo es Fase 4): `AssignCredentialCommand(CompanyId, MemberId, string TagSerial)`, con la misma
  validación de unicidad. En `MemberDetailView` (pestaña Datos) un botón "Asignar credencial" que escanea.

**Índice único (único cambio de schema de la fase, opcional pero recomendado):** hoy `IX_Members_TagSerial`
es no único, así que dos socios podrían compartir credencial y `FirstOrDefaultAsync` devolvería cualquiera.
Migración `AddMemberTagSerialUniqueIndex`: índice **único filtrado** sobre `(CompanyId, TagSerial)` con
`WHERE TagSerial IS NOT NULL` (SQLite lo soporta via `HasFilter`). La validación en el comando da el mensaje
amable; el índice es el backstop. Si el usuario prefiere no migrar, queda solo la validación en el comando
(se asume credencial única por convención) y se omite la migración.

### Decisiones

- **El scanner lee la credencial del carnet (`TagSerial`), con fallback a DNI.** Funciona con cualquier
  lector 1D barato (el gimnasio imprime el código que quiera) y además soporta "escanear el DNI como número"
  sin enrolar. Ver "Decisiones a confirmar" (DNI vs credencial propia).
- **`AccessMethod.Barcode` nuevo** para que el AccessLog distinga tarjeta de carga manual. Sin migración.
- **Detector puro + glue por vista.** El timing es la única lógica y es 100% testeable sin Avalonia ni reloj.
  El hook es por pantalla (no global) para no capturar teclas en Config, Socios, etc.
- **Enrolar `TagSerial` reusa lo que ya hay** (`EnrollTag`, campo, índice). El único schema opcional es el
  índice único.

### Tests (TDD)

- `tests/GymForge.Desktop.Tests/KeyboardWedgeDetectorTests.cs` (nuevo, **sin** Avalonia):
  - `RafagaRapidaConEnter_EmiteScan` (10 chars a 5 ms + Enter → `CompletedScan`, teclas intermedias `Swallow`).
  - `TecleoLento_NoEmiteYNoTraga` (chars a 150 ms + Enter → siempre `Passthrough`, `CompletedScan == null`).
  - `RafagaDemasiadoCorta_NoEmite` (2 chars < `MinScanLength`).
  - `GapLargoReiniciaBuffer` (ráfaga, pausa > `IdleResetMs`, otra ráfaga → solo la segunda se emite entera).
  - `PrimerCaracterSiemprePassthrough` (documenta el límite aceptado).
  - `Reset_LimpiaEstado`.
- `tests/GymForge.Application.Tests/Access/CredentialMatchTests.cs`: `LooksLikeDni` ("40123456" true;
  "4012345" true; "ABC123" false; "123456789" false (9); vacío false).
- Ampliar `tests/GymForge.Application.Tests/GatekeeperTests.cs`:
  - `Barcode_ResuelvePorTagSerial`.
  - `Barcode_SinTag_CaeADniSiTieneFormaDni`.
  - `Barcode_SinTag_NoEsDni_Deniega_TagUnknown`.
  - Los casos existentes de `RfidCard`/`Manual`/`KeypadPin` **siguen verdes**.
- `tests/GymForge.Application.Tests/Handlers/FindMemberByCredentialQueryTests.cs`: TagSerial → match; sin
  tag + DNI → match; multi-tenant (no cruza empresa).
- `tests/GymForge.Application.Tests/Handlers/AssignCredentialCommandTests.cs`: asigna; rechaza si otro socio
  de la empresa ya tiene esa credencial; aísla por tenant.
- Integración: si se hace el índice único, `MemberRepositoryIntegrationTests` → insertar dos socios con el
  mismo `TagSerial` en la misma empresa lanza; en empresas distintas no.
- El listener de Avalonia y el enganche del scanner en kiosk/POS: **verificación manual** (no unit).

---

## Pieza B — Foto del socio por webcam

### Qué falta

Capturar de la cámara real en el alta (y permitir cambiar la foto en la ficha), guardar la imagen como
archivo y degradar si no hay cámara, todo compatible con el exe single-file self-contained.

### Decisión de librería: **FlashCap** (con fallback obligatorio por file picker)

Recomendación: **FlashCap** (`FlashCap`, NuGet, MIT, mantenida) para la captura en vivo, detrás de
`IPhotoCapture`. Justificación comparada:

| Opción | Dependencia nativa | Single-file self-contained | Veredicto |
|---|---|---|---|
| **FlashCap** | **Ninguna.** P/Invoke managed a DirectShow/Media Foundation del propio Windows | **Compatible sin nada especial** (es puro managed, ~200 KB, sin DLL que extraer) | **Elegida** |
| OpenCvSharp4 + runtime.win | `OpenCvSharpExtern.dll` + ffmpeg (~40 MB) | Necesita `IncludeNativeLibrariesForSelfExtract`; las DLL se extraen a temp en el primer arranque (más lento, falsos positivos de AV) | Descartada (pesada para sacar una foto) |
| Windows.Media.Capture (CsWinRT) | Proyecciones WinRT | Exige subir el TFM a `net9.0-windows10.0.19041.0`; `MediaCapture` es incómodo desde Win32 (`IInitializeWithWindow`), la preview en Avalonia es difícil y parte de la API asume identidad "packaged" | Descartada (sobredimensionada) |
| AForge.NET / DirectShowLib | DirectShow vía libs .NET Framework | Sin mantenimiento (último release ~2013), riesgoso en net9 | Descartada |
| P/Invoke propio (Media Foundation / escapi) | Ninguna, pero todo a mano | Compatible pero es reinventar FlashCap con más bugs | Descartada (FlashCap ya lo hace) |

FlashCap resuelve exactamente lo que el brief pedía por "captura por P/Invoke", pero como librería mantenida:
enumera cámaras, abre un dispositivo, entrega frames (se puede pedir MJPEG y guardar el JPEG directo) y se
integra con Avalonia renderizando frames en un `Image`/`WriteableBitmap`. **No agrega ninguna DLL nativa al
publish.** Es la **única dependencia nueva de la fase** y por eso es una decisión a confirmar con el usuario.

**Fallback obligatorio (piso sin dependencias):** "Elegir archivo" con el file picker de Avalonia
(`StorageProvider.OpenFilePickerAsync`, patrón ya usado para el logo) y, opcional, "Pegar imagen" desde el
portapapeles. Está **siempre** disponible y es lo que se usa cuando `IPhotoCapture.IsAvailable == false` (PC
sin cámara) o si la captura falla. Si el usuario decide no sumar FlashCap, se envía solo el fallback y el
resto del diseño no cambia.

### Diseño — `IPhotoCapture` en Desktop

```csharp
// src/GymForge.Desktop/Services/IPhotoCapture.cs
public record CameraDevice(string Id, string Name);

public interface IPhotoCapture
{
    /// <summary>¿Hay al menos una cámara utilizable? Si es false, la UI muestra solo "Elegir archivo".</summary>
    bool IsAvailable { get; }
    Task<IReadOnlyList<CameraDevice>> GetCamerasAsync(CancellationToken ct = default);
    /// <summary>Arranca la preview; entrega frames (JPEG) para pintarlos en un Image de Avalonia.</summary>
    Task StartPreviewAsync(CameraDevice camera, Action<byte[]> onJpegFrame, CancellationToken ct = default);
    Task StopPreviewAsync();
    /// <summary>Devuelve el JPEG del frame actual (la foto a guardar).</summary>
    Task<byte[]> CaptureStillAsync(CancellationToken ct = default);
}
```

- Implementación Windows: `FlashCapPhotoCapture` (`src/GymForge.Desktop/Services/`). Registrada en
  `ServiceCollectionExtensions` como `AddSingleton<IPhotoCapture, FlashCapPhotoCapture>()`. Si FlashCap no
  encuentra dispositivos, `IsAvailable = false` y `GetCamerasAsync` devuelve vacío (nunca tira).
- Degradación: `IPhotoCapture` encapsula la cámara; el fallback de archivo lo maneja la vista directamente
  con el `StorageProvider` (no es parte de la interfaz, porque es un flujo de UI, no de dispositivo).

### Persistencia de la foto (sin romper el patrón ni la migración)

- Store fino en Desktop: `MemberPhotoStore.Save(Guid memberId, byte[] jpeg) : string` escribe en
  `%LOCALAPPDATA%\GymForge\fotos\{memberId}.jpg` (crea la carpeta, igual que `BrandDir()`) y devuelve la ruta
  absoluta. La **lógica de archivos vive en Desktop** (como el logo), no en Application.
- Comando de dominio fino: `UpdateMemberPhotoCommand(Guid CompanyId, Guid MemberId, string PhotoPath)` →
  `member.UpdatePhoto(path)` + save. Application **no** hace I/O de archivos: recibe la ruta ya escrita.
- Flujo en el alta (`CreateMemberViewModel`): el VM guarda `byte[]? _pendingPhoto` (de la webcam o del
  archivo elegido). Tras crear el socio (`_createdMember.Id` disponible), si hay foto pendiente:
  `path = _photoStore.Save(id, _pendingPhoto)` → `UpdateMemberPhotoCommand(...)`. Mismo criterio de
  reintento que ya tiene el alta (no duplica socio si falla el cobro; acá tampoco duplica si re-guarda).
- Flujo en la ficha (`MemberDetailView`): el Id ya existe → captura/elige → store → comando → refresca la
  imagen.
- **Migración entre PCs: nada que hacer.** `DataTransferService` ya copia `fotos` y reapunta
  `Members.PhotoUrl`. Solo hay que respetar la carpeta y la ruta absoluta. Los recibos no viajan (se
  regeneran); las fotos sí.

### Decisiones

- **FlashCap** por ser managed-only (cero impacto en el publish single-file), mantenida y suficiente para
  sacar una foto. Decisión a confirmar con el usuario por ser dependencia nueva.
- **Fallback de archivo siempre presente**; es el piso sin dependencias y la degradación cuando no hay cámara.
- **Foto como archivo en `fotos\` con ruta absoluta** (idéntico al logo), para no tocar la migración.
- **I/O de archivos en Desktop, dominio recibe la ruta.** Application se mantiene sin dependencias de disco.

### Tests (TDD)

- `tests/GymForge.Application.Tests/Handlers/UpdateMemberPhotoCommandTests.cs`: setea `PhotoUrl` en el socio
  correcto; multi-tenant (no cruza empresa); ruta vacía/nula → validación.
- `tests/GymForge.Desktop.Tests/CreateMemberViewModelPhotoTests.cs`: con `IPhotoCapture` fake y
  `MemberPhotoStore` fake, tras guardar el socio con foto pendiente se llama al store y a
  `UpdateMemberPhotoCommand` con el Id devuelto; sin foto, no se llama; si la captura tira, el alta no se
  rompe (la foto es best-effort).
- `IPhotoCapture` real (FlashCap) y el fallback de file picker: **verificación manual** (hardware/UI).
- `MemberPhotoStore`: test de integración liviano opcional (escribe y devuelve ruta bajo `fotos\`).

---

## Pieza C — Teclado numérico en pantalla (`NumericKeypad`)

### Qué falta

Un control táctil reutilizable para cargar DNI/credencial y PIN sin teclado físico. No existe nada parecido
(el PIN hoy es un `TextBox` pelado).

### Diseño — `UserControl` con `StyledProperty`, sin dependencia de VM

```csharp
// src/GymForge.Desktop/Controls/NumericKeypad.axaml(.cs)
public partial class NumericKeypad : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<NumericKeypad, string>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<NumericKeypad, int>(nameof(MaxLength), 20);
    public static readonly StyledProperty<bool> MaskProperty =       // true = mostrar ● (PIN)
        AvaloniaProperty.Register<NumericKeypad, bool>(nameof(Mask), false);

    public string Text { get; set; }
    public int MaxLength { get; set; }
    public bool Mask { get; set; }

    /// <summary>Se dispara al tocar el botón de confirmar (OK/enter del teclado).</summary>
    public event EventHandler? Entered;
}
```

- Grilla 3×4: `0–9`, backspace (⌫) y "OK". Los dígitos agregan a `Text` respetando `MaxLength`; ⌫ borra el
  último; "OK" dispara `Entered`. Toda la mutación es sobre el string `Text` (bindeable two-way); el control
  no conoce ningún ViewModel.
- Estilado con el design system existente (botones grandes táctiles; `ux` define tamaños y tokens). `Mask`
  solo cambia la presentación del display (no guarda distinto).

### Reutilización

- **Kiosk** (`CheckInKioskView`): `NumericKeypad Text="{Binding CredentialInput}"` y `Entered` →
  `ManualCheckInCommand` (sigue siendo `AccessMethod.Manual`). Convive con el `TextBox` actual y con el
  scanner (Pieza A).
- **Login de caja** (`CashView`): `NumericKeypad Text="{Binding Pin}" Mask="True" MaxLength="8"` y `Entered`
  → `LoginCommand`. Opcionalmente el mismo control en Onboarding y en Config → Seguridad (cambio de PIN); no
  es obligatorio en Fase 3 pero queda disponible.

### Decisiones

- **Control de presentación, cero lógica de negocio.** Es un editor de string reutilizable; la acción
  ("ingresar", "login") la decide quien lo usa via `Entered`. Esto lo hace ubicable en cualquier pantalla.
- **`StyledProperty` two-way** en vez de depender de un VM concreto: se bindea a `CredentialInput`, a `Pin` o
  a lo que sea.

### Tests (TDD)

- `tests/GymForge.Desktop.Tests/NumericKeypadTests.cs`: agregar dígitos respeta `MaxLength`; ⌫ borra el
  último; "OK" dispara `Entered`; `Mask` no cambia el valor de `Text`. (Se pueden testear los métodos de
  mutación sin render; si render headless está disponible en el proyecto, mejor.)
- Integración kiosk/caja con el keypad: **verificación manual**.

---

## Pieza D — Arquitectura de hardware (encuadre)

### Decisión — dos categorías, frontera explícita

- **In-process / periférico del SO (Fase 3, SIN sidecar):**
  - **Scanner keyboard-wedge** = entrada de teclado. Se maneja en Desktop (`KeyboardWedgeDetector` + glue).
    No hay driver, no hay interfaz en `GymForge.Hardware`, no hay puerto.
  - **Webcam** = captura in-process que alimenta la UI y termina en un archivo. `IPhotoCapture` en Desktop.
    No es un sidecar; `GymForge.Hardware` (que no referencia Avalonia) no es su lugar.
- **Out-of-process / dispositivo externo (Fase 6, CON sidecar):** `IFiscalPrinter` (FiscalBroker :12000),
  `IBiometricReader` ZKTeco (BioBroker :12001), `IAccessController` (AccessBroker :12002). Siguen siendo
  stubs. `GymForge.Hardware` queda **reservado** para estos contratos de protocolo de dispositivo.

Regla para el futuro: si el dispositivo habla por teclado/USB-HID estándar o es una cámara UVC del SO →
in-process en Desktop. Si habla un protocolo propietario, necesita un runtime x86 (OCX fiscal), una SDK
nativa (libzkfpcsharp) o un socket a un controlador → sidecar y contrato en `GymForge.Hardware`.

### Qué queda explícitamente para Fase 6 (no se toca ahora)

- **Biometría ZKTeco real** (`IBiometricReader.EnrollAsync/VerifyAsync`, BioBroker `/enroll` `/verify`
  `WS /swipes`). El botón "Enrolar huella" del alta sigue mock; `FingerprintTemplate` sigue siendo `byte[]`
  ZKTeco y no se persiste nada en Fase 3.
- **Apertura de puerta física** tras un `Granted` (`IAccessController.OpenDoorAsync`, AccessBroker). El kiosk
  hoy no abre ninguna puerta; en Fase 3 tampoco. Solo registra el AccessLog.
- **Facturación fiscal** (FiscalBroker / AFIP WSFE): Fase 6.

---

## Orden de implementación (TDD)

Primero lo puro y de bajo riesgo; la UI y la dependencia nueva al final. Un commit por paso (regla 7),
`dotnet test` verde antes de cada uno.

1. **Detector de ráfaga (puro).** `KeyboardWedgeDetector` + `WedgeOptions` + tests. Sin Avalonia, sin UI.
   `feat: [acceso] detector de lector keyboard-wedge por timing`
2. **Resolución de credencial (Application).** `CredentialMatch.LooksLikeDni`, `AccessMethod.Barcode`, rama
   aditiva en `ValidateSwipeUseCase`, `FindMemberByCredentialQuery`; ampliar `GatekeeperTests`. Sin migración.
   `feat: [acceso] resolver credencial de scanner por tag o DNI`
3. **Enrolar credencial.** `TagSerial` en `CreateMemberCommand` + `AssignCredentialCommand` con validación de
   unicidad + (opcional) migración `AddMemberTagSerialUniqueIndex`; tests de handlers e integración.
   `feat: [socios] asignar credencial de acceso al socio`
4. **Glue del scanner en kiosk y POS.** `WedgeKeyboardListener` (Avalonia) + enganche en `CheckInKioskView`
   (`Barcode`) y en `RegisterSaleView` (producto→socio, con `FindProductByBarcodeQuery`). Verificación manual.
   `feat: [acceso] lector HID en kiosk y POS`
5. **Teclado numérico.** `NumericKeypad` `UserControl` + tests de mutación; integrarlo en kiosk y login de
   caja.
   `feat: [ui] teclado numerico en pantalla`
6. **Foto por webcam.** `IPhotoCapture` + `FlashCapPhotoCapture` + `MemberPhotoStore` +
   `UpdateMemberPhotoCommand` + fallback file picker/portapapeles; integrar en alta y ficha; tests de handler
   y de VM con fakes.
   `feat: [socios] foto del socio por webcam con fallback a archivo`

Paso 3 puede introducir una migración EF (índice único): generarla con `dotnet ef migrations add
AddMemberTagSerialUniqueIndex --project src/GymForge.Infrastructure --startup-project
src/GymForge.Infrastructure` (CLAUDE.md). El arranque la aplica con `MigrateAsync()`. Si el usuario opta por
no migrar, el paso 3 queda solo con la validación en el comando.

## Piezas que tocan UI (entra `ux`)

- **Kiosk (A + C):** feedback de que llegó un escaneo; el teclado numérico para carga manual táctil; que el
  scanner no rompa el reset/countdown actual.
- **POS / Caja (A):** feedback del escaneo (producto agregado / socio seleccionado); qué pasa si el código no
  resuelve.
- **Alta y ficha (A + B):** campo "Credencial" + botón "Escanear"; recuadro de foto con preview de cámara,
  botones "Capturar" / "Elegir archivo" / "Pegar", selector de cámara si hay más de una, y el estado "sin
  cámara" que deja solo "Elegir archivo".
- **Login de caja (C):** teclado numérico para el PIN.

## Decisiones a confirmar con el usuario

1. **¿Qué lee el scanner: DNI o credencial propia del gimnasio?** Recomendación: credencial en
   `Member.TagSerial` (carnet/llavero del gimnasio, cualquier lector 1D barato) **con fallback a DNI** si el
   código tiene forma de DNI. Si el gimnasio quiere escanear el **PDF417 del DNI argentino**, hace falta un
   lector **2D imager** y parsear el payload del PDF417 para extraer el número (parseo = fuera de alcance de
   Fase 3; se puede sumar después). ¿Alcanza con credencial+fallback DNI para el piloto?
2. **Librería de webcam: ¿FlashCap?** Es la única dependencia nueva de la fase (managed, sin DLL nativa,
   compatible single-file). Alternativa sin dependencias: enviar solo el fallback "elegir archivo / pegar
   imagen" y diferir la cámara en vivo. Confirmar.
3. **Índice único de credencial (migración opcional).** ¿Se agrega el índice único filtrado sobre
   `(CompanyId, TagSerial)` (recomendado, evita credenciales duplicadas) o alcanza con la validación en el
   comando sin migrar?
4. **Producto por barcode en el POS.** ¿Se incluye en Fase 3 (barato: `Product.Barcode` ya existe) o el POS
   solo resuelve socios y el barcode de producto queda para Fase 4?
5. **Umbrales del detector.** Defaults propuestos: `InterKeyScanMaxMs=50`, `IdleResetMs=300`,
   `MinScanLength=3`, `MaxScanLength=64`. Confirmar o ajustar tras probar con el lector real del gimnasio
   (son un `WedgeOptions`, fáciles de tunear; podrían ir a `AccessSettings` si se quiere configurables, pero
   eso suma alcance).

---

## Consecuencias

- `AccessMethod` suma `Barcode` (string en AccessLog; sin migración). El Gatekeeper gana una rama aditiva de
  resolución; el resto del algoritmo no cambia y los tests existentes quedan verdes.
- `Member.TagSerial` empieza a usarse de verdad (hoy estaba en el modelo pero sin UI). Posible migración
  única del índice (defensiva).
- `CreateMemberCommand` suma un parámetro opcional `TagSerial` (compatible con los llamadores actuales por el
  default). Nuevos comandos/queries chicos: `AssignCredentialCommand`, `FindMemberByCredentialQuery`,
  `FindProductByBarcodeQuery`, `UpdateMemberPhotoCommand`.
- **Única dependencia de paquete nueva: FlashCap** (si el usuario la aprueba). Managed-only → cero impacto en
  el publish single-file self-contained. El fallback no agrega nada.
- Las fotos empiezan a ocupar `%LOCALAPPDATA%\GymForge\fotos\`; ya viajan en el export (DataTransferService),
  ya se reapuntan al importar, y no entran en los backups de DB ni en los recibos. Sin cambios de migración
  de datos.
- `GymForge.Desktop` suma servicios de UI/periférico (`KeyboardWedgeDetector`, `WedgeKeyboardListener`,
  `IPhotoCapture`/`FlashCapPhotoCapture`, `MemberPhotoStore`, `NumericKeypad`). `GymForge.Hardware` queda
  intacto y reservado a los sidecars.
- El kiosk sigue sin abrir puertas físicas y la biometría sigue mock: ambas son Fase 6, con frontera ahora
  documentada.

## Fuera de alcance de Fase 3 (deuda explícita)

- **Biometría ZKTeco real** (BioBroker `/enroll` `/verify`), **apertura de puerta física** (AccessBroker) y
  **facturación fiscal** (FiscalBroker/AFIP): Fase 6.
- **Parseo del PDF417 del DNI argentino** y lectores 2D dedicados: fuera salvo pedido (depende de la decisión 1).
- **Umbrales del wedge configurables por empresa** (hoy `WedgeOptions` con defaults): se puede sumar después.
- **Captura de firma** (`SignatureUrl`): no entra; es análogo a la foto pero no está pedido.
- **Editor completo del socio desde la ficha** (más allá de asignar credencial/foto): es Fase 4.
- **Reconocimiento facial** (`AccessMethod.Facial`): no.

---

## Referencias
- Kiosk: `src/GymForge.Desktop/Views/Checkin/CheckInKioskView.axaml(.cs)` ·
  `src/GymForge.Desktop/ViewModels/Checkin/CheckInKioskViewModel.cs`
- Gatekeeper: `src/GymForge.Application/UseCases/Access/ValidateSwipeUseCase.cs` ·
  `src/GymForge.Domain/Enums/AccessEnums.cs` (`AccessMethod`)
- Credencial del socio: `src/GymForge.Domain/Entities/Member.cs` (`TagSerial`, `EnrollTag`) ·
  `src/GymForge.Infrastructure/Persistence/Configurations/MemberConfiguration.cs` (índice `IX_Members_TagSerial`) ·
  `src/GymForge.Infrastructure/Repositories/MemberRepository.cs` (`FindByTagSerialAsync`, `FindByDocumentAsync`)
- POS: `src/GymForge.Desktop/ViewModels/Cash/RegisterSaleViewModel.cs` ·
  `src/GymForge.Domain/Entities/Product.cs` (`Barcode`)
- Alta / foto: `src/GymForge.Desktop/ViewModels/Members/CreateMemberViewModel.cs` (`CapturePhoto` mock) ·
  `src/GymForge.Desktop/Views/Members/CreateMemberView.axaml` · patrón de imagen a disco:
  `src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs` (`SaveBrandingAsync`, `BrandDir`)
- Migración de datos (fotos ya contempladas): `src/GymForge.Infrastructure/Persistence/DataTransferService.cs`
  (carpetas `brand`/`fotos`; `Reapuntar` de `Members.PhotoUrl`)
- Login por PIN (sin keypad reutilizable hoy): `src/GymForge.Desktop/Views/Cash/CashView.axaml`
- Hardware / sidecars (Fase 6): `src/GymForge.Hardware/Interfaces/` · CLAUDE.md §Sidecars
- File picker reutilizable: `src/GymForge.Desktop/Views/Members/MembersListView.axaml.cs`
- Roadmap/estado: `docs/PROGRESS.md` · `docs/TASKS.md`
