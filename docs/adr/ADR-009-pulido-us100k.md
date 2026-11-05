# ADR-009 — Pulido "US$100k": command palette, branding derivado, micro-interacciones, dashboard con ejes, calendario semanal, limpieza de tokens y accesibilidad (Fase 5)

**Status:** Accepted
**Date:** 2026-11-04
**Deciders:** arqui (planner) · implementa `dev` · UI `ux` (rol central en esta fase) · veta `qa`
**Sesión:** 8 — Fase 5 "Pulido US$100k" del roadmap (`docs/PROGRESS.md`)

---

## Addendum — alcance y decisiones confirmadas (2026-11-04, al pasar a `Accepted`)

- **Alcance de la sesión:** se implementaron las piezas **A, B, C, D, E y G**. La **pieza F (calendario semanal)** queda **diferida a una F5 parte 2** (decisión del usuario).
- **Branding (A):** el usuario eligió **derivar automáticamente un tono de acento legible** (no limitar la paleta a acentos oscuros): `BrandPalette` calcula el tinte + un texto de acento con AA ≥ 4,5:1 para cualquier color de marca, en light y dark.
- **Defaults confirmados:** mapa de tono de `ChargeStatus` (Paid→success; Pending/PartiallyPaid/Overdue→warning; Refunded→danger; Billed→info; Draft/WrittenOff→neutral); **i18n = consistencia de strings es-AR, sin framework** (`.resx`); micro-interacciones sutiles (≤160 ms) + foco visible.
- **Verificación pendiente (qa no puede en headless):** Escape idempotente, Tab cíclico / foco inicial y devolución en los 4 modales, y teclado en grillas quedan para **chequeo manual**; la implementación del Tab cíclico de `ModalHost` es lo más frágil sin probar en vivo (ver `docs/TASKS.md`).

## Context

Las fases 0–4 cerraron la correctitud del core, la robustez de producción, cobranzas, el check-in
premium y la profundidad (reportes, edición de socio, comisiones). La **Fase 5 es mayormente de
diseño/UX**: no agrega features de negocio, sube la **percepción de calidad** de un producto que ya
funciona para que se vea y se sienta como un sistema de US$100k. El rol central es de `ux`, con `dev`
para la lógica (command palette, ejes del dashboard, derivación del tinte, grilla de calendario).

Por qué cada pieza sube la percepción de calidad:

1. **Branding derivado (UX4).** El gimnasio elige su color de acento en Config → Marca y
   `App.ApplyBrandAccent` ya re-tiñe `SystemAccentColor` (y sus variantes Light/Dark) con ese color:
   botones primarios, pills de acento (texto), nav activo y el gráfico del dashboard siguen el color.
   **Pero `AccentTintBrush` es un recurso estático** índigo en `Colors.axaml` (`#1F6366F1` light /
   `#2E818CF8` dark), y de él cuelgan `Border.IconChip`, `Border.Avatar` y el fondo de
   `Border.Pill.accent`, además del bloque de resultado del modal de freeze. Resultado: un gimnasio que
   elige verde o naranja ve chips, avatares y el bloque del modal **en índigo**. Nada grita "plantilla"
   más fuerte que un acento a medias. Es barato y de altísimo impacto.

2. **Limpieza de tokens + AA parejo (UX1, UX6, UX7, UX21).** La consistencia del color es la columna
   vertebral de lo "premium". Hoy conviven dos sistemas: el bueno (`Border.Pill` + `StatusToneConverter`
   con tokens AA light/dark, usado en la ficha y en Reportes) y el viejo (colores literales vía
   `StatusColorConverter`, `ActiveColorBrushConverter`, `ActiveColorConverter`), que pinta pills a
   **~2–2,5:1** (debajo de AA) en Socios, Cobros, Productos, Planes y Dashboard. Unificar en el sistema
   bueno, retirar los tres converters de color y cerrar los huecos AA de los tokens deja todo el producto
   con la misma gramática visual.

3. **Command palette (Ctrl+K).** Es la feature "firma" de un producto de categoría: saltar a cualquier
   sección o disparar una acción sin tocar el mouse. El **cableado ya existe** (`Ctrl+K`, `Escape`,
   `IsCommandPaletteOpen`, `SearchQuery`, botón "Buscar… (Ctrl+K)" en el topbar) pero **no hay overlay ni
   fuente de comandos**: apretar Ctrl+K hoy solo prende un bool que nada renderiza. Falta la UI y la
   lista.

4. **Micro-interacciones + modales coherentes (UX2, UX3, UX17).** El producto ya tiene transiciones en
   las cards interactivas y en el router (CrossFade), pero el foco/hover/press no es parejo:
   `Button.Primary` deshabilitado es casi invisible (UX3), tres (cuatro) modales repiten el chrome del
   overlay a mano y solo el de freeze tiene Escape/foco/Tab (UX2), y la selección de fila de Morosos
   pinta un acento al 70% que arruina los pills (UX17). El pulido de estados coherentes es el pegamento
   de la sensación "cara".

5. **Dashboard con ejes y hover.** El gráfico de recaudación de 30 días ya tiene área + línea y
   **tooltips por día** (barras transparentes con `ToolTip.Tip`), pero **no tiene ejes**: no se lee
   cuánto vale cada punto. Agregarle un eje Y con 3–4 referencias de monto y líneas de guía lo convierte
   de "adorno" en "analítica real".

6. **Calendario semanal.** Clases ya tiene navegación por semana (anterior/siguiente/esta semana,
   `WeekLabel`) y reservas, pero la agenda es un **`ListBox` plano** de horarios. Una **grilla de 7 días**
   con encabezados de fecha y los horarios ubicados por día lee como una agenda profesional. Es la pieza
   más grande y la más aditiva (lo actual funciona), así que se prioriza al final.

7. **Accesibilidad + i18n es-AR (UX8, UX9, UX10, UX16, UX34 + varios).** AA parejo (no solo en los
   pills): hints con `Opacity` en vez de token secundario (UX8), un aviso con `Border` literal en vez de
   `InfoBar` (UX9), feedback de "Copiar" lejos del botón (UX10), paneles sueltos que deberían ser
   `Border.Inset`/`Mono` (UX16), el hint del 0% de comisión invisible (UX34). **i18n es-AR** acá **no es
   un framework de localización** (sería sobre-ingeniería para un producto de un solo locale offline):
   es una **pasada de consistencia de strings** en español rioplatense (voseo parejo, sin mezclar
   tú/usted, terminología consistente "socio"/"cuota"/"sede").

Esta es la guía de implementación para `dev` y `ux`: seguir el orden y las piezas de abajo. `qa` veta.
Alcance acotado a estas siete piezas; lo que queda afuera (incluidas las deudas de otras fases) está en
"Fuera de alcance" con su motivo.

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first / multi-tenant / append-only:** esta fase es **UI pura**. El command palette solo
  **navega** y dispara acciones existentes; el branding solo escribe recursos de estilo; ninguna pieza
  escribe datos nuevos ni cruza tenants. No se tocan `AccessLog`/`AuditLog` ni los global query filters.
