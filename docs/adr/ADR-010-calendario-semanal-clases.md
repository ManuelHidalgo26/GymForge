# ADR-010 — Calendario semanal de Clases: grilla de 7 días (Fase 5 parte 2, pieza F)

**Status:** Accepted
**Date:** 2026-11-04
**Deciders:** arqui (planner) · implementa `dev` (proyección por día) · UI `ux` (render de la grilla) · veta `qa`
**Sesión:** Fase 5 "Pulido US$100k" del roadmap, **parte 2** (`docs/PROGRESS.md`). Única pieza pendiente del
pulido: la **pieza F** diferida en ADR-009 (NEW-11 en `docs/TASKS.md`).

---

## Addendum — cierre e implementación (2026-11-04, al pasar a `Accepted`)

- **Implementado tal cual el diseño**, con TDD. Flujo: arqui (este ADR) → dev (VM + grilla + 5 tests) → ux
  (pasada visual/AA) → qa (**APROBADO CON OBSERVACIONES**, sin bloqueantes). Commiteado por el orquestador
  en `feat/sesion-6`.
- **Entregado:** `ScheduleDayVm` (record con `Date`, `DayName` es-AR, `DateLabel`, `IsToday`, `Schedules`,
  `IsEmpty`); `WeekDays` + `BuildWeekDays()` + `SelectScheduleCommand` en `ClassesViewModel`; grilla de 7
  columnas (`ScrollViewer` horizontal + `ItemsControl`/`UniformGrid Rows="1"`, `MinWidth` 120) con chips
  `Button.ScheduleChip` (hora + clase + pill de ocupación `neutral`); resalte del día de hoy (del calendario
  del gimnasio vía `ICompanyCalendar`, no de la PC) y de la selección (borde 2 px, sin cambio de tamaño);
  `AutomationProperties.Name` accesible por chip (MultiBinding+StringFormat); converter `ValuesEqualConverter`.
  Navegación de semana y lógica de reservas reutilizadas sin cambios.
- **Decisiones confirmadas al cerrar:** semana **Lun–Dom** (ya en el código, `MondayOf`); días sin horarios
  se **muestran con "—"** (no se colapsan); semana entera vacía mantiene el **banner `IsEmpty`**; el pill de
  ocupación quedó con tono **`neutral`** (desvío menor del MVP "plano", decidido por `ux`: el tono neutral ya
  es AA y lo hace legible). MVP sin timeline por hora (refinamiento posterior, fuera de alcance).
- **Verificación `qa`:** build Debug + Release 0 errores; `dotnet test` **1461 verdes**, 0 omitidos
  (Domain 107 · Application 590 · Desktop 416 · Integration 348; piso previo 1456); render verificado con
  `tools/GymForge.Screenshots` en light y dark; foco con Tab y anillo visible confirmados; multi-tenant,
  append-only y cero migración verificados.
- **Observaciones no bloqueantes → `docs/TASKS.md` (F5-D1 a F5-D4):** ancho de la columna Dom a ~1180 px
  (scroll horizontal; decidir `MinWidth` vs panel de reservas); `_today` no se refresca a medianoche;
  `PrevWeek`/`NextWeek` fire-and-forget pre-existente; test de `IsToday` con `Task.Yield()` frágil.

## Context

La parte 1 de la Fase 5 (ADR-009, `Accepted`) cerró las piezas A–E y G (branding derivado, limpieza de
tokens, command palette, modales, dashboard con ejes y accesibilidad). La **pieza F — calendario semanal
de Clases** se difirió por ser la más grande y la más **aditiva** (la agenda actual ya funciona). Este ADR
la especifica y la deja lista para que `dev` implemente con TDD.

Hoy la agenda de Clases es un **`ListBox` plano** de los horarios de la semana. Se lee como una lista, no
como una agenda. La pieza F la convierte en una **grilla de 7 días** (Lun–Dom, cada uno con su fecha) con
los horarios ubicados por día. Es **UI pura**: no agrega features de negocio, no escribe datos, no toca el
esquema. Sube la percepción de calidad de una pantalla que ya opera.

Lo único nuevo es:
1. una **proyección por día** de los `Schedules` ya cargados (lógica de VM → `dev`), y
2. su **render en grilla** de 7 columnas (XAML → `ux`).

