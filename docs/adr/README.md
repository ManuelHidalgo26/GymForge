# ADRs — GymForge

Architecture Decision Records. Uno por decisión relevante; no se borran, cambian de **Status**
(Proposed → Accepted / Superseded). Contexto de cada sesión: `docs/PROGRESS.md`. Backlog: `docs/TASKS.md`.

| ADR | Título | Status | Sesión / Fase |
|---|---|---|---|
| [ADR-001](ADR-001-stack-decision.md) | UI Framework: Avalonia vs Electron vs Tauri | Accepted | pre-roadmap |
| [ADR-002](ADR-002-multi-tenant-y-correctitud-core.md) | Multi-tenant y correctitud del core | Accepted | S1 — Fase 0 (1) |
| [ADR-003](ADR-003-estado-automatico-auditoria-cuenta-corriente.md) | Estado automático, auditoría y cuenta corriente | Accepted | S2 — Fase 0 (2) |
| [ADR-004](ADR-004-robustez-de-produccion.md) | Robustez de producción (backup off-site, auto-update, licencia CUIT+fingerprint, logs, fix sedes) | Accepted | S3 (continuación) — Fase 1 |
| [ADR-005](ADR-005-whatsapp-cobranzas.md) | WhatsApp gratis + cobranzas (morosos, wa.me, DunningConfig editable, NotificationLog) | Accepted | S4 — Fase 2 |
| [ADR-006](ADR-006-escaner-hid-y-checkin-premium.md) | Escáner HID + check-in premium (keyboard-wedge, foto por archivo, teclado numérico, arquitectura de hardware) | Accepted | S5 — Fase 3 |
| [ADR-007](ADR-007-reportes-y-export.md) | Reportes reales + export (morosos, recaudación desagregada, altas y bajas; export PDF/Excel con ClosedXML, diálogo nativo de guardado) | Accepted | S6 — Fase 4 (parte 1) |
| [ADR-008](ADR-008-editar-socio-rutinas-comisiones.md) | Editar socio desde la ficha, rutinas en la ficha y comisiones de staff (snapshot al vender + reporte, reutiliza el export de ADR-007) | Proposed | S7 — Fase 4 (parte 2) |
| [ADR-009](ADR-009-pulido-us100k.md) | Pulido "US$100k": command palette (Ctrl+K), branding derivado (AccentTintBrush), limpieza de tokens/converters + AA, micro-interacciones y modales, dashboard con ejes/hover, calendario semanal, i18n es-AR | Accepted | S8 — Fase 5 (parte 1) |
| [ADR-010](ADR-010-calendario-semanal-clases.md) | Calendario semanal de Clases: grilla de 7 días (Lun–Dom) con proyección por día, chips clickeables y pill de ocupación; reutiliza navegación y reservas; cero migración (pieza F diferida de ADR-009) | Accepted | S9 — Fase 5 (parte 2) |