- **Sin migración EF.** Ninguna pieza toca el esquema. Si alguna propuesta pidiera schema (no es el caso
  previsto), se marca y no se da por hecho.
- **Tokens, no literales (design system):** todo color sale de `Colors.axaml`/`AppStyles.axaml`; está
  prohibido sumar colores literales nuevos (esta fase, de hecho, **retira** los que quedan). Las clases
  son opt-in (`Border.Card`, `Border.Pill.*`, `TextBlock.FormLabel`, etc.).
- **Contraste AA (criterio del proyecto):** el objetivo es WCAG **AA** — ratio **≥ 4,5:1** para texto
  normal y **≥ 3:1** para texto grande/negrita. El ratio se **calcula a partir de los valores hex** de
  `Colors.axaml` y los tonos Fluent (luminancia relativa), **no se mide sobre captura** (mismo criterio
  que usó `ux` en F1/F2/F3). `qa` verifica el render real con `tools/GymForge.Screenshots` en light y dark.
- **MVVM Toolkit:** `[ObservableProperty]` / `[RelayCommand]`; nada de code-behind salvo lo estrictamente
  de presentación (foco, túnel de teclas), como ya se hace en el modal de freeze y el kiosk.
- `dotnet test` verde antes de cada commit (regla 6). Piso declarado al cierre de la limpieza:
  **1392 tests verdes**. Un commit por feature (regla 7).
- **`.editorconfig` (NEW-8):** es **decisión del usuario**, no se resuelve acá. La limpieza de tokens de
  esta fase **no debe** hacer un barrido de formato masivo ni cambiar la severidad de estilo; se tocan
  solo los archivos de cada pieza.

### Hechos del código verificados al 2026-11-04 (no asumir: ya chequeado)

**Command palette:**
- `MainWindowViewModel` (`src/GymForge.Desktop/Views/Shell/MainWindowViewModel.cs`) ya tiene
  `IsCommandPaletteOpen` (bool), `SearchQuery` (string) y los comandos `OpenCommandPalette` /
  `CloseCommandPalette` (hoy solo prenden/apagan el bool). El `enum NavSection` (11 valores: Dashboard,
  Members, Access, Cash, Collections, Plans, Classes, Routines, Products, Reports, Settings) y
  `NavigateCommand(NavSection)` existen, igual que los eventos de acción rápida del Dashboard
  (`CreateMemberRequested`, `CheckInRequested`, `ChargeRequested`) y `ToggleThemeCommand`.
- `MainWindow.axaml`: `Ctrl+K` → `OpenCommandPaletteCommand` y `Escape` → `CloseCommandPaletteCommand`
  están en `Window.KeyBindings`; el botón `Classes="SearchButton"` del topbar ya invita "Buscar…
  (Ctrl+K)". **Falta el overlay** (no hay nada bindeado a `IsCommandPaletteOpen` en el XAML) y **la lista
  de comandos**. `AppStyles.axaml` ya define `Button.SearchButton` (MinWidth 260) con el comentario
  "Search / command palette button".

**Branding:**
- `App.ApplyBrandAccent(hex)` (`src/GymForge.Desktop/App.axaml.cs:79`) setea
  `FluentAvaloniaTheme.CustomAccentColor`, que recalcula `SystemAccentColor` + `SystemAccentColorLight1/2/3`
  + `SystemAccentColorDark1/2/3`. Se llama al arrancar con `session.BrandColorHex` y desde
  `SettingsViewModel` al cambiar el color. **`AccentTintBrush` NO cuelga de ahí**: es un `SolidColorBrush`
  fijo en las dos `ThemeDictionaries` de `Colors.axaml` (`#1F6366F1` / `#2E818CF8`).
- Consumidores de `AccentTintBrush`: `Border.IconChip` y `Border.Avatar` (background) y
  `Border.Pill.accent` (background) en `AppStyles.axaml`, más el bloque de resultado de
  `FreezeModalView.axaml:60`. El **texto** del pill de acento usa `SystemAccentColor` (pleno), así que
  **ya** sigue el color del gimnasio; el que no lo sigue es el **tinte de fondo**.
- La paleta de swatches de marca vive en Config (`Button.Swatch`); el acento efectivo se persiste en
  `SessionContext.BrandColorHex` con fallback `SessionContext.DefaultBrandColorHex`.

**Tokens / converters:**
- Sistema bueno: `StatusToneConverter` (`Converters/StatusToneConverter.cs`) mapea **`MemberStatus` y
  `MembershipStatus`** a tono (success/warning/danger/info/neutral/accent) y alimenta `Border.Pill.*`
  (tokens AA light/dark). **No cubre `ChargeStatus` ni bool activo/inactivo.**
- Sistema viejo a retirar:
  - `StatusColorConverter` (registrado en `Colors.axaml:8`): usado solo en `MembersListView.axaml:124,131`
    (pills de estado de socio con color pleno + opacidad). Además `ChargesViewModel.StatusColor`
    (`:160-163`) alimenta los pills de `ChargesView.axaml:97,101` con el mismo patrón sub-AA.
  - `ActiveColorBrushConverter` (`ActiveConverters.cs:31`, literales `#3F9C49`/`#E05252` ≈3,5:1 / 3,0:1):
    usado en `DashboardView.axaml:133,135` (pill de tendencia) y `:257` (estado de acceso
    "Granted"/"Denied"). Registrado en `App.axaml:107`.
  - `ActiveColorConverter` (`ActiveConverters.cs:8`, literales `#22C55E`/`#9CA3AF`): usado en
    `ProductsView.axaml:140,144` y `PlansView.axaml:106,110` (pill activo/inactivo + opacidad 0,15, mismo
    patrón sub-AA que UX1). Registrado en `App.axaml:106`.
  - **No** son "legacy" `ActiveLabelConverter`, `ActiveToggleLabelConverter` ni `StringEqualsConverter`
    (son de **texto/lógica**, no de color): **se conservan**.
- Tokens con huecos AA (medidos por `ux` en UX6/UX21): `SuccessBrush` light `#15803D` ≈4,4–5,0:1 (≈4,4
  sobre el lienzo gris, fuera de card); `Button.Danger` texto blanco sobre `DangerBrush` dark ≈2,8:1;
  `Button.Primary` blanco sobre el acento ≈4,47:1 (a 0,03 de AA a 14 px). `DangerTextBrush` y `NeutralBrush`
  ya están a AA (cerrados en F1/F2).

**Dashboard:**
- `DashboardView.axaml` dibuja el gráfico en un `Viewbox Stretch="Fill"` sobre un `Canvas` virtual
  300×130 (área con `RevenueAreaGeometry`, línea con `RevenueLineGeometry`, ambas con
  `SystemAccentColor`), con un `ItemsControl` de 30 barras transparentes (`RevenueBars`, `RevenueBarVm`)
  encima para los tooltips por día (`ToolTip.Tip="{Binding Tooltip}"`). Ya existen `Revenue30DaysTotal`,
  `ChartFromLabel`, `ChartToLabel`. **No hay eje Y ni líneas de guía ni etiquetas de monto.** Ojo: las
  etiquetas de texto **no pueden ir dentro del `Viewbox`** (se deforman al estirar); van en un gutter
  fijo a la izquierda del plot.