**Se reutiliza tal cual**, sin tocarse: la navegación de semana (`PrevWeek`/`NextWeek`/`ThisWeek`/`WeekLabel`),
la carga de `Schedules` (`LoadSchedulesAsync` + `GetWeekSchedulesQuery`) y **toda** la lógica de reservas
(`SelectedSchedule`/`HasSelection`, panel derecho con `Bookings`, `BookMemberCommand`, `CancelBookingCommand`,
`Members`/`SelectedMember`). La selección de un horario de la grilla alimenta el mismo panel de reservas que
hoy alimenta el `ListBox`.

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first / multi-tenant / append-only:** la pieza es **UI pura**. No hay I/O de red, no escribe datos
  nuevos y no cruza tenants. La proyección opera **en memoria** sobre `Schedules`, que ya viene filtrado por
  `CompanyId` + `SiteId` desde `GetWeekSchedulesQuery`. No se tocan `AccessLog`/`AuditLog` ni los global query
  filters. La selección solo cambia `SelectedSchedule` (estado de UI); reservar/cancelar sigue pasando por los
  commands existentes, con su guard de `CompanyId`.
- **Cero migración EF.** Ninguna propuesta toca el esquema. Si en la implementación apareciera una necesidad
  de schema (no se prevé), se frena y se decide; no se da por hecho.
- **Tokens, no literales (design system):** todo color sale de `Colors.axaml`/`AppStyles.axaml`. Se reutilizan
  `Border.Pill` (ocupación), `Border.Card`, `Border.IconChip`, `StackPanel.EmptyState`, `Border.CountBadge`,
  `Border.Inset`, `TextBlock.FormLabel` y los tokens de acento de la pieza A (`AccentTintBrush`/`AccentTextBrush`).
  Prohibido sumar colores literales nuevos.
- **Contraste AA (criterio del proyecto):** WCAG **AA** — ratio **≥ 4,5:1** para texto normal y **≥ 3:1** para
  texto grande/negrita. Se **calcula sobre los valores hex** de `Colors.axaml`/tonos Fluent (luminancia
  relativa), **no se mide sobre captura** (mismo criterio que ADR-009). `qa` verifica el render real con
  `tools/GymForge.Screenshots` en light y dark.
- **MVVM Toolkit:** `[ObservableProperty]`/`[RelayCommand]`; nada de code-behind salvo lo estrictamente de
  presentación (p. ej. foco), como ya hace el resto de la app.
- `dotnet test` verde antes de cada commit (regla 6). Piso declarado al cierre de la Fase 5 parte 1:
  **1456 tests verdes** (no bajar). **Un commit por feature** (regla 7).
- **`.editorconfig` (NEW-8):** decisión del usuario, no se resuelve acá. Esta pieza no hace barrido de formato
  ni cambia severidad de estilo; toca solo los archivos de Clases y su test.

### Hechos del código verificados al 2026-11-04 (no asumir: ya chequeado)

**Vista — `src/GymForge.Desktop/Views/Classes/ClassesView.axaml`:**
- `Grid RowDefinitions="Auto,Auto,Auto,*"`: fila 0 = toolbar (navegación + alta de clase/horario), fila 1 =
  form "nueva clase", fila 2 = form "nuevo horario", fila 3 = cuerpo.
- **Toolbar (líneas 20–42):** `PrevWeekCommand`, `WeekLabel` (`Width="120"`, centrado), `NextWeekCommand`,
  `ThisWeekCommand` ("Esta semana"), `OpenFormCommand` ("Nueva clase"), `OpenScheduleFormCommand`
  ("Nuevo horario", `Classes="Primary"`). `Border.CountBadge` con `Schedules.Count` (línea 18). **Todo esto se
  reutiliza sin cambios.**
- **Cuerpo (línea 105):** `Grid ColumnDefinitions="*,360"` dentro de `Margin="16,12"`. Columna 0 = agenda;
  columna 1 = panel de reservas (ancho fijo 360).
- **Agenda HOY (líneas 113–134):** `ListBox` plano, `ItemsSource="{Binding Schedules}"`,
  `SelectedItem="{Binding SelectedSchedule}"`, `Background="Transparent"`. `ItemTemplate` con
  `x:DataType="cls:ScheduleRowDto"`: `Border.IconChip` + `ClassName` (SemiBold) + `Start` con
  `StringFormat='ddd dd/MM · HH:mm'` (`FormLabel`) + `Border.Pill` con `Occupancy`. **Este bloque es lo único
  que se reemplaza por la grilla.**
- **Estado vacío de la agenda (líneas 137–141):** `StackPanel.EmptyState IsVisible="{Binding IsEmpty}"`:
  "Sin horarios esta semana" / "Creá uno con «Nuevo horario» o cambiá de semana."
