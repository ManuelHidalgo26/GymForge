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
- `DunningStartup` (`Desktop/Services`): lo corre **una vez por día al abrir la app**
  (marcador en `%LOCALAPPDATA%\GymForge\dunning-last-run.txt`). Best-effort, nunca crashea.

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

- **Idempotencia por día exacto**: si la app no se abre el día en que una etapa cae (ej. el
  gym cerró), esa etapa se saltea. El paso siguiente es una tabla `NotificationLog` que
  registre qué recordatorio se envió a cada cobro, para reintentar y no duplicar.
- **Sin opt-out**: falta un flag de "no molestar" por socio y respetar horarios.
- **Config en UI**: exponer proveedor + credenciales + plantillas + activación en Configuración.
- **Métricas**: entregado/leído/respondido según lo que reporte el proveedor.
