# Migración de datos entre PCs — diseño

Fecha: 2026-07-22 · Estado: implementado

## Problema

No hay forma de llevarse los datos del gimnasio de una PC a otra, ni de sacar un respaldo
que el dueño pueda guardar por su cuenta. Si la PC de recepción se rompe o se cambia, se
pierde todo lo que no esté en los backups automáticos locales — que viven en la misma PC.

## Alcance

Un paquete `.zip` que contiene todo lo del gimnasio, con dos operaciones desde
Configuración: **exportar** (respaldo / mudanza) e **importar** (restaurar en otra PC).

Fuera de alcance, a propósito:

- Fusionar dos bases (mezclar socios de dos gimnasios abre conflictos de IDs que hoy no
  se justifican).
- Sincronización automática a la nube.

## Contenido del paquete

```
manifest.json     FormatVersion, versión de la app, nombre del gimnasio, fecha y
                  contadores (socios, cobros) para el resumen previo a importar
gymforge.db       copia consistente, hecha con el backup API de SQLite
archivos/brand/   logo del gimnasio
archivos/fotos/   fotos de socios y de mediciones corporales
```

No viajan: recibos PDF (se regeneran desde la base con `BuildReceiptQuery`), backups,
logs, ni la carpeta `vendor` (claves privadas del vendedor: no son datos del gimnasio y
no deben salir de la máquina de desarrollo).

## La decisión que hace que funcione: reescritura de rutas

La base guarda **rutas absolutas** de archivos (`Company.LogoUrl`, `Member.PhotoUrl`,
`BodyMeasurement.PhotoFront/Side/Back`). Esas rutas incluyen el perfil de usuario de
Windows del equipo de origen, que en la PC nueva no existe.

Por eso `ImportAsync` reapunta cada ruta a la carpeta de datos local, conservando solo el
nombre del archivo. Sin ese paso el paquete sirve como respaldo pero no como mudanza: las
imágenes aparecerían rotas aunque los archivos estén copiados.

## Componentes

| Dónde | Qué |
|---|---|
| `IDataTransfer` (Application/Interfaces) | `SuggestedFileName` · `ExportAsync` · `InspectAsync` · `ImportAsync` |
| `DataPackageInfo` | Contenido del manifest, también usado para el resumen en pantalla |
| `DataPackageException` | Paquete inválido; su mensaje se muestra tal cual al usuario |
| `DataTransferService` (Infrastructure/Persistence) | Implementación, junto a `DatabaseBackupService` |
| `SettingsViewModel` | `ExportDataCommand` · `ChooseImportFileCommand` · `ConfirmImportCommand` · `CancelImportCommand` |
| `SettingsView.axaml` | Sección "Copia de seguridad y migración" |

## Flujos

**Exportar** — elegir destino → `ExportAsync` → mensaje con el resumen. La app sigue
usable: la copia de la base es consistente aunque haya gente fichando.

**Importar** — elegir archivo → `InspectAsync` valida y devuelve el contenido → se muestra
el resumen (gimnasio, socios, cobros, fecha) y se pide **PIN de administrador** →
`ImportAsync` respalda la base actual (`BackupNow("pre-import")`), reemplaza, reapunta
rutas → **la app se reinicia**.

El reinicio es deliberado: el `DbContext` y las pantallas quedaron con datos del gimnasio
anterior, y arrancar de cero es más seguro que refrescar todo en caliente. Además, el
arranque corre `MigrateAsync()`, así que un paquete de una versión anterior de GymForge
queda migrado sin trabajo extra.

## Errores

| Situación | Comportamiento |
|---|---|
| No es un zip / está dañado | "El archivo está dañado o no es un .zip válido." |
| Zip sin `manifest.json` | "Este archivo no es una exportación de GymForge." |
| `FormatVersion` mayor a la soportada | Se rechaza indicando la versión que lo generó |
| Base del paquete corrupta (`integrity_check`) | Se rechaza **antes** de tocar los datos locales |
| PIN incorrecto | No procede |
| Base tomada por el DbContext al reemplazar | Reintentos con `ClearAllPools` y espera creciente |

La validación completa ocurre en `InspectAsync`, que no modifica nada. `ImportAsync` la
invoca primero: si el paquete no sirve, los datos actuales quedan intactos.

## Detalle de implementación

Las conexiones a archivos temporales usan `Pooling=False`. Sin eso, el pool de
`Microsoft.Data.Sqlite` conserva el handle después del `Dispose` y el archivo no se puede
comprimir ni borrar — se manifestaba como `IOException: being used by another process`.

## Tests

`DataTransferServiceTests` (7 casos): round-trip completo entre dos instalaciones
simuladas con verificación de la reescritura de rutas; backup previo al import; y los
rechazos de paquete no-GymForge, versión futura y base corrupta, incluido el caso de que
un import fallido deje los datos actuales sin tocar.