- **Panel de reservas (líneas 144–187):** `Border.Card IsVisible="{Binding HasSelection}"`,
  `DataContext="{Binding SelectedSchedule}"` en el encabezado; usa `BookMemberCommand`, `Bookings`,
  `CancelBookingCommand`, `Members`, `SelectedMember` navegando al VM raíz con
  `$parent[UserControl].((vm:ClassesViewModel)DataContext)`. **Se conserva igual.**
- **Sin selección (líneas 190–195):** `StackPanel.EmptyState IsVisible="{Binding !HasSelection}"`: "Elegí un
  horario".

**ViewModel — `src/GymForge.Desktop/ViewModels/Classes/ClassesViewModel.cs`:**
- Colecciones: `Schedules` (`ObservableCollection<ScheduleRowDto>`, línea 26), `Bookings` (27),
  `Members` (28). Estado: `SelectedSchedule` (30), `SelectedMember` (31), `WeekStart` (`DateOnly`, 32).
- `IsEmpty => !IsLoading && Schedules.Count == 0` (50). `HasSelection => SelectedSchedule is not null` (51).
  `WeekLabel => $"{WeekStart:dd/MM} – {WeekStart.AddDays(6):dd/MM}"` (52) → **span de 7 días**.
- `OnSelectedScheduleChanged` (67–71): notifica `HasSelection` y dispara `LoadBookingsAsync()`. **Este es el
  pipeline de reservas; no se toca.**
- `LoadSchedulesAsync` (105–115): envía `GetWeekSchedulesQuery(CompanyId, SiteId, WeekStart)`, reemplaza
  `Schedules` y **re-vincula** la selección por Id si el horario sigue en la semana visible (112–114).
- `PrevWeek`/`NextWeek` (125–126) y `ThisWeekAsync` (128–133): recargan `Schedules`. `EnsureCurrentWeekAsync`
  (93–100) y `CurrentWeekStartAsync` (102–103) usan `_calendar.TodayAsync(CompanyId)` → el "hoy" es el **día
  del gimnasio**, no el de la zona de la PC.
- **`MondayOf` (232):** "la semana arranca el lunes". Es decir, **la agenda ya es Lun–Dom** (el default
  rioplatense ya está en el código; no hay que cambiarlo).

**DTOs y queries — `src/GymForge.Application/UseCases/Classes/ClassScheduleCommands.cs`:**
- `ScheduleRowDto` (12–24): `Id, ClassDescriptionId, ClassName, Start (DateTime), End (DateTime), Capacity,
  BookedCount`; computados `Available => Capacity - BookedCount`, `IsFull => BookedCount >= Capacity`,
  `Occupancy => "{BookedCount}/{Capacity}"`. **`Start`/`End` están en hora local del gimnasio** (comentario
  líneas 9–10: "para una agenda semanal es lo intuitivo y evita conversiones en la UI"). Es un `record`
  (igualdad por valor; el `Id` es único, así que comparar DTOs identifica al horario).
- `GetWeekSchedulesQuery` (31–46): trae los horarios de la sede en la ventana **`[WeekStart, WeekStart+7)`**
  (`from = WeekStart`, `to = WeekStart.AddDays(7)`), filtrados por `CompanyId` + `SiteId` en el repositorio.
  **Garantiza que todos los `Start` caen dentro de los 7 días de la semana** (el borde superior es exclusivo).
- `BookingRowDto` (26): `Id, MemberName, BookingStatus Status, DateTime BookedAt`.
- `GetScheduleBookingsQuery` (83–104): guard explícito `schedule.CompanyId == q.CompanyId`. `BookMemberCommand`
  (106+) y `CancelBookingCommand` (151+) también guardan `CompanyId`. **Multi-tenant ya cubierto.**
- `ClassDto` en `ClassCommands.cs:8`.

**Estilos — `src/GymForge.Desktop/Assets/Styles/AppStyles.axaml`:** `Border.Card` (16), `Border.Inset` (62),
`Border.IconChip` (77), `Border.Pill` (175, base sin tono: solo radio/padding) y tonos `.success/.warning/
.danger/.info/.neutral/.accent` (185–222, todos con tinte tokenizado AA; el `.danger` usa `DangerTextBrush`
por AA), `Border.CountBadge` (225), `StackPanel.EmptyState` (294). El `Border.Pill.accent` ya consume
`AccentTintBrush`/`AccentTextBrush` derivados del color del gimnasio (pieza A de ADR-009).

