# Auditoría E2E — GymForge

Fecha: 2026-06-16 · Revisión integral de todas las vistas (capturas reales headless),
suite de tests y flujos. Estado general: **app funcional de punta a punta**. Lo de abajo
son pulidos/UX, no bloqueantes.

## ✅ Funciona bien
- **Shell**: sidebar + topbar, selector de sede, buscador (Ctrl+K), cambio de tema.
- **Dashboard**: KPIs (socios activos, check-ins, mora, recaudación), gráfico de 30 días,
  "Vencen esta semana", "Actividad de hoy" con ingresos/rechazos. Coherente con los datos.
- **Socios**: lista con búsqueda, filtro por estado, paginación, badges Activo/Prospecto.
- **Alta de socio**: formulario completo + sección "Membresía inicial" (cobro de la 1ª cuota).
- **Caja**: login por PIN, apertura, movimientos (ingreso/egreso), arqueo/cierre.
- **Cobro/venta**: modal membresía/producto + venta a consumidor final (sin socio).
- **Productos**: catálogo + stock por sede + aviso "Reponer".
- **Planes**: CRUD con modalidad/duración/precio/estado.
- **Clases v2**: agenda semanal navegable + reservas con cupo (X/Y) + cancelación.
- **Rutinas v2**: armador (socio → rutina → días → ejercicios con series/reps) + biblioteca.
- **Check-in**: panel "Accesos de hoy", foco/Enter, anti doble-submit, rechazos con motivo.
- **Reportes**: recaudación por período + reimpresión de recibos.
- **Configuración**: gimnasio, sedes, control de acceso, licencia, sistema.
- **Temas**: light y dark consistentes en todas las vistas nuevas.
- **Tests**: 109/109 verdes (Domain 9 + Application 83 + Integration 17).

## ⚠️ Mejoras / cambios leves (por prioridad)

1. **Licencia "Sedes: 2 de 1"** — el seed crea 2 sedes (Central + Norte) pero el tier Free
   permite 1; se muestra el exceso sin advertencia. Decidir: seed con 1 sede, marcar el
   exceso en rojo, o "grandfatherear" las sedes ya existentes. (Es el más visible.)
2. **DatePicker de fecha de nacimiento en inglés** ("day/month/year") en el alta de socio —
   localizar a español o usar un control con cultura es-AR.
3. **PIN de ejemplo "1234" visible** en el login de cajero ("admin de ejemplo: 1234") —
   ocultar en producción (mostrarlo solo en modo demo).
4. **Empty state de socios** dice "Todavía no hay socios" incluso cuando hay socios pero la
   búsqueda/filtro no devuelve nada — distinguir "DB vacía" de "sin resultados".
5. **"Pack 10 Visitas" muestra duración "1 mes"** — para un pack por visitas la duración en
   meses confunde; mostrar la cantidad de visitas o "—".
6. **Email truncado** en la columna angosta de la lista de socios — tooltip o ensanchar.

## Notas
- El recibo PDF no se pudo renderizar en este entorno (falta `pdftoppm`), pero su contenido
  está cubierto por el test `BuildReceiptQuery` (incluye el detalle de la venta de producto).
- Ningún hallazgo es bloqueante: son pulidos de UX/localización y una inconsistencia de
  datos de seed vs. límite de licencia.
