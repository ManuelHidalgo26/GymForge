# Notificaciones + cobro automático (dunning)

Recordatorios de cobro/vencimiento por WhatsApp (o SMS). Esta es la **base**: la lógica
de dunning está lista y testeada, y funciona de punta a punta con un sender que registra
en el log. El envío real se enchufa implementando un proveedor.

## Piezas

- `INotificationSender` (`Application/Interfaces`): abstracción de envío. `NotificationMessage`
  = teléfono + texto + canal (WhatsApp/SMS).
- `LogNotificationSender` (`Infrastructure/Services`): implementación por defecto; **registra
  el mensaje en el log** en vez de enviarlo. Permite operar y probar sin proveedor.
- `DunningConfig` (`Application/UseCases/Dunning`): `Enabled` (off por defecto) + `Stages`
  (recordatorios a **1, 3, 7, 15 y 30 días** de vencido, con tono creciente). Cada etapa es
  `DunningStage(DaysOverdue, Label, Template)`; la plantilla admite `{nombre}`, `{gimnasio}`,
  `{monto}`, `{vencimiento}` y `{dias}`. Se persiste por empresa en `Company.DunningJson`
  (`DunningSettings`), se hidrata al arrancar (`SessionContext`) y se edita en Configuración →
  "Cobranzas y recordatorios" (`UpdateDunningSettingsCommand`, aplica en caliente). Es config de
  negocio: viaja con el export de la base.
- `DunningMessageRenderer`: arma el texto desde la plantilla de la etapa. Lo usan el job y el
  recordatorio manual, así el socio recibe lo mismo por los dos caminos.
- `DunningService`: recorre los cobros vencidos y, para los que cumplen **exactamente** una
  etapa a la fecha, arma el mensaje y lo envía. Cada aviso enviado queda en `NotificationLog`
  (append-only) y no se reenvía una etapa ya registrada para el mismo cargo.
- **Recordatorio manual por WhatsApp** (`wa.me`, gratis): pantalla Morosos (por sede) →
  `SendWhatsAppReminderCommand` arma el mensaje de la etapa vigente, registra el aviso como
  "iniciado por el operador" (no hay confirmación de entrega) y devuelve el link, que
  `IWhatsAppLinkSender` (Desktop) abre en WhatsApp Desktop/Web. Funciona con el dunning
  automático encendido o apagado. La normalización del celular (`WhatsAppLink`) es best-effort para
  Argentina.
- `DailyMaintenanceHost` (`Desktop/Services`): dispara el mantenimiento diario (vence membresías, marca
  mora y corre el dunning) **al abrir la app y cada 6 horas** mientras siga abierta. Hace catch-up de los
  días perdidos (hasta 35) desde el checkpoint `%LOCALAPPDATA%\GymForge\maintenance-last-run.txt`, con el
  día **local** del gimnasio. Best-effort, nunca crashea. Reemplaza al viejo `DunningStartup`.

## Cómo activarlo

1. **Elegir proveedor** e implementar `INotificationSender`:
   - **Twilio** (WhatsApp Business API): requiere cuenta + número aprobado; se llama a la API
     REST de Messages con `From` (whatsapp:+…) y `To`.
   - **Wassenger** u otro gateway: más simple (usa un WhatsApp común vía API HTTP).
   Ejemplo de registro (reemplaza al log sender):
   ```csharp
   services.AddSingleton<INotificationSender, TwilioNotificationSender>();
   ```
2. **Habilitar** el dunning desde Configuración → "Cobranzas y recordatorios" (toggle de aviso
   automático). Sin proveedor real el job solo registra en el log lo que enviaría; el canal gratis
   real es el recordatorio manual de la pantalla Morosos.

## Limitaciones actuales / próximos pasos

- **Idempotencia por día exacto**: cada etapa dispara solo el día exacto. El catch-up del mantenimiento
  diario recorre los días perdidos (hasta 35) y corre el dunning de cada uno, así que una etapa que cayó con
  el gym cerrado se recupera al reabrir. Si el checkpoint obliga a repetir un día, `NotificationLog`
  (dedup por cargo + etapa) evita reenviar el mismo aviso.
- **Sin opt-out**: falta un flag de "no molestar" por socio y respetar horarios.
- **Proveedor real**: el envío automático de WhatsApp/SMS (API paga) sigue fuera; falta exponer proveedor +
  credenciales en Configuración.
- **Métricas**: entregado/leído/respondido según lo que reporte el proveedor.