**Tests — `tests/GymForge.Desktop.Tests/ZonaHorariaViewModelsTests.cs`:** sección "Clases" (líneas 60–97).
`ClassesSut()` (62–70) arma el VM con mocks de `IMemberRepository`, `IMediator` (devuelve listas vacías para
`GetClassesQuery` y `GetWeekSchedulesQuery`), `SessionContext` y `ICompanyCalendar`. `ArrangeAsync` (32–37)
fija el "hoy" del gimnasio en `2026-07-22` (miércoles; lunes = `2026-07-20`). **No hay un archivo de test
dedicado a la agenda de `ClassesViewModel`**; se agrega uno para la proyección (ver "Tests").

---

## Decisión — resumen

Reemplazar el `ListBox` plano de la agenda (ClassesView.axaml:113–134) por una **grilla de 7 columnas**
(Lun–Dom con fecha), alimentada por una **proyección por día** nueva en el VM. Todo lo demás de la pantalla
(toolbar, navegación, carga, reservas, estados vacíos) se conserva.

| Aspecto | Decisión |
|---|---|
| Alcance | Solo el reflow de la agenda a grilla de 7 días + su proyección. UI pura. |
| Semana | **Lun–Dom** (ya es así por `MondayOf`; se confirma como default rioplatense). |
| MVP | **7 columnas con horarios apilados cronológicamente** por día. **NO** timeline con posición vertical por hora (queda como refinamiento posterior). |
| Proyección | `dev`: colección de 7 `ScheduleDayVm` (uno por día), cada uno con sus `ScheduleRowDto` ordenados por hora. |
| Render | `ux`: grilla de 7 columnas iguales con encabezado día+fecha, chips/cards clickeables por horario, panel de reservas conservado. |
| Selección | `SelectScheduleCommand(ScheduleRowDto)` → setea `SelectedSchedule` (dispara el pipeline de reservas existente). |
| Día de hoy | Encabezado del día == hoy del gimnasio resaltado con tokens de acento (AA). |
| Semana vacía | Se mantiene el banner `IsEmpty` actual (propuesta; ver "Decisiones a confirmar"). |
| Migración | **Ninguna.** Multi-tenant y append-only intactos. |
| Tamaño | **L** (la pieza más grande de la Fase 5, pero acotada). |

### Decisiones transversales (leer antes del detalle)
- **No se cambia la carga ni la navegación.** `LoadSchedulesAsync`, `GetWeekSchedulesQuery`, `WeekStart`,
  `WeekLabel`, `PrevWeek`/`NextWeek`/`ThisWeek` quedan **idénticos**. La proyección es un derivado en memoria de
  `Schedules`.
- **No se cambia la lógica de reservas.** `SelectedSchedule` sigue siendo la fuente de verdad de la selección;
  `OnSelectedScheduleChanged` sigue disparando `LoadBookingsAsync`; el panel derecho (`Border.Card` 144–187) no
  se toca. La grilla solo ofrece una forma nueva de **setear** `SelectedSchedule`.
- **La proyección confía en la ventana de la query.** Como `GetWeekSchedulesQuery` acota a
  `[WeekStart, WeekStart+7)`, los 7 buckets (WeekStart..WeekStart+6) cubren todos los horarios; no hace falta
  un bucket "fuera de rango".

---

## Diseño de la proyección por día (`dev`)

### Tipo nuevo `ScheduleDayVm` (presentación, en el proyecto Desktop)
Un tipo chico, inmutable, que representa **una columna/día** de la grilla. No es una entidad ni un DTO de
Application; vive junto al VM (p. ej. `ViewModels/Classes/ScheduleDayVm.cs`).

Campos:
- `Date` (`DateOnly`) — el día (WeekStart + i).
- `DayName` (`string`) — abreviatura rioplatense: **`Lun Mar Mié Jue Vie Sáb Dom`** (arreglo fijo por índice
  0..6; determinista y testeable, evita depender de `CultureInfo` de la PC).
- `DateLabel` (`string`) — `Date` con `"dd/MM"`.
- `IsToday` (`bool`) — `Date == <hoy del gimnasio>`.
- `Schedules` (`IReadOnlyList<ScheduleRowDto>`) — los horarios de ese día, **ordenados por `Start`**.
- `IsEmpty` (`bool`) — `Schedules.Count == 0` (para el "—" por columna).

### Nuevas propiedades/estado del VM
- `[ObservableProperty] private ObservableCollection<ScheduleDayVm> _weekDays = [];` — las 7 columnas que la
  grilla bindea.
