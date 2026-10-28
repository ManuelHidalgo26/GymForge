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
  (recordatorios a **1, 3, 7, 15 y 30 días** de vencido, con tono creciente).
- `DunningService`: recorre los cobros vencidos y, para los que cumplen **exactamente** una
  etapa a la fecha, arma el mensaje (`DunningTemplates`) y lo envía. Idempotente si corre una
  vez por día (cada etapa dispara solo el día exacto).
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
2. **Habilitar** el dunning: `DunningConfig.Enabled = true` (hoy es código/DI; el próximo paso
   es exponerlo en Configuración junto con las credenciales del proveedor y el texto de las
   plantillas).

## Limitaciones actuales / próximos pasos

- **Idempotencia por día exacto**: cada etapa dispara solo el día exacto. El catch-up del mantenimiento
  diario recorre los días perdidos (hasta 35) y corre el dunning de cada uno, así que una etapa que cayó con
  el gym cerrado se recupera al reabrir. Si la falla del checkpoint obliga a repetir un día, los avisos de
  ese día se reenvían: el paso siguiente es una tabla `NotificationLog` que registre qué recordatorio se
  envió a cada cobro, para reintentar y no duplicar.
- **Sin opt-out**: falta un flag de "no molestar" por socio y respetar horarios.
- **Config en UI**: exponer proveedor + credenciales + plantillas + activación en Configuración.
- **Métricas**: entregado/leído/respondido según lo que reporte el proveedor.