**Clases (calendario):**
- `ClassesView.axaml`: navegación de semana (`PrevWeekCommand`/`NextWeekCommand`/`ThisWeekCommand`,
  `WeekLabel`), `Schedules` (`ScheduleRowDto` con `ClassName`, `Start`, `Occupancy`) renderizados como un
  `ListBox` plano, `SelectedSchedule`/`HasSelection` y el panel derecho de reservas (`Bookings`,
  `BookMemberCommand`, `CancelBookingCommand`). La lógica de reservas y de navegación **se reutiliza tal
  cual**; falta solo la **proyección por día de la semana** y su render en grilla.

**Modales (UX2):**
- `FreezeModalView.axaml` es el modal "modelo": `KeyboardNavigation.TabNavigation="Cycle"`, `IsCancel`
  (Escape), `IsDefault` (Enter), foco inicial en code-behind, `ScrollViewer` de reserva. Los otros
  (`ChargesView`, `CashView`→`RegisterSaleView`, `MemberDetailView`, `CollectionsView` que reusa
  `PaymentModalView`) repiten a mano el `Border` del overlay (440 / radio 12 / padding 24 / sombra
  literal) y **no** tienen Escape/foco/Tab parejos.

---

## Decisión — resumen

| # | Pieza | Qué toca | `ux` / `dev` | Tokens / converters | AA | Migración | Tamaño |
|---|---|---|---|---|---|---|---|
| A | **Branding derivado** (UX4) | `AccentTintBrush` pasa a derivarse del acento del gimnasio en `ApplyBrandAccent` | `dev` (+`ux` valida) | `AccentTintBrush` (deja de ser estático) | Decisión de paleta AA-safe (ver pieza) | No | **S** |
| B | **Limpieza de tokens + converters** (UX1, UX6, UX7, UX21, UX30, UX32) | Unificar pills en `Border.Pill`+`StatusTone`; retirar `StatusColorConverter`, `ActiveColorBrushConverter`, `ActiveColorConverter`; cerrar huecos AA | `dev`+`ux` | retira 3 converters de color; `StatusTone` suma `ChargeStatus` y bool activo | Sube pills a AA; `SuccessBrush`/`Button.Danger`/`Button.Primary` | No | **M** |
| C | **Command palette (Ctrl+K)** | Overlay + fuente de comandos (NavSection + acciones) + filtrado + teclado | `dev` (lógica) + `ux` (overlay) | reusa `Border.ModalCard` (pieza D) | AA en scrim + card + lista | No | **M** |
| D | **Micro-interacciones + modales** (UX2, UX3, UX17) | `Border.ModalCard` + host común (Escape/foco/Tab) en los 4 modales; `:disabled` de `Button.Primary`; focus-visible parejo; selección de `DataGrid` | `ux` (+`dev` para el host de modal) | `Border.ModalCard` nuevo; estilos `:disabled`/`:focus` | foco visible AA | No | **M** |
| E | **Dashboard con ejes y hover** | Eje Y (3–4 referencias de monto) + líneas de guía; hover ya existe, se enriquece | `dev` (VM: ticks) + `ux` (render) | `TextBlock.Caption`/`Numeric`; `Border.Divider` para guías | AA en labels de eje | No | **M** |
| F | **Calendario semanal (Clases)** | Grilla de 7 días con encabezados de fecha + horarios por día; reutiliza reservas y navegación | `ux` (grilla) + `dev` (proyección por día) | clases existentes | AA en encabezados/chips | No | **L** (fase 2 / último) |
| G | **Accesibilidad + i18n es-AR** (UX8, UX9, UX10, UX16, UX34, UX15b/c) | `Opacity`→`FormLabel`; aviso PIN→`InfoBar`; "Copiado" inline; propagar `Inset`/`Mono`; hint 0% visible; teclado en grillas; consistencia de strings es-AR | `ux` (+`dev` puntual: UX10, UX34) | reusa `FormLabel`/`Inset`/`Mono`/`InfoBar` | cierra huecos AA fuera de card | No | **M–L** |

### Decisiones transversales (leer antes del detalle)

- **Cero migración EF.** Toda la Fase 5 es Desktop/UI. Multi-tenant y append-only intactos. Si en la
  implementación apareciera una necesidad de schema (no se prevé), se frena y se decide; no se da por
  hecho.
- **No se suman colores literales; se retiran los que quedan.** El norte de la pieza B es que el 100% del
  color salga de tokens. Al terminar B, `grep` de `#RRGGBB` en las vistas debería quedar solo en
  superficies con literal justificado y documentado (kiosk oscuro fijo, máscaras de gradiente del
  gráfico), no en pills ni estados.
- **El acento del gimnasio ya fluye por `SystemAccentColor`.** La pieza A no reinventa el branding: solo
  hace que el **tinte** (`AccentTintBrush`) acompañe al acento que ya se aplica. Todo lo demás
  (`Button.Primary`, nav activo, línea/área del gráfico, texto de pill de acento) **ya** sigue el color.
- **`ux` es el rol central de la fase.** `dev` entra en lo que es lógica: derivación del tinte (A), host
  del command palette y su fuente de comandos (C), host común de modal (D), cómputo de ticks del eje (E),
  proyección por día (F), y los dos puntos de VM de G (UX10, UX34). Todo lo demás es XAML/estilos de `ux`.

---

## Priorización realista

La Fase 5 es grande. **No hace falta meter todo** para lograr el salto de percepción. Orden honesto:

- **Imprescindible para el "look US$100k" (MUST):** **A** (branding derivado), **B** (limpieza de tokens
  + AA), **C** (command palette), **D** (micro-interacciones + modales), **E** (dashboard con ejes). Estas
  cinco son las que un evaluador nota en los primeros 60 segundos.
- **Alto valor, puede ir después del MUST (SHOULD):** **G** (accesibilidad + i18n es-AR). Es un barrido
  parejo que se puede hacer incremental; los ítems son chicos e independientes.
- **Puede quedar para un pulido posterior (COULD / fase 2 de F5):** **F** (calendario semanal). Es la
  pieza más grande y **aditiva** — la agenda actual funciona. Recomendación: si el tiempo aprieta, cerrar
  F5 con A–E + G y dejar F como primer ítem de una F5-parte-2, en vez de entregar las siete a medias.

Guía de "suficiente": con **A + B + C + E** el producto ya se ve de otra categoría; **D** lo hace sentir
sólido; **G** empareja los bordes; **F** es el lujo final.

---

## Pieza A — Branding derivado (`AccentTintBrush`) — UX4

### Qué falta
Que el **tinte** de acento (fondo de chips, avatares, pills de acento y el bloque del modal de freeze)
acompañe el color que el gimnasio eligió, en light y dark, sin perder AA.

