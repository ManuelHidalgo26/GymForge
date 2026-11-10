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
| [ADR-008](ADR-008-editar-socio-rutinas-comisiones.md) | Editar socio desde la ficha, rutinas en la ficha y comisiones de staff (snapshot al vender + reporte, reutiliza el export de ADR-007) | Accepted | S7 — Fase 4 (parte 2) |
| [ADR-009](ADR-009-pulido-us100k.md) | Pulido "US$100k": command palette (Ctrl+K), branding derivado (AccentTintBrush), limpieza de tokens/converters + AA, micro-interacciones y modales, dashboard con ejes/hover, calendario semanal, i18n es-AR | Accepted | S8 — Fase 5 (parte 1) |
| [ADR-010](ADR-010-calendario-semanal-clases.md) | Calendario semanal de Clases: grilla de 7 días (Lun–Dom) con proyección por día, chips clickeables y pill de ocupación; reutiliza navegación y reservas; cero migración (pieza F diferida de ADR-009) | Accepted | S9 — Fase 5 (parte 2) |
| [ADR-011](ADR-011-endurecimiento-final.md) | Endurecimiento final (post-Fase 5): tres tiers priorizados. MUST/SHOULD (NEW-9 Screenshots al sln, NEW-10 pin SSH.NET, D9a captive-dep del Dashboard + ValidateScopes, NEW-12 test headless de ModalHost, D7 DNI único + migración, D3 SiteId + migración/backfill). DEFER con plan: D9b scope-por-operación, D2 auth de API (apagada), UX19 primer carácter del lector | Accepted | Endurecimiento final — post Fase 5 |
| [ADR-002 · Addendum](ADR-002-addendum-estado-socio-gatekeeper.md) | Estado del socio en el Gatekeeper (NEW-25): allowlist fail-closed `{Active, Overdue}` tras resolver el socio; motivos `Suspended` (reusado) y `MemberInactive` (nuevo, ordinal 10) | Accepted | Barrido de deuda pasada 3 (2026-11-10); extiende ADR-002 §3.4 |
| [ADR-002 · Addendum reconciliación](ADR-002-addendum-reconciliacion-estado-socio.md) | Reconciliación de estado de socio en bases legadas (NEW-31 / O-3): en el arranque, socios `Prospect` con membresía `Active` pasan a `Active` (UPDATE directo, idempotente, backup previo gated, sin AuditLog). Estados persistidos como texto | Accepted | Barrido de deuda pasada 3 (2026-11-10), commit `d7852bf`; extiende ADR-002 §3.4 y el addendum de NEW-25 |