- `private DateOnly _today;` — el día del gimnasio, para `IsToday`. Se setea donde ya se obtiene el hoy
  (`EnsureCurrentWeekAsync`/`ThisWeekAsync` llaman `_calendar.TodayAsync`); guardarlo en vez de descartarlo.
  (Hoy `CurrentWeekStartAsync` calcula `MondayOf(await _calendar.TodayAsync(...))` y tira el `today`; conviene
  exponer un paso que devuelva el `today` y derivar `WeekStart = MondayOf(today)` + `_today = today`.)

### Construcción (`BuildWeekDays`)
Se llama **cada vez que cambia `Schedules`** (reemplaza el patrón actual `OnSchedulesChanged → IsEmpty`,
sumándole la reconstrucción) y después de fijar `_today`:

```csharp
private static readonly string[] DayNames = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];

private void BuildWeekDays()
{
    var byDay = Schedules
        .GroupBy(s => DateOnly.FromDateTime(s.Start))                 // Start es hora local del gimnasio
        .ToDictionary(g => g.Key, g => (IReadOnlyList<ScheduleRowDto>)g.OrderBy(s => s.Start).ToList());

    var days = new List<ScheduleDayVm>(7);
    for (var i = 0; i < 7; i++)
    {
        var date = WeekStart.AddDays(i);
        var rows = byDay.TryGetValue(date, out var r) ? r : [];
        days.Add(new ScheduleDayVm(date, DayNames[i], date == _today, rows));
    }
    WeekDays = new ObservableCollection<ScheduleDayVm>(days);
}

partial void OnSchedulesChanged(ObservableCollection<ScheduleRowDto> value)
{
    OnPropertyChanged(nameof(IsEmpty));
    BuildWeekDays();
}
```

Notas:
- **Agrupar por `DateOnly.FromDateTime(s.Start)`** es correcto porque `Start` está en hora local del gimnasio
  (verificado en el DTO). No hay conversión de zona.
- **Siempre 7 columnas**, en orden Lun→Dom, incluidos los días sin horarios (su `Schedules` queda vacío → la
  columna muestra "—").
- **Días vacíos** no requieren datos: la columna existe igual con `IsEmpty = true`.
- **Semana entera vacía:** `Schedules.Count == 0` → `IsEmpty` del VM es `true` y todas las columnas quedan
  vacías. Propuesta (ver "Decisiones a confirmar"): en ese caso mostrar el **banner `IsEmpty` actual** y no la
  grilla (comportamiento de hoy), para no mostrar 7 columnas de "—".

### Selección desde la grilla
- Nuevo comando en el VM raíz:
  ```csharp
  [RelayCommand] private void SelectSchedule(ScheduleRowDto row) => SelectedSchedule = row;
  ```
- Setear `SelectedSchedule` **dispara `OnSelectedScheduleChanged`** (sin cambios) → `HasSelection` + recarga de
  `Bookings`. Es exactamente lo que hoy hace el `SelectedItem` del `ListBox`, así que **el pipeline de reservas
  no se rompe**.
- La re-vinculación de `LoadSchedulesAsync` (112–114, que re-asigna `SelectedSchedule` por Id tras recargar)
  sigue funcionando: al reasignarse, `BuildWeekDays` ya corrió con los `Schedules` nuevos y la grilla refleja
  la selección por igualdad de valor del `record`.

Lo que **no** cambia en el VM: `LoadAsync`, `LoadSchedulesAsync`, `LoadBookingsAsync`, `BookMemberAsync`,
`CancelBookingAsync`, `PrevWeek`/`NextWeek`/`ThisWeekAsync`, formularios de clase/horario, `WeekLabel`.

---

## Diseño de la grilla (`ux`)

Reemplaza únicamente el `ListBox` de las líneas 113–134; el resto de la columna 0 (estado vacío) y la columna
1 (panel de reservas) quedan igual.

### Estructura
- Dentro de la columna 0 del cuerpo (`Grid ColumnDefinitions="*,360"`), una **grilla de 7 columnas iguales**
  (p. ej. `UniformGrid Columns="7"`) **envuelta en un `ScrollViewer` horizontal** (ver "Responsive").
- **Cada columna** = un `ScheduleDayVm`:
  - **Encabezado** arriba: `DayName` (SemiBold) + `DateLabel` (`FormLabel`), p. ej. "Lun" / "20/07". Si
    `IsToday`, el encabezado se resalta (ver "Día de hoy").
  - **Cuerpo** scrolleable verticalmente: un `ItemsControl` con `ItemsSource="{Binding Schedules}"` que apila
    los horarios **cronológicamente** (ya vienen ordenados por `Start`). Cada ítem = una **card/chip**
    clickeable.
  - Si `IsEmpty`, en vez de la lista, un placeholder "—" discreto (`FormLabel`/terciario, centrado).