### Diseño
`AccentTintBrush` deja de ser un recurso estático y se **deriva del acento efectivo** cuando se aplica la
marca. En `App.ApplyBrandAccent(hex)` (el único punto que ya resuelve el color, con fallback), además de
setear `CustomAccentColor`, calcular el tinte y **escribirlo en las dos ThemeDictionaries** (light y dark)
como `SolidColorBrush`:

- **Derivación = acento a una opacidad fija por tema** (mezcla aditiva sobre la card es equivalente, pero
  el alfa es más simple y es lo que ya hace el token hoy): tomar `color` (R,G,B del acento) y construir
  `Color.FromArgb(alphaLight, R, G, B)` para el dict Light y `Color.FromArgb(alphaDark, R, G, B)` para el
  Dark. **Mantener los alfa actuales** (`0x1F` ≈ 12% light, `0x2E` ≈ 18% dark): ya están calibrados para
  que el chip/pill se lea sobre la card en cada tema. Así, con índigo el resultado es idéntico al de hoy
  (sin regresión) y con cualquier otro acento el tinte lo acompaña.
- Implementación: en `ApplyBrandAccent`, tras resolver `color`, hacer
  `SetThemeBrush("Light", 0x1F, color)` y `SetThemeBrush("Dark", 0x2E, color)` sobre las
  `ResourceDictionary.ThemeDictionaries` del merged dict de `Colors.axaml` (o exponer un
  `IBrandingService`/método estático chico que `App` y `SettingsViewModel` compartan). Los consumidores
  (`IconChip`, `Avatar`, `Pill.accent`, modal de freeze) usan `{DynamicResource AccentTintBrush}`, así que
  **se re-tiñen solos** al reemplazar el recurso.
- `Colors.axaml` conserva los dos `AccentTintBrush` **índigo como valor por defecto** (para el diseñador
  en tiempo de diseño y como fallback si nunca se aplica marca). El runtime los pisa.

### Accesibilidad (criterio AA)
El **texto/ícono** sobre el tinte de acento usa `SystemAccentColor` **pleno** (pill de acento, iniciales
del avatar, ícono del chip). Con el tinte al 12–18% de un acento **oscuro** (índigo, verde oscuro, bordó)
el ratio texto-pleno/tinte queda ≈4,5:1+. **Riesgo:** un acento **claro** (amarillo, celeste pastel) deja
el texto pleno claro sobre un tinte claro < AA. **Decisión a confirmar:** la **paleta de swatches de marca
debe limitarse a acentos AA-safe** (suficientemente oscuros para servir como texto sobre su propio tinte),
o bien el texto sobre el tinte usar un "acento legible" derivado (oscurecer el acento para texto). Para v1,
lo simple y correcto es **curar la paleta de swatches** (decisión 1); si el usuario quiere acentos claros,
pasa a derivar un tono de texto (más trabajo, pieza aparte).

### Tests / verificación
- `tests/GymForge.Desktop.Tests`: un test de la derivación (dado un hex, el `AccentTintBrush` resultante
  tiene el ARGB esperado en light y dark; un hex inválido cae al índigo por defecto).
- `qa`: `tools/GymForge.Screenshots` en light y dark con **dos acentos distintos** (p. ej. índigo y verde)
  — los chips del Dashboard, los avatares y el bloque del modal de freeze deben acompañar el color.

---

## Pieza B — Limpieza de tokens y converters legacy (UX1, UX6, UX7, UX21, UX30, UX32)

### Qué falta
Unificar los pills/estados en el único sistema AA (`Border.Pill` + `StatusToneConverter`), **retirar los
tres converters de color** y cerrar los huecos AA de los tokens.

### Diseño
1. **`StatusToneConverter` suma los casos que faltan** para poder retirar los converters viejos:
   - `ChargeStatus` → tono (p. ej. Paid→success, Pending→warning/neutral, Overdue→warning, Void/Refunded→
     danger/neutral; `ux`/`dev` fijan el mapa con el criterio ya usado para membresías).
   - `bool` activo/inactivo → `success`/`neutral` (para Productos y Planes), o un converter de tono chico
     equivalente. Así `ActiveColorConverter` queda sin uso.
2. **Migrar los pills a `Border.Pill` + `Classes.<tono>="{Binding …, Converter={StaticResource StatusTone}}"`:**
   - `MembersListView.axaml:116-137` (UX1): reemplazar el color pleno + opacidad por `Border.Pill` con los
     seis `Classes.<tono>` (patrón idéntico al de `MemberDetailView.axaml:82-87`).
   - `ChargesView.axaml:92-107` (UX1): idem; **borrar** `ChargesViewModel.StatusColor` (`:155-163`).
   - `ProductsView.axaml:140-144` y `PlansView.axaml:106-110`: pills activo/inactivo a `Border.Pill`
     success/neutral.
   - `DashboardView.axaml:131-136` (UX7, pill de tendencia) y `:256-257` (estado de acceso): a
     `Border.Pill.success/danger` + `TextBlock.success/danger`.
3. **Retirar los converters y sus registros:**
   - `StatusColorConverter` (`Converters/StatusColorConverter.cs` + registro `Colors.axaml:8`).
   - `ActiveColorBrushConverter` (clase en `ActiveConverters.cs:31` + registro `App.axaml:107`).
   - `ActiveColorConverter` (clase en `ActiveConverters.cs:8` + registro `App.axaml:106`).
   - **Conservar** `ActiveLabelConverter`, `ActiveToggleLabelConverter`, `StringEqualsConverter` (texto/
     lógica, no color).
4. **Cerrar huecos AA de tokens (UX6, UX21):** `SuccessBrush` light `#15803D` → `#166534` (≈ sube a AA
   sobre el lienzo gris, fuera de card); texto oscuro para `Button.Danger` en dark (blanco sobre
   `DangerBrush` da ≈2,8:1); y el pill `success`/`danger` que aún use el brush pleno pasa a su `*TextBrush`
   (patrón de `DangerTextBrush`). `Button.Primary` blanco sobre el acento queda a 0,03 de AA a 14 px:
   aceptar (es texto grande/negrita, pasa a 3:1) o subir el peso; decisión menor de `ux`.
5. **Propagación del design system (UX30, UX32):** alinear montos a la derecha con cifras tabulares en las
   tablas (ya hay `TextBlock.Numeric`/`+tnum`), y propagar `KpiLabel`/`KpiValue` a los KPIs del Dashboard
   (hoy usan tamaños inline equivalentes). Es consolidación, no colores nuevos.

### Accesibilidad (criterio AA)
Todos los pills quedan sobre su tinte con texto en el `*Brush`/`*TextBrush` tokenizado (≥4,5:1 a 11 px).
Fuera de card, `SuccessBrush` sube a AA. Verificación por cálculo sobre los hex + captura de `qa`.

### Tests / verificación
- `tests/GymForge.Desktop.Tests`: `StatusToneConverter` cubre `ChargeStatus` y bool (un estado → un solo
  tono). Tests de wiring (`DesktopWiringTests`) siguen verdes tras borrar converters (nada los referencia).
