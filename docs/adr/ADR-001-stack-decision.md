# ADR-001 — UI Framework: Avalonia UI vs Electron vs Tauri

**Status:** Accepted  
**Date:** 2026-04-20  
**Deciders:** GymForge Team

---

## Context

GymForge es un sistema de gestión de gimnasios desktop-first para Windows, con los siguientes
requisitos no funcionales que condicionan la elección de framework UI:

- **Offline-first absoluto**: toda la lógica corre localmente, sin depender de red.
- **Acceso a hardware nativo**: impresoras fiscales via COM/OCX, lectores biométricos ZKTeco,
  torniquetes TCP, webcam, lector HID de códigos de barras.
- **Performance**: tablas de 10 000+ filas sin lag, búsqueda FTS5 < 200ms, check-in < 100ms.
- **Distribución**: MSI + OTA updates via Velopack.
- **Mercado LATAM**: equipos con Windows 10/11, hardware económico (i3, 8 GB RAM).
- **Stack del equipo**: experiencia en .NET / C#.

---

## Opciones evaluadas

### 1. Avalonia UI 11 + C# .NET 9

**Ventajas:**
- Renderiza con Skia (GPU-accelerated) sin depender del runtime del OS.
- Eco-sistema .NET completo: EF Core, MediatR, etc., sin puente JS↔C#.
- Acceso directo a `DllImport` / COM Interop para hardware legacy.
- Memory footprint ~80 MB para app vacía.
- Fluent theming nativo, MVVM Toolkit, data-binding robusto.
- Licencia MIT. Hot reload en desarrollo.

**Desventajas:**
- Comunidad más chica que WPF o WinForms.
- Controles de terceros menos abundantes que WPF.
- Designer vs Code: el designer de Avalonia no está tan maduro como el de WPF.

---

### 2. Electron (JS/TS + React / Svelte)

**Ventajas:**
- Ecosistema npm enorme.
- Mejor tooling de UI moderno (Tailwind, shadcn/ui).
- Developers web pueden contribuir.

**Desventajas:**
- Chromium + Node embebido: ~250–400 MB de RAM base + 150 MB binario.
- Puente IPC para cada llamada a hardware (impresora fiscal, biometría) — latencia extra.
- Para hardware COM/OCX x86 se necesita un servidor de proceso separado de todas formas.
- Node.js limitado para lógica de negocio compleja (dominio DDD).
- SQLite via better-sqlite3 pero sin EF Core migrations ni Linq expresivo.

**Descartado** por overhead de RAM en hardware económico y necesidad de puente IPC para hardware.

---

### 3. Tauri 2 (Rust + WebView2)

**Ventajas:**
- Binario pequeño (~10 MB), RAM base ~30 MB.
- WebView2 del OS (no Chromium empaquetado).
- Rust para la capa de sistema.

**Desventajas:**
- Frontend en JS/HTML igual que Electron.
- Integración con SDK .NET/C# (ZKTeco, AFIP SOAP, EF Core) requiere sidecar en C# de todas formas.
- Rust tiene curva de aprendizaje alta para el equipo actual.
- WebView2 requiere Windows 10 1803+ (sin soporte offline instaladores en algunos casos).

**Descartado** porque el equipo no tiene experiencia en Rust y igual necesitaríamos sidecars .NET.

---

### 4. WPF + .NET 9

**Ventajas:**
- Maduro, ecosistema enorme, DocumentBased printers, estilos XAML.
- Team familiar con el stack.

**Desventajas:**
- Windows-only (lock-in total).
- Renderizado GDI/DirectX menos consistente que Skia de Avalonia.
- WPF no recibe features nuevas activamente (modo mantenimiento).
- Virtualización en DataGrid requiere librerías de terceros para producción.

**Descartado** en favor de Avalonia que ofrece mejor trayectoria a futuro.

---

## Decisión

**Avalonia UI 11 + C# .NET 9** con MVVM Toolkit y FluentAvalonia.

La elección permite mantener todo en C# sin puentes IPC adicionales, acceder a hardware nativo
directamente, y usa el mismo lenguaje/runtime que el dominio DDD. Los sidecars para hardware
legacy (FiscalBroker, BioBroker, AccessBroker) son procesos x86 separados que exponen HTTP/WS
locales — esta separación es necesaria independientemente del framework UI elegido, ya que los
OCX fiscales son x86 y la app principal es x64/AnyCPU.

---

## Consecuencias

- Se requiere aprender Avalonia XAML (similar a WPF, diferencias en bindings avanzados).
- El diseñador de UI se hace Code-first + Preview; sin diseñador drag-drop completo.
- Si en el futuro se necesita app web, los ViewModels pueden reutilizarse con Blazor Hybrid.
- FluentAvalonia provee controles de nivel producción (NavigationView, InfoBar, NumberBox, etc.)
  que no vienen en Avalonia core.

---

## Referencias

- https://docs.avaloniaui.net/
- https://github.com/amwx/FluentAvalonia
- https://learn.microsoft.com/en-us/dotnet/desktop/wpf/
- https://tauri.app/
- https://www.electronjs.org/