### Chip de horario (card chica, clickeable)
- Contenido: **hora** (`Start` con `StringFormat='HH:mm'`, Mono/SemiBold), **nombre de la clase**
  (`ClassName`), y la **`Border.Pill` de ocupación** (`Occupancy`), reutilizada tal cual.
- Click → `SelectScheduleCommand` del VM raíz, con `CommandParameter="{Binding}"` (el `ScheduleRowDto`). En
  XAML: `Command="{Binding $parent[UserControl].((vm:ClassesViewModel)DataContext).SelectScheduleCommand}"`
  (mismo patrón de navegación al VM raíz que ya usa el panel de reservas, líneas 154/169/175).
- **Estado seleccionado:** la card del horario == `SelectedSchedule` lleva un realce de acento (borde/anillo
  `AccentTextBrush` o fondo `AccentTintBrush`, ambos AA tras la pieza A). Mecanismo recomendado (decisión chica
  de `dev`/`ux`, no se re-litiga): un `MultiBinding` del `Classes.selected` que compara el `ScheduleRowDto` del
  chip con `SelectedSchedule` por igualdad de `record` (o un converter de igualdad de logica, del estilo del
  `StringEqualsConverter` que el proyecto ya conserva). Se actualiza reactivo al cambiar `SelectedSchedule`.
- La card es un control accionable (Button o Border con `PointerPressed` + rol accesible); debe tener **foco
  visible** y activarse con Enter/Espacio (consistente con la pieza D de ADR-009).

### Día de hoy (resaltado, AA)
- El encabezado del día con `IsToday` usa **tokens de acento**: fondo `AccentTintBrush` + texto `AccentTextBrush`
  (ambos AA en light y dark tras la pieza A), **o** una barra/underline de 2px en acento con el texto en
  secundario. Nada de color literal.
- Si hoy cae **fuera** de la semana visible (navegando con Prev/Next), ninguna columna se resalta (`IsToday`
  false en las 7). Correcto.

### Responsive (mínimo)
- La grilla de 7 columnas vive dentro de un **`ScrollViewer HorizontalScrollBarVisibility="Auto"`** y cada
  columna tiene un **`MinWidth` legible** (≈ 110–120 px). Con la ventana angosta (p. ej. 1024 con la sidebar
  expandida y el panel de reservas de 360 fijo), en vez de espachurrar las 7 columnas a un ancho ilegible, la
  grilla **scrollea horizontalmente** conservando columnas legibles. Con la ventana ancha, `UniformGrid`
  reparte el ancho disponible en 7 columnas iguales.
- El cuerpo de cada día scrollea **vertical** (días con muchos horarios) sin empujar el alto de la pantalla.
- El panel de reservas de ancho fijo (360) **no se toca** en esta pieza (su reflow a ancho variable o a un
  overlay en ventanas angostas queda fuera de alcance; ver "Fuera de alcance").

---

## Accesibilidad (criterio AA)
- Encabezados día+fecha: `DayName` en texto primario; `DateLabel` en `FormLabel` (secundario tokenizado, ya a
  AA). Resalte de hoy con `AccentTintBrush`/`AccentTextBrush` (AA por pieza A, light y dark) o underline + texto
  secundario.
- Chips: hora + clase en texto primario; `Border.Pill` de ocupación con su tono tokenizado AA. El realce de
  selección usa acento AA, no un relleno que baje el contraste del texto.
- **Teclado:** las cards de horario son accionables por Tab + Enter/Espacio, con foco visible (coherente con
  la pieza D). El `ScrollViewer` horizontal no debe atrapar el foco.
- `AutomationProperties.Name` útil en cada card (p. ej. "Funcional, lunes 20/07 18:00, 5 de 20") para lectores
  de pantalla, ya que el layout en columnas no transmite el día por sí solo.
- Verificación por **cálculo sobre los hex** + captura de `qa` en light y dark.

---

## Tests / verificación

`dev` escribe los tests **primero** (TDD). Reutilizar el harness de `ZonaHorariaViewModelsTests` (mocks de
`IMediator`/`IMemberRepository`/`SessionContext`/`ICompanyCalendar`; `_mediator.Send(Arg.Any<GetWeekSchedulesQuery>…)`
devolviendo una lista de `ScheduleRowDto`). Sugerencia: archivo nuevo
`tests/GymForge.Desktop.Tests/ClassesCalendarViewModelTests.cs` (o ampliar la sección "Clases" existente).