- `qa`: capturas de Socios, Cobros, Productos, Planes y Dashboard en light y dark; ningún pill por debajo
  de AA; `grep` de literales de color en vistas acotado a lo documentado.

---

## Pieza C — Command palette (Ctrl+K)

### Qué falta
El overlay y la fuente de comandos. El cableado (atajo, bool, botón) ya está.

### Diseño — overlay simple, sin sobre-ingeniería
- **Overlay:** un `Panel`/`Border` que ocupa toda la columna de contenido (o toda la ventana) con un
  **scrim** semitransparente (`#99000000` sobre un token, AA-neutral) y, centrada arriba (~15% desde el
  tope), una **card** (`Border.ModalCard` de la pieza D, MaxWidth ~560) con: un `TextBox` de búsqueda
  arriba (ícono de lupa, watermark "Escribí para buscar una sección o acción…") y debajo un `ListBox`
  scrolleable de resultados. Se renderiza en `MainWindow.axaml` con `IsVisible="{Binding IsCommandPaletteOpen}"`.
- **Fuente de comandos (en `MainWindowViewModel`):** una lista fija construida una vez, de un tipo chico
  `PaletteCommand(string Title, string? Group, string Icon, Action Run)` (o `Func<Task>`):
  - **Navegación:** un comando por cada `NavSection` (Title = la etiqueta ya usada en el sidebar: "Socios",
    "Caja", "Morosos", "Planes", "Clases", "Rutinas", "Productos", "Reportes", "Configuración",
    "Dashboard", "Accesos"), Group "Ir a", `Run = () => Navigate(section)`. El ícono reusa el `Symbol` del
    sidebar.
  - **Acciones rápidas:** "Nuevo socio" (`OpenCreateMember`), "Registrar check-in" (`Navigate(Access)`),
    "Cobrar" (`Navigate(Cash)`), "Cambiar tema" (`ToggleTheme`). Group "Acciones". Son las que ya existen
    como comandos/eventos; **no se inventan acciones nuevas**.
- **Filtrado:** `SearchQuery` filtra por `Contains` **insensible a mayúsculas y a acentos** (normalizar
  con `string.Normalize`/quitar diacríticos, como ya hace el parser de CSV y la búsqueda de socios) sobre
  `Title`. Sin ranking fuzzy en v1 (simplicidad > cleverness). Lista vacía → estado vacío "Sin resultados".
  Exponer `FilteredCommands` (ObservableCollection) y `SelectedCommand`.
- **Teclado y foco:** al abrir (Ctrl+K o el botón), **foco al `TextBox`** (code-behind de presentación,
  como el modal de freeze) y preseleccionar el primer resultado; ↑/↓ mueven la selección en el `ListBox`;
  **Enter** ejecuta `SelectedCommand.Run()` y cierra; **click** en una fila ejecuta y cierra; **Escape**
  cierra (ya cableado). `KeyboardNavigation.TabNavigation="Cycle"` dentro de la card. Al cerrar, **resetear
  `SearchQuery`** y devolver el foco al contenido.
- **`ExecuteCommand`** (nuevo `[RelayCommand]`): corre el `Run` y llama `CloseCommandPalette()`.

### Accesibilidad (criterio AA)
`AutomationProperties.Name` en la card ("Paleta de comandos"); los resultados son un `ListBox` navegable
por lector de pantalla; scrim y card a AA; el foco entra y sale limpio (no queda atrapado al cerrar).

### Riesgo a verificar (no bloqueante)
Hoy `Escape` es un `KeyBinding` a nivel ventana que llama `CloseCommandPalette` **siempre**. Con un modal
abierto (freeze usa `IsCancel=Escape`), hay que confirmar que Escape cierra el modal correcto y no hay
doble efecto. `CloseCommandPalette` es idempotente (setear false), así que el riesgo es bajo, pero `qa`
debe verificar Escape con: (a) palette abierto, (b) modal de freeze abierto, (c) nada abierto.

### Tests / verificación
- `tests/GymForge.Desktop.Tests/MainWindowViewModelTests` (nuevo o ampliar): abrir prende
  `IsCommandPaletteOpen` y puebla comandos; filtrar por texto (con acentos) reduce la lista; ejecutar un
  comando de navegación cambia `CurrentSection` y cierra; cerrar resetea `SearchQuery`.
- `qa`: runtime con `tools/GymForge.Screenshots` (palette abierto, filtrado, vacío) en light y dark;
  teclado real (↑/↓/Enter/Escape) — con F3-D1 abierto, el check de teclado va también al tool de capturas.

---

## Pieza D — Micro-interacciones + unificación de modales (UX2, UX3, UX17)

### Qué falta
Estados hover/press/**focus** coherentes, un chrome de modal único con Escape/foco/Tab, y arreglar la
selección de fila de Morosos.

### Diseño
- **`Border.ModalCard` (UX2):** extraer una clase en `AppStyles.axaml` con el chrome del overlay (fondo de
  card, radio 12–14, padding 24, **borde `SurfaceBorderBrush`** —en dark la sombra no se ve y el borde
  define la card— y `BoxShadow` tokenizada). Migrar las cuatro copias a mano: `ChargesView`, `CashView`
  (`RegisterSaleView`), `MemberDetailView` (overlay final) y `CollectionsView` (reusa `PaymentModalView`).
  Llevar a `PaymentModalView` y `RegisterSaleView` el **comportamiento del modal de freeze**: `IsCancel`
  (Escape), foco inicial, `KeyboardNavigation.TabNavigation="Cycle"` y `ScrollViewer` de reserva. El
  command palette (pieza C) reusa `Border.ModalCard`.
- **`Button.Primary:disabled` (UX3):** definir un estado `:disabled` propio (acento atenuado + texto con
  contraste suficiente) en `AppStyles.axaml`, en vez de la plantilla Fluent que lo deja casi invisible.
  Afecta a todos los modales.
- **Focus-visible parejo:** asegurar un anillo de foco visible y consistente en `Button.Primary/Secondary/
  Ghost/Danger` y `NavButton` (Fluent lo trae; verificar que las clases opt-in no lo pisen). Es el detalle
  que más "se siente" al operar con teclado.
- **Transiciones coherentes:** el producto ya tiene `BoxShadowsTransition`/`BrushTransition` en
  `Border.Card.Interactive` y `CrossFade` en el router. Agregar transiciones **sutiles y parejas** de
  background/opacity en hover/press de botones donde falten, sin animaciones largas (≤ 160 ms, como las
  existentes). Nada de rebotes ni efectos; coherencia, no show.
- **Selección de `DataGrid` (UX17):** quitar/suavizar el fondo de fila seleccionada (hoy acento al ~70% que
  arruina los pills) en el estilo global de `DataGrid` en `AppStyles.axaml`; Morosos/Cobros/Socios/
  Productos no usan selección semántica (las acciones son por botón). Además, margen inferior al stack de
  avisos de Morosos (`CollectionsView`).

### Accesibilidad (criterio AA)
Foco visible en todos los controles accionables; `:disabled` legible (texto ≥ 3:1); Escape/Enter/Tab
operables en los 4 modales. Verificación con teclado por `qa`.

### Tests / verificación
- `tests/GymForge.Desktop.Tests`: los modales con `IsCancel`/foco no rompen bindings existentes (humo).
- `qa`: capturas de los 4 modales en light y dark (incluido `:disabled` del primario), Morosos sin el
  acento al 70%, y recorrido de teclado (Tab cíclico, Escape, Enter) en cada modal.

---

## Pieza E — Dashboard con ejes y hover

### Qué falta
El gráfico de recaudación no tiene ejes ni referencias de monto; el hover (tooltips por día) ya existe.

### Diseño — atado a lo que ya hay
- **Estructura:** envolver el plot en un `Grid ColumnDefinitions="Auto,*"`: columna izquierda = **gutter
  de eje Y** (etiquetas de monto, **fuera del `Viewbox`** para que no se deformen), columna derecha = el
  `Viewbox`+`Canvas` actual con el área/línea y las barras de tooltip (sin cambios de fondo).
- **Eje Y (VM → `dev`):** calcular 3–4 **ticks "lindos"** (0 y el máximo redondeado hacia arriba a una
  cifra redonda, con 1–2 intermedios) a partir del máximo de `RevenueBars`. Exponer `YAxisTicks`
  (etiquetas formateadas `$N0` es-AR + su posición relativa 0..1). `ux` las renderiza en el gutter
  (`TextBlock.Caption`/`Numeric`, alineadas a la derecha) y dibuja **líneas de guía** horizontales suaves
  en el plot (`Border.Divider`/stroke tokenizado, por detrás del área).
- **Hover (ya existe, se enriquece):** mantener los tooltips por día; opcionalmente resaltar el día bajo
  el cursor (marcador/guía vertical). El eje X ya tiene `ChartFromLabel`/`ChartToLabel`; se conservan.
- Sin dependencias nuevas (nada de LiveCharts/OxyPlot): se mantiene el gráfico XAML nativo, coherente con
  la decisión previa del proyecto.

### Accesibilidad (criterio AA)
Etiquetas de eje en secundario/terciario con contraste AA (misma regla que los labels del gráfico hoy, que
usan terciario ≈ límite — usar `Caption` secundario si no llega). Las cifras siguen siendo tabulares.

### Tests / verificación
- `tests/GymForge.Desktop.Tests`: el cálculo de ticks (dado un máximo, devuelve ticks redondos esperados;
  caso sin recaudación → eje 0). 
- `qa`: capturas del Dashboard con eje en light y dark; tooltip por día sigue funcionando.

---

## Pieza F — Calendario semanal (Clases) — **última prioridad / fase 2 de F5**

### Qué falta
Que la agenda semanal se vea como un calendario (7 columnas por día) en vez de una lista plana.

### Diseño — reutiliza navegación y reservas
- **Proyección (VM → `dev`):** sobre los `Schedules` ya cargados de la semana, exponer una agrupación
  **por día de la semana** (Lun–Dom con su fecha), cada grupo ordenado por hora. No cambia la carga ni la
  navegación (`PrevWeek`/`NextWeek`/`ThisWeek`/`WeekLabel` se reutilizan), ni la lógica de reservas
  (`SelectedSchedule`, `Bookings`, `BookMemberCommand`, `CancelBookingCommand`).
- **Grilla (XAML → `ux`):** `UniformGrid`/`Grid` de 7 columnas con encabezado de día+fecha arriba y, en
  cada columna, los horarios del día como **chips/cards chicas** (clase + hora + `Pill` de ocupación),
  clickeables para seleccionar (alimentan el panel derecho de reservas, que se conserva). Estado vacío por
  día ("—") y de semana sin horarios (ya existe `IsEmpty`).
- **MVP vs. lujo:** v1 = 7 columnas con horarios **apilados cronológicamente** por día (sin posicionar por
  altura de hora): evita la matemática de layout de un timeline y ya lee como calendario. El **timeline
  con filas de hora** (posición vertical por horario) queda como refinamiento posterior si se quiere.
- Como es aditivo y la lista actual funciona, **si el tiempo aprieta, F sale de esta sesión** y abre una
  F5-parte-2.

### Tests / verificación
- `tests/GymForge.Desktop.Tests`: la proyección agrupa por día y ordena por hora; semana sin horarios →
  grilla vacía; seleccionar un horario de la grilla alimenta el panel de reservas.
- `qa`: capturas de Clases con la grilla (semana con y sin horarios) en light y dark; reserva/cancelación
  siguen funcionando.

---

## Pieza G — Accesibilidad + i18n es-AR (UX8, UX9, UX10, UX16, UX34, UX15b/c)

### Qué falta
Un barrido parejo de accesibilidad y de consistencia de strings. Son ítems chicos e independientes.

### Diseño
- **UX8** — migrar los `Opacity="0.6/0.7"` de hints (≈45 usos en 14 vistas) a `Classes="FormLabel"`
  (secundario tokenizado, AA), para no depender del fondo. `TextBlock.Caption` (terciario) **no** para
  texto informativo.
- **UX9** — el aviso de PIN de fábrica en Config (`SettingsView.axaml`, card Seguridad): de `Border`
  literal + texto `DangerBrush` a `fa:InfoBar` Warning con `LiveSetting` (igual que el aviso de licencia y
  el tope de freeze).
- **UX10** — feedback de "Copiar" (código de esta PC) **inline, junto al botón**: exponer
  `ThisPcCodeCopied` (bool) en `SettingsViewModel` y mostrar "Copiado" al lado de "Copiar"; `LicenseMessage`
  queda solo para la activación (`dev`).
- **UX16** — propagar `Border.Inset` (paneles hundidos) y `TextBlock.Mono` (valores técnicos) a los
  `Border … Background=SubtleFill…` sueltos y a los `FontFamily="Consolas,…"` inline (lista de rutas en el
  ticket: `CreateMemberView:221,247`, `MemberDetailView:300`, `FreezeModalView:24`, `PaymentModalView:18,32`,
  `RegisterSaleView:93`, código de esta PC/origen de updates en Config).
- **UX34** — hint visible del 0% en Productos (form y columna "% comisión"): el texto que hoy vive solo en
  `AutomationProperties.Name` pasa a texto visible ("si es 0%, rige el % general del vendedor") (`dev`+`ux`).
- **UX15 (b/c)** — unificar `Header=""`/`Header="Acciones"` de las columnas de botones en las grillas y
  **verificar con teclado** que los botones por fila se alcanzan con Tab y se activan con Enter/Espacio,
  con foco visible (`qa`). (UX15a, ancho de Morosos, queda fuera: ver "Fuera de alcance".)
- **i18n es-AR (consistencia, NO framework):** pasada de strings visibles para dejar **voseo parejo**
  (evitá/registrá/guardá), sin mezclar tú/usted, y terminología consistente ("socio", "cuota", "sede",
  "membresía"). **No se introduce `.resx` ni un sistema de localización** (sobre-ingeniería para un locale
  único offline — decisión 4). Entregable: un barrido + una guía corta de términos en `docs/` si `ux` lo
  ve útil (no obligatorio).

### Accesibilidad (criterio AA)
Cierra los huecos fuera de card (hints en secundario, no `Opacity`); avisos como `InfoBar` con
`LiveSetting`; teclado operable en grillas. Todo verificado por cálculo + captura.

### Tests / verificación
- `tests/GymForge.Desktop.Tests`: `ThisPcCodeCopied` cambia al copiar (UX10).
- `qa`: capturas de Config (aviso PIN como InfoBar, "Copiado" inline), Productos (hint 0% visible), y
  recorrido de teclado en las grillas; revisión de strings es-AR.

---

## Orden de implementación

Primero lo que da el salto de percepción y lo que es base para el resto; `ux` hace lo visual, `dev` la
lógica. Un commit por feature (regla 7), `dotnet test` verde antes de cada uno. **Ninguna migración.**

1. **Pieza A — Branding derivado** (`dev`). Derivar `AccentTintBrush` en `ApplyBrandAccent` + test +
   decisión de paleta AA-safe. Desbloquea que todo lo visual siguiente se vea correcto por-gimnasio.
   `feat: [marca] tinte de acento derivado del color del gimnasio`
2. **Pieza B — Limpieza de tokens + converters** (`dev`+`ux`). `StatusTone` suma ChargeStatus/bool; migrar
   pills; retirar los 3 converters de color; cerrar huecos AA; propagar KPI/alineación. Base limpia.
   `feat: [ui] unificar pills y estados en tokens AA; retirar converters de color`
3. **Pieza D — Micro-interacciones + modales** (`ux`+`dev`). `Border.ModalCard` + host común (Escape/foco/
   Tab) en los 4 modales; `:disabled` del primario; foco visible; selección de DataGrid. Deja el
   `Border.ModalCard` listo para C.
   `feat: [ui] chrome de modal unificado, foco/estados coherentes`
4. **Pieza C — Command palette** (`dev` lógica + `ux` overlay). Overlay sobre `Border.ModalCard`; fuente de
   comandos (NavSection + acciones); filtrado sin acentos; teclado y foco.
   `feat: [ui] paleta de comandos (Ctrl+K)`
5. **Pieza E — Dashboard con ejes y hover** (`dev` VM + `ux` render). Gutter de eje Y + ticks + guías;
   hover existente conservado.
   `feat: [dashboard] ejes y guias en el grafico de recaudacion`
6. **Pieza G — Accesibilidad + i18n es-AR** (`ux` + `dev` puntual). Barrido UX8/9/10/16/34/15 + strings.
   `feat: [ui] accesibilidad AA pareja y consistencia es-AR`
7. **Pieza F — Calendario semanal** (`ux`+`dev`). Grilla de 7 días + proyección por día. **Opcional / fase
   2 de F5** si el tiempo aprieta.
   `feat: [clases] agenda semanal en grilla de 7 dias`

> **`ux` entra** en todas salvo donde es solo lógica (A, el cómputo de ticks de E, la fuente de comandos de
> C, los dos puntos de VM de G). **Verificación (`qa`):** tests verdes + runtime con
> `tools/GymForge.Screenshots` en **light y dark** para cada pieza; **contraste AA** calculado sobre los
> hex de `Colors.axaml`; recorrido de teclado (command palette, modales, grillas). Con F3-D1 abierto
> (checks de teclado fuera de CI), todo cambio de teclado/visual se corre también con el tool de capturas.

## Piezas que tocan UI (entra `ux`)
- **A:** validar el tinte derivado en los chips/avatares/pill de acento/modal de freeze, light y dark.
- **B:** migración de pills a `Border.Pill`+`StatusTone` en 5 vistas; huecos AA de tokens; KPI/alineación.
- **C:** overlay del command palette (card centrada arriba, scrim, lista de resultados, estado vacío).
- **D:** `Border.ModalCard`, `:disabled` del primario, foco visible, selección de DataGrid, los 4 modales.
- **E:** render del eje Y, líneas de guía y etiquetas del gráfico.
- **F:** grilla de 7 días de Clases.
- **G:** UX8/9/10/16/34/15 + strings es-AR.

## Deuda de UI/UX: qué entra en Fase 5 y qué no

**Entra (Fase 5):** UX1, UX2, UX3, UX4, UX6, UX7, UX8, UX9, UX10, UX16, UX17, UX21, UX30, UX32, UX34,
UX15(b) (header "Acciones") y UX15(c) (teclado en grillas). Más, por ser el **mismo patrón legacy** que
UX1/UX7: retirar `ActiveColorConverter` (pills activo/inactivo de Productos y Planes).

**No entra, con motivo:**
- **UX11, UX12** — origen de updates / "Reiniciar tras Después": son **decisión** y comportamiento de
  auto-update (deuda de F1), no pulido visual.
- **UX13** — lógica del editor de cobranzas (restaurar textos, contador): **feature de producto** de F2.
- **UX14, UX26** — estado vacío tras error de carga (Morosos/Reportes): **lógica de VM** por pantalla;
  candidato a una pasada de consistencia posterior ("vacío vs. error"), no driver del "look US$100k".
- **UX15(a)** — ancho mínimo de la grilla de Morosos: layout menor.
- **UX19 / F3-D2, UX22, UX23, UX24** — primer carácter del lector, huecos del kiosk y foto: **deuda de F3**
  (hardware/funcional), no pulido visual de esta fase.
- **UX25, UX27, UX28, UX29, UX31, UX33** — pulido específico de la pantalla de Reportes (estados vacíos por
  tabla, doble carga, labels, columnas duplicadas, volumen): deuda de F4 parte 1; se deja para una pasada
  de Reportes aparte. (UX30 y UX32 **sí** entran por ser propagación del design system.)
- **i18n como framework (.resx/localización):** fuera (sobre-ingeniería para un locale único) — decisión 4.
- **NEW-8 (`.editorconfig`):** **decisión del usuario**, no se toca acá; la limpieza de tokens no hace
  barrido de formato ni cambia severidad.

## Decisiones a confirmar con el usuario
1. **Paleta de marca AA-safe (pieza A).** El tinte derivado funciona en light y dark para acentos
   **oscuros**; un acento **claro** deja el texto de acento sobre su tinte por debajo de AA. Propuesta:
   **limitar los swatches de marca a acentos oscuros AA-safe**. ¿Lo dejamos así, o querés permitir acentos
   claros (implica derivar un tono de texto legible, más trabajo)?
2. **Alcance de Fase 5 en esta sesión.** Propuesta: entregar **A–E + G** (el salto de percepción) y dejar
   **F (calendario semanal)** como primer ítem de una F5-parte-2, por ser la pieza más grande y aditiva.
   ¿Lo aceptás, o querés meter F sí o sí en esta sesión?
3. **Mapa de tono de `ChargeStatus` (pieza B).** Propuesta: Paid→success, Pending→warning, Overdue→warning,
   Void→neutral, Refunded→danger (ajustable). ¿Confirmás o preferís otro criterio de color para cobros?
4. **i18n = consistencia, no framework (pieza G).** Propuesta: barrido de strings es-AR (voseo, términos)
   **sin** introducir `.resx`/localización, porque el producto es de un solo locale offline. ¿De acuerdo,
   o querés dejar preparada la base para multi-idioma (mucho más trabajo, sin demanda hoy)?
5. **Micro-interacciones: alcance (pieza D).** Propuesta: transiciones **sutiles y cortas** (≤160 ms),
   foco visible parejo y modales coherentes; **sin** animaciones de entrada/rebote. ¿Alcanza, o querés
   animaciones de transición más marcadas (riesgo de verse "demo", no "premium")?

---

## Consecuencias
- **Esquema:** ninguna migración. Multi-tenant y append-only intactos (la fase no escribe datos nuevos).
- **Branding:** el acento del gimnasio tiñe de verdad **toda** la UI (chips, avatares, pills de acento,
  modal de freeze), no solo botones y gráfico. `AccentTintBrush` pasa de estático a derivado en runtime.
- **Tokens:** un solo sistema de estado (`Border.Pill` + `StatusTone`); tres converters de color menos;
  AA parejo (pills, `SuccessBrush` fuera de card, `Button.Danger` dark). Menos superficie para que algo se
  vea "barato".
- **Command palette:** navegación y acciones por teclado (Ctrl+K) — la feature "firma" de categoría, sobre
  el cableado que ya existía.
- **Modales:** un chrome único (`Border.ModalCard`) con Escape/foco/Tab parejos en los cuatro; menos XAML
  duplicado.
- **Dashboard:** el gráfico pasa de adorno a analítica legible (ejes + guías), con el hover que ya tenía.
- **Clases:** agenda en grilla de 7 días (si entra F).
- **Accesibilidad:** AA parejo también fuera de las cards; teclado operable en grillas y modales.
- **Mantenimiento:** menos converters y literales; el design system queda como única fuente de color.

## Riesgos
- **Acento claro vs. AA (pieza A).** Mitigación: paleta de swatches AA-safe (decisión 1) o tono de texto
  derivado. `qa` prueba con dos acentos distintos.
- **Escape global del command palette vs. `IsCancel` de los modales (pieza C).** Mitigación: Close
  idempotente + verificación de `qa` en los tres casos (palette / modal / nada).
- **Retiro de converters que rompa un binding olvidado (pieza B).** Mitigación: `grep` de cada converter
  antes de borrarlo (se verificaron los usos actuales) + `DesktopWiringTests` verde.
- **Etiquetas del eje dentro del `Viewbox` (pieza E).** Se deformarían al estirar. Mitigación: labels en un
  gutter fijo **fuera** del `Viewbox` (ya contemplado en el diseño).
- **Scope creep de micro-interacciones (pieza D) e i18n (pieza G).** Son barridos amplios; mitigación:
  alcance acotado por los tickets listados, transiciones cortas y sin framework de localización.
- **F (calendario) más grande de lo que parece.** Mitigación: MVP = 7 columnas apiladas (sin timeline por
  hora) y prioridad última / fase 2.

## Fuera de alcance (deuda explícita → `docs/TASKS.md`)
- **Framework de i18n / multi-idioma** (`.resx`, cultura seleccionable): no entra (locale único offline).
- **Calendario con timeline por hora** (posición vertical por horario): refinamiento posterior de F.
- **UX11, UX12** (auto-update), **UX13** (editor de cobranzas), **UX14/UX26/UX28** (estados vacío-vs-error
  y doble carga), **UX15a** (ancho de Morosos), **UX19/F3-D2, UX22, UX23, UX24** (F3: lector/kiosk/foto),
  **UX25/UX27/UX29/UX31/UX33** (pulido específico de Reportes): ver su fila en `docs/TASKS.md`.
- **Roles/decisiones de política** (D11 ampliada, O10) y **NEW-8** (`.editorconfig`): decisión del usuario,
  ajenas a esta fase de UI.
- **F4-D1 a F4-D4** (retención/churn, detalle POS, efectividad de cobranza): siguen fuera, de F4.

---

## Referencias
- Shell / command palette: `src/GymForge.Desktop/Views/Shell/MainWindowViewModel.cs` (`NavSection`,
  `IsCommandPaletteOpen`, `SearchQuery`, `OpenCommandPalette`/`CloseCommandPalette`, `NavigateCommand`) ·
  `Views/Shell/MainWindow.axaml` (KeyBindings Ctrl+K/Escape, `Button.SearchButton`)
- Branding: `src/GymForge.Desktop/App.axaml.cs` (`ApplyBrandAccent`) ·
  `src/GymForge.Desktop/Assets/Styles/Colors.axaml` (`AccentTintBrush`) · `Services/SessionContext.cs`
  (`BrandColorHex`, `DefaultBrandColorHex`) · `ViewModels/Settings/SettingsViewModel.cs` (swatches)
- Tokens / converters: `src/GymForge.Desktop/Assets/Styles/AppStyles.axaml` (`Border.Pill.*`, `Button.*`,
  `DataGrid`, `Border.ModalCard` a crear) · `Converters/StatusToneConverter.cs` ·
  `Converters/StatusColorConverter.cs` · `Converters/ActiveConverters.cs` · `App.axaml` (registros)
- Pills a migrar: `Views/Members/MembersListView.axaml:116-137` · `Views/Charges/ChargesView.axaml:92-107`
  + `ViewModels/Charges/ChargesViewModel.cs:155-163` · `Views/Products/ProductsView.axaml:140-144` ·
  `Views/Plans/PlansView.axaml:106-110` · `Views/Dashboard/DashboardView.axaml:131-136,256-257`
- Dashboard: `Views/Dashboard/DashboardView.axaml` (Viewbox/Canvas, `RevenueBars`/`RevenueBarVm`,
  `Revenue30DaysTotal`, `ChartFromLabel`/`ChartToLabel`) · `ViewModels/Dashboard/DashboardViewModel.cs`
- Clases: `Views/Classes/ClassesView.axaml` · `ViewModels/Classes/ClassesViewModel.cs` ·
  `src/GymForge.Application/UseCases/Classes/` (`ScheduleRowDto`, `BookingRowDto`)
- Modales: `Views/Members/FreezeModalView.axaml` (modelo) · `Views/Charges/{ChargesView,PaymentModalView}.axaml`
  · `Views/Cash/{CashView,RegisterSaleView}.axaml` · `Views/Members/MemberDetailView.axaml` ·
  `Views/Collections/CollectionsView.axaml`
- Accesibilidad/strings: `Views/Settings/SettingsView.axaml` (UX9/UX10/UX16) ·
  `Views/Products/ProductsView.axaml` (UX34) · varias vistas (UX8/UX16)
- Roadmap/estado: `docs/PROGRESS.md` (Fase 5) · `docs/TASKS.md` (UX1–UX34, tokens) · `docs/adr/README.md`