Casos a exigir (los tres del encargo + bordes):
1. **Agrupa por día y ordena por hora.** Dada una semana con horarios en varios días y desordenados, `WeekDays`
   tiene **7 elementos** en orden Lun→Dom; cada `ScheduleDayVm.Schedules` contiene solo los de su fecha y
   **ordenados por `Start`** ascendente. Verificar que un horario del martes no aparece en la columna del lunes.
2. **Semana sin horarios → grilla vacía.** Con `GetWeekSchedulesQuery` devolviendo `[]`: `WeekDays` tiene 7
   columnas, **todas con `IsEmpty = true`**, y `IsEmpty` del VM es `true` (banner).
3. **Seleccionar un horario de la grilla alimenta el panel de reservas.** `SelectScheduleCommand.Execute(row)`
   deja `SelectedSchedule == row`, `HasSelection == true` y **envía `GetScheduleBookingsQuery`** por ese
   `ScheduleId` (no se rompe el pipeline de `SelectedSchedule` → `Bookings`).
4. **`IsToday` marca el día correcto.** Con el hoy del gimnasio dentro de la semana (p. ej. miércoles
   `2026-07-22`), exactamente una columna tiene `IsToday == true` y es la del miércoles; navegando a otra
   semana, ninguna.
5. **(Borde) Día con varios horarios y día vacío conviven.** Un día con 2+ horarios y otro sin ninguno:
   el primero ordena por hora, el segundo queda `IsEmpty`.

**Piso: 1456 tests verdes; no bajar.** `dotnet test GymForge.sln` exit 0, 0 omitidos, Debug y Release sin
errores, **sin migración**. Un commit por feature (regla 7):
`feat: [clases] agenda semanal en grilla de 7 dias`.

**`qa`:** runtime con `tools/GymForge.Screenshots` en **light y dark** — semana con horarios (grilla de 7
columnas, hoy resaltado), semana sin horarios (banner), ventana angosta (scroll horizontal, columnas
legibles), reservar/cancelar desde un horario elegido en la grilla (el panel derecho responde igual que hoy).
`tools/GymForge.Screenshots` está **fuera del `.sln`** (NEW-4/F3-D1): recompilar el tool tras cambiar la firma
del VM para que no quede roto, como pasó en la limpieza del 2026-11-04.

---

## Consecuencias
- **Esquema:** ninguna migración. Multi-tenant y append-only intactos (la pieza no escribe datos nuevos; la
  selección es estado de UI).
- **Clases:** la agenda pasa de lista plana a **grilla de 7 días** que lee como un calendario profesional, con
  el día de hoy resaltado y los horarios apilados por día. Es la última pieza del "look US$100k" de la Fase 5.
- **VM:** suma una **proyección derivada** (`WeekDays` + `ScheduleDayVm` + `SelectScheduleCommand`) sobre
  `Schedules`; no cambia carga, navegación ni reservas. Superficie de cambio chica y aislada a Clases.
- **Reutilización:** `SelectedSchedule` sigue siendo la fuente de verdad; el panel de reservas y toda su lógica
  quedan intactos (menos riesgo de regresión).
- **Mantenimiento:** un tipo de presentación nuevo (`ScheduleDayVm`) y una grilla en XAML; sin dependencias
  nuevas, sin colores literales, sin converters de color nuevos.

## Riesgos
- **Sincronización de la selección entre columnas.** Con varias "listas" de chips (una por día) y una sola
  `SelectedSchedule`, el realce debe derivar de comparar cada chip con `SelectedSchedule` (no de un
  `ListBox.SelectedItem` por columna, que no sincroniza entre listas). Mitigación: `SelectScheduleCommand` +
  realce por igualdad de valor del `record`; cubierto por el test #3 y por `qa` en runtime.
- **Igualdad de `record` tras recargar.** Al recargar la semana se crean `ScheduleRowDto` nuevos; la
  re-vinculación por `Id` (líneas 112–114) ya asigna la instancia nueva a `SelectedSchedule`, así que el realce
  sigue al horario correcto. Verificar que el realce no "se pierde" tras Prev/Next/ThisWeek si el horario sigue
  visible.
- **Responsive en ventana angosta.** 7 columnas + panel fijo de 360 aprietan el ancho. Mitigación: `MinWidth`
  por columna + scroll horizontal (nunca columnas ilegibles). `qa` valida a 1024 de ancho.
- **Grilla más grande de lo que parece.** Mitigación: MVP = columnas con horarios **apilados** (sin timeline por
  hora), scroll vertical por día; el timeline queda fuera de alcance.
- **Tool de capturas fuera del `.sln`.** Cambiar la firma/propiedades del VM puede romper
  `tools/GymForge.Screenshots` sin que `dotnet test` lo note (ya pasó, NEW-4/F3-D1). Recompilarlo a mano.

## Decisiones a confirmar con el usuario
1. **Semana entera sin horarios: ¿banner o grilla de "—"?** Propuesta: **mantener el banner `IsEmpty` actual**
   ("Sin horarios esta semana") y no mostrar la grilla de 7 columnas vacías, que se ve pobre. Alternativa:
   mostrar siempre las 7 columnas (cada una con "—") para que la estructura del calendario esté siempre
   presente. Default propuesto: **banner** (mínimo cambio, consistente con hoy).
2. **Días sin horarios dentro de una semana con actividad.** Propuesta: **mostrarlos igual** (columna con "—"),
   porque es lo que hace que se lea como calendario. (No se colapsan.) ¿De acuerdo?
3. **Inicio de semana.** El código ya arranca el **lunes** (`MondayOf`), o sea **Lun–Dom**, el default
   rioplatense. Propuesta: **mantenerlo**. (No hay trabajo acá; solo confirmación de que no se quiere Dom–Sáb.)
4. **(Menor) Tono del pill de ocupación.** Hoy la ocupación usa `Border.Pill` plano (sin tono). Propuesta MVP:
   **mantenerlo plano**. Opcional (fuera del MVP): teñir el pill cuando `IsFull` (p. ej. `warning`), ya que el
   DTO expone `IsFull`/`Available`. ¿Dejamos plano o querés el tono por ocupación?

## Fuera de alcance (deuda explícita → `docs/TASKS.md`)
- **Timeline por hora** (posición vertical de cada horario según la hora, con filas horarias): refinamiento
  posterior de F; el MVP apila cronológicamente.
- **Reflow del panel de reservas** (ancho variable o overlay en ventanas angostas): la columna de 360 queda
  fija; su responsive es otra pieza.
- **Cambios en la carga/navegación/reservas**, en `GetWeekSchedulesQuery` o en el esquema: no se tocan.
- **`SiteId` en `Booking`/`ClassSchedule`** (D3) y demás deuda de Clases: ajena a esta pieza (hoy el
  aislamiento por sede ya lo da `GetWeekSchedulesQuery` filtrando `CompanyId` + `SiteId`).
- **i18n como framework** (`.resx`): fuera (locale único offline, decisión de ADR-009). Los nombres de día son
  un arreglo fijo es-AR.

---

## Referencias
- Vista: `src/GymForge.Desktop/Views/Classes/ClassesView.axaml` (agenda = `ListBox` 113–134; cuerpo
  `ColumnDefinitions="*,360"` 105; estado vacío 137–141; panel de reservas 144–195)
- ViewModel: `src/GymForge.Desktop/ViewModels/Classes/ClassesViewModel.cs` (`Schedules` 26, `SelectedSchedule`
  30, `WeekStart` 32, `IsEmpty` 50, `HasSelection` 51, `WeekLabel` 52, `OnSelectedScheduleChanged` 67–71,
  `LoadSchedulesAsync` 105–115, `MondayOf` 232)
- DTOs/queries: `src/GymForge.Application/UseCases/Classes/ClassScheduleCommands.cs` (`ScheduleRowDto` 12–24,
  `GetWeekSchedulesQuery` 31–46, `BookingRowDto` 26, `GetScheduleBookingsQuery` 83–104) ·
  `ClassCommands.cs` (`ClassDto` 8)
- Estilos: `src/GymForge.Desktop/Assets/Styles/AppStyles.axaml` (`Border.Pill` 175 + tonos 185–222,
  `Border.Card` 16, `Border.IconChip` 77, `StackPanel.EmptyState` 294, `Border.CountBadge` 225, `Border.Inset`
  62) · tokens de acento `AccentTintBrush`/`AccentTextBrush` (pieza A, ADR-009)
- Tests: `tests/GymForge.Desktop.Tests/ZonaHorariaViewModelsTests.cs` (harness + sección "Clases" 60–97)
- Contexto: ADR-009 (`docs/adr/ADR-009-pulido-us100k.md`, pieza F ~435–460 y "Hechos" de Clases ~159–164) ·
  `docs/PROGRESS.md` (Fase 5 parte 2) · `docs/TASKS.md` (NEW-11)
