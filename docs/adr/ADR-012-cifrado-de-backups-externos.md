# ADR-012 — Cifrado de los respaldos que salen de la PC (cierre de F1-D2)

**Status:** Accepted
**Date:** 2026-11-11
**Deciders:** **usuario** decidió *cifrar* (sobre "solo advertir") · **arqui** diseña el mecanismo · implementa **dev** · veta **qa**
**Sesión:** Cierre de la deuda **F1-D2** de ADR-004 ("Robustez de producción"). Extiende ADR-004 §backup off-site.
**Piso:** no bajar los tests verdes vigentes. Sin migración EF (no toca esquema).

---

## Context

### El problema (F1-D2, `docs/TASKS.md`)

El respaldo que **sale de la PC de recepción** viaja **sin cifrar** y contiene datos personales y
sensibles de socios (DNI, contacto, y **datos de salud** en `BodyMeasurement`/rutinas) — **Ley 25.326**
(datos sensibles). Un pendrive perdido o robado, o una carpeta sincronizada a Dropbox/OneDrive que sufra
una filtración, es una **fuga de datos personales**. Hoy el texto de Config incluso **recomienda** carpeta
de red y nube (`src/GymForge.Desktop/Views/Settings/SettingsView.axaml:537`), lo que amplía la exposición.

Dos superficies dejan datos de la PC hacia afuera:

1. **Copia off-site automática** — `DatabaseBackupService` copia cada backup local a la carpeta externa
   configurada (pendrive, red, carpeta sincronizada) en el arranque y al cerrar caja
   (`CopyOffsite`, `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs:153-184`).
2. **Exportación / migración** — `DataTransferService` arma un `.zip` con la base + logo + **fotos** que el
   dueño se lleva a otra PC o guarda como respaldo (`ExportAsync`,
   `src/GymForge.Infrastructure/Persistence/DataTransferService.cs:59-92`).

### Qué decidió el usuario

**Cifrar el backup externo** (en vez de solo advertir "guardá el pendrive bajo llave"). Este ADR define
el **mecanismo de clave**, el **algoritmo**, el **formato del contenedor**, qué se cifra, el flujo de
restore y la compatibilidad con backups viejos.

### Hechos del código verificados al 2026-11-11 (no asumidos)

- **PBKDF2 ya existe en el repo** y es la primitiva a reusar: `Pbkdf2PinHasher` usa
  `Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, SHA256, 32)` con salt aleatorio y
  `CryptographicOperations.FixedTimeEquals` (`src/GymForge.Infrastructure/Services/Pbkdf2PinHasher.cs:14-42`).
  Las 100k iteraciones son para el **PIN de login** (debe ser rápido, valor bajo); **no** es la cifra para
  un secreto que protege datos en un medio perdido.
- **El PIN de admin es de 4 dígitos** (espacio de 10.000). El login es por PIN
  (`AuthenticateStaffCommand`). Derivar la clave del backup **solo** del PIN es débil: con el pendrive en
  mano, un atacante fuerza los 10.000 PINs **offline en segundos**, aun con PBKDF2 e iteraciones altas.
  **No se deriva la clave del PIN.**
- **No hay dependencia de cripto más allá de `System.Security.Cryptography`** (`Directory.Packages.props`):
  no hay Argon2/BouncyCastle. `AesGcm` y `Rfc2898DeriveBytes.Pbkdf2` están disponibles en el BCL de net9.0.
  `System.Security.Cryptography.ProtectedData` (DPAPI) **no** está referenciado hoy.
- **El off-site rota y copia atómicamente** con el patrón `gymforge-*.db`
  (`BackupFilePattern`, `:19`; `OffsiteFolderName = "GymForge-backups"`, `:22`; `CopyAtomically`, `:190-203`;
  `Rotate`, `:248-256`; `DeleteOrphanTemps`, `:205-212`). El backup **local** (`CreateLocalBackup`, `:111-136`)
  es un `.db` SQLite directo que el auto-recupero del arranque restaura con un `File.Copy` sobre la base
  (`EnsureHealthy` → `LatestBackup` → `File.Copy`, `:46-65`) **sin** pedir nada al usuario.
- **El import ya valida antes de tocar nada** y **ya pide PIN de admin** para la operación destructiva:
  `InspectAsync` (`DataTransferService.cs:132-190`) lee el `manifest.json`, chequea `FormatVersion` e
  `integrity_check`; `ConfirmImportAsync` valida con `AuthenticateStaffCommand`
  (`src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs:557-584`). `FormatVersion` del paquete es
  una constante (`DataTransferService.cs:23`): un paquete de versión mayor se rechaza con mensaje claro.
- **El export ya excluye lo propio de esta PC**: no viajan `settings.json`, `backups`, `vendor`, `logs`
  ni recibos (test `DataTransferServiceTests.Export_DoesNotCarryFilesThatBelongToThisPc...`). La carpeta
  `vendor` (`%LOCALAPPDATA%\GymForge\vendor`) ya se usa para secretos locales que **no** salen de la máquina
  (clave privada de licencia). Es el lugar natural para el secreto local de backup.
- **DI**: `DatabaseBackupService` y `DataTransferService` se registran como singletons con el
  `connectionString` y resuelven `ILocalSettings`/`ILogger`
  (`src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:39-49`).

### Reglas del proyecto que condicionan el diseño (CLAUDE.md)

- **Offline-first / best-effort del backup:** la copia off-site **nunca debe romper** una operación de
  negocio (hoy es fire-and-forget y no propaga). El cifrado tampoco puede tirar hacia la UI.
- **Simplicidad > cleverness:** se reusa `Rfc2898DeriveBytes.Pbkdf2` y `AesGcm` del BCL; no se inventa
  cripto nueva ni se agrega un framework. Una sola clase de contenedor, un solo formato, bien testeada.
- **Sin migración de esquema:** esto es código + un secreto local; **no** toca la base.

---

## Decisión

### 1) Mecanismo de clave — **frase de acceso del respaldo** (distinta del PIN), con caché local DPAPI

Se adopta la **opción (a) como secreto de registro**: una **frase de acceso del respaldo** que el gimnasio
fija en Config, **distinta del PIN de 4 dígitos**. De ella se deriva la clave de cifrado con **PBKDF2-HMAC-
SHA256** (iteraciones altas; ver abajo). La frase es **independiente de la máquina**: vive en la cabeza del
dueño / su gestor de contraseñas, no en la PC. **Esto es lo que habilita restaurar en OTRA PC** (que es la
razón de existir del backup externo): en la PC nueva se instala GymForge, se importa el paquete y **se tipea
la frase**. Nada atado a esta máquina.

**Por qué (a) y no las otras:**

- **No PIN-only** (descartado explícitamente): 4 dígitos = 10.000 combinaciones → fuerza bruta offline
  trivial con el medio en mano, aun con PBKDF2. No protege nada.
- **No DPAPI-random-key-only (opción b pura)** (descartado como *único* mecanismo): una clave aleatoria
  guardada solo con DPAPI **no sirve para restaurar en otra PC** — DPAPI está atada al usuario+máquina. La
  opción (b) sólo funciona para recuperación si además se **exporta una "clave de recuperación"** que el
  gimnasio guarda aparte → termina teniendo **el mismo modo de falla** que (a) ("si perdés el secreto no hay
  restore"), pero con **más piezas** y un **peor footgun**: se podrían acumular meses de backups que
  literalmente **no se pueden restaurar** porque nadie exportó la clave de recuperación. En (a), en cambio,
  **no se puede crear un backup externo sin fijar antes la frase**, así que el secreto de recuperación
  **siempre existe**.
- **Fuerza real:** la debilidad de (a) es la calidad de la frase humana. Se mitiga con **longitud mínima**,
  **iteraciones altas** y **cifrado autenticado** (ver §4). El modelo de amenaza es "medio perdido / nube
  filtrada", no "Estado-nación con el equipo": una frase de ≥ 12 caracteres con 600.000 iteraciones PBKDF2
  es adecuada.

**La pieza (b) que sí se toma — caché local DPAPI, por necesidad de automatismo:** la copia off-site es
**automática y desatendida** (corre en el arranque, antes de que nadie tipee nada, y al cerrar caja). **No se
puede pedir la frase en cada backup.** Por eso el app guarda una **copia local de la frase protegida con
Windows DPAPI (scope CurrentUser)** en `%LOCALAPPDATA%\GymForge\vendor\` (junto a los otros secretos locales
que **no** salen de la PC). Esa caché:

- permite cifrar los backups automáticos **sin prompt**;
- **nunca sale** de la PC (ya excluida del export: `vendor` no viaja);
- **no** es necesaria para restaurar (el restore re-deriva la clave de la **frase tipeada**). Si la PC muere,
  la caché muere con ella — **irrelevante**, porque la recuperación usa la frase.

Esto es, en efecto, un **híbrido simple**: **un solo secreto de registro** (la frase) con una **caché local
DPAPI** para operar desatendido. Conceptualmente limpio y mínimo.

> **Nueva dependencia (decisión explícita):** DPAPI requiere el paquete first-party de Microsoft
> `System.Security.Cryptography.ProtectedData`. Es la **única** dependencia nueva; se justifica porque el
> cifrado automático la *necesita* (el usuario pidió "sin deps nuevas salvo que haga falta" — acá hace
> falta). Se pinea centralmente en `Directory.Packages.props`. Alternativa sin paquete: P/Invoke a
> `CryptProtectData` — se **rechaza** por fragilidad; el paquete first-party es más limpio.

**Trade-off que se deja EXPLÍCITO (sin letra chica):** **si se pierde la frase de acceso, los respaldos
cifrados son irrecuperables.** No hay backdoor, no hay escrow, no hay "resetear la frase y leer backups
viejos". Es el costo inevitable del cifrado real. La UX lo refuerza (ver §5). Mitigación operativa: al fijar
la frase se muestra un aviso de una sola vez ("anotala y guardala **aparte**, no en el mismo pendrive") y se
ofrece, opcional, guardarla/imprimirla en una "hoja de recuperación".

### 2) Qué se cifra y qué no

| Artefacto | ¿Se cifra? | Por qué |
|---|---|---|
| **Copia off-site** de `DatabaseBackupService` (pendrive/red/nube) | **SÍ** | Sale de la PC. Es el caso de la fuga. |
| **Export/migración** `.zip` de `DataTransferService` (incluye fotos) | **SÍ** | Sale de la PC (mudanza, respaldo, nube, mail). |
| **Backup local** en `%LOCALAPPDATA%\GymForge\backups\*.db` | **NO** | No sale de la PC. Vive **al lado de la base viva, que ya está en claro**: cifrarlo no suma contra "medio robado". Y el **auto-recupero del arranque** (`EnsureHealthy`) necesita restaurar un `.db` **usable al instante y sin frase** — cifrarlo rompería el self-heal desatendido. |
| `settings.json`, `logs`, `vendor` | **NO** (y no salen) | Específicos de la PC; ya excluidos del export. |

**Regla:** se cifra **en el borde donde el dato sale de la PC**. El backup local queda en claro para no tocar
`EnsureHealthy`/`LatestBackup` ni el camino de recuperación de corrupción.

### 3) Restore / import — descifrado, frase incorrecta y compat de lectura

- **Detección por magic bytes, no por extensión:** `InspectAsync`/`ImportAsync` leen los primeros 4 bytes.
  Si son `GFBK` → paquete **cifrado**; si no, y es un zip con `manifest.json` → paquete **legacy sin cifrar**
  (se sigue importando; **compat de lectura**).
- **Flujo cifrado (UI):** elegir archivo → detecta cifrado → **pide la frase de acceso** → descifra a un
  temporal → corre la validación existente (`manifest` + `integrity_check`) → muestra el resumen →
  **pide PIN de admin** (como hoy) → `ImportAsync`. Es decir, import cifrado exige **frase** (para descifrar)
  **y** **PIN** (para autorizar el reemplazo destructivo). La frase va **antes** del resumen: no hay metadato
  en claro que mostrar sin la frase (evita filtrar el nombre del gimnasio del manifest). *(Opción si se
  quiere pre-chequeo antes de tipear la frase: un header de metadatos no-personales —nombre del gimnasio,
  fecha, contadores— en claro. El nombre del gimnasio es dato de la empresa, no del socio. Se deja como
  mejora opcional; el default es no filtrar nada.)*
- **Frase incorrecta:** AES-GCM falla la verificación del tag → `DataPackageException` con mensaje claro,
  **sin crash** y **sin tocar los datos actuales**: *"La frase de acceso es incorrecta, o el archivo está
  dañado."* (GCM no distingue frase mala de archivo corrupto; el mensaje cubre ambos). Permite reintentar.
- **Export sin frase fijada:** se **bloquea** el export y se encamina a fijar la frase primero (modal
  inline). No se produce silenciosamente un paquete en claro que sale de la PC. Pero no se deja al gimnasio
  **sin** respaldo: fijar la frase es parte del mismo flujo.
- **Off-site configurado sin frase:** la copia off-site **se omite** (nunca escribe en claro) y el estado de
  Config pasa a **"Requiere atención: falta la frase de acceso para cifrar la copia externa"** (reusa el pill
  `OffsiteNeedsAttention` existente). Respeta el contrato best-effort (no tira).

### 4) Algoritmo y formato del contenedor (`GFBK` v1)

- **Cifrado:** **AES-256-GCM** (autenticado: confidencialidad + integridad + detección de manipulación).
  `new AesGcm(key, tagSizeInBytes: 16)` (es `IDisposable`).
- **KDF:** **PBKDF2-HMAC-SHA256**, **600.000 iteraciones** por defecto (OWASP 2023 para PBKDF2-SHA256; el
  PIN usa 100k porque es otro propósito). **Salt aleatorio de 16 bytes por archivo.** Clave de 32 bytes.
  Frase normalizada: `Encoding.UTF8.GetBytes(frase.Normalize(NormalizationForm.FormC))`. Las iteraciones van
  **en el header** → se pueden subir en el futuro sin romper la lectura de backups viejos.
- **Contenedor "troceado" (chunked), una sola clase bien testeada.** Motivo: el export incluye **fotos** →
  el payload puede ser de cientos de MB; un `AesGcm` *single-shot* sobre un `byte[]` gigante arriesga OOM en
  una PC de recepción modesta. `AesGcm` del BCL no es streaming, así que se trocea en bloques de 1 MiB, cada
  bloque con su propio GCM. Memoria acotada; el `.db` off-site chico cae en un único bloque sin caso especial.

**Layout (big-endian):**

```
Header (claro, autenticado como AAD en cada bloque):
  "GFBK"            4 bytes   magic
  version           1 byte    = 0x01
  algId             1 byte    = 0x01  (PBKDF2-SHA256 / AES-256-GCM)
  flags             2 bytes   = 0 (reservado)
  iterations        4 bytes   UInt32  (default 600000)
  saltLen           1 byte    = 16
  salt              16 bytes
  noncePrefix       4 bytes   aleatorio, fijo por archivo
  chunkSize         4 bytes   UInt32  plaintext por bloque (default 1048576)

Por cada bloque i = 0..n-1:
  isFinal           1 byte    (0/1, en claro, pero autenticado por el AAD)
  len               4 bytes   UInt32  bytes de texto plano de este bloque (== len del ciphertext)
  ciphertext        len bytes
  tag               16 bytes
  donde:
    nonce_i = noncePrefix(4) || UInt64BE(i)          // 12 bytes, único por bloque
    aad_i   = headerBytes || UInt64BE(i) || isFinal  // ata header, orden y marca de fin
```

- **Clave por archivo** (salt por archivo) ⇒ `(clave, nonce)` único garantizado; GCM seguro.
- **Anti-truncado / anti-reordenamiento:** el índice `i` y el `isFinal` van en el **AAD** (no se pueden
  tocar sin romper el tag). Al descifrar se lee hasta encontrar el bloque con `isFinal=1`; si se llega a EOF
  **sin** un bloque final → *"archivo truncado o dañado"*. El header completo como AAD ata salt/nonce/iters.
- **Versionado:** `version` + `algId` + `iterations` en el header permiten leer backups viejos y, más
  adelante, cambiar de algoritmo o subir iteraciones sin romper compat. Un `version`/`algId` desconocido →
  mensaje claro ("respaldo creado con una versión más nueva; actualizá GymForge"), igual que hoy con
  `FormatVersion`.
- **Nombre/extensión off-site:** los archivos cifrados off-site usan una extensión propia (p. ej.
  `gymforge-*.db.gfbak`) para que **nadie confunda** un blob cifrado con un SQLite usable. **Esto obliga a
  dev a actualizar el patrón de rotación/`.tmp` del off-site** (hoy `BackupFilePattern = "gymforge-*.db"`):
  agregar un patrón dedicado para el off-site cifrado en `Rotate`/`DeleteOrphanTemps`/`CopyAtomically`. El
  `LatestBackup()` del **local** no se toca (sigue escaneando `*.db` en la carpeta local, que queda en claro).
- **Export:** el `.zip` se arma como hoy en un temporal y **se cifra el zip completo** al contenedor
  `GFBK`; la extensión sugerida pasa a `.gfbak`. Se sigue **aceptando** `.zip` legacy al importar.

### 5) Texto de Config (qué cambia)

- Nuevo sub-bloque en la card "Copia de seguridad y migración": **"Frase de acceso del respaldo"** (fijar /
  cambiar; estado "Configurada ✓" sin mostrarla). Fijar/cambiar la frase **pide PIN de admin** (misma barra
  que el import) y muestra el aviso de pérdida irreversible.
- El texto que hoy recomienda red/Dropbox/OneDrive (`SettingsView.axaml:537`) cambia a: una vez cifrado, se
  **puede** usar carpeta de red o sincronizada **porque la copia va cifrada**, con la advertencia:
  *"La copia externa viaja cifrada con tu frase de acceso. Guardá la frase **aparte** del pendrive/carpeta:
  si la perdés, el respaldo no se puede recuperar."*
- Si no hay frase fijada: el estado off-site muestra **"Requiere atención — falta la frase de acceso"** y la
  copia no se hace (ver §3).

### 6) Migración / compatibilidad

- **Sin migración de esquema.** No toca la base.
- **Backups/zips viejos sin cifrar:** se **siguen leyendo** (import detecta por magic bytes; si no es `GFBK`
  y es un zip válido con manifest → legacy, se importa). Los **nuevos** siempre salen cifrados.
- **Backups locales:** sin cambios (quedan en claro; el self-heal del arranque no se toca).
- **Primer arranque tras el update:** si hay un off-site configurado pero aún no hay frase, la copia se omite
  con el aviso de Config hasta que el gimnasio fije la frase (no degrada seguridad: deja de salir dato en
  claro; el backup local sigue). No se migra ni re-cifra nada automáticamente.

---

## Diseño para dev (resumen de componentes)

- **`BackupCrypto`** (Infrastructure, p. ej. `Persistence/BackupCrypto.cs`): implementa el contenedor `GFBK`.
  API sugerida, orientada a stream para acotar memoria:
  `void Encrypt(Stream plain, Stream cipher, string passphrase)` y
  `void Decrypt(Stream cipher, Stream plain, string passphrase)`; más `bool LooksEncrypted(Stream)` (lee el
  magic). Reusa `Rfc2898DeriveBytes.Pbkdf2` y `AesGcm`. **No** cargar el payload entero en memoria.
- **`IBackupKeyStore`** (Application/Interfaces) + **`DpapiBackupKeyStore`** (Infrastructure): fija / lee /
  borra la frase en `vendor\` protegida con `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`;
  `bool HasPassphrase`, `string? GetPassphrase()`, `void SetPassphrase(string)`. **Nunca** devuelve la frase
  a la UI para mostrarla; sólo la consume el cifrado.
- **`DatabaseBackupService.CopyOffsite`**: en vez de `CopyAtomically(backupPath, dest)`, cifrar el `.db`
  local → `dest.gfbak` con `BackupCrypto` usando la frase de `IBackupKeyStore`; si no hay frase → **omitir**
  y devolver un outcome que la UI muestre como "requiere atención" (nuevo valor del enum
  `OffsiteBackupOutcome`, p. ej. `MissingPassphrase`, o reusar `Failed` con mensaje). Actualizar
  `Rotate`/`DeleteOrphanTemps` al patrón `*.gfbak`.
- **`DataTransferService.ExportAsync`**: tras armar el zip temporal, cifrarlo a `destino` (`.gfbak`) con
  `BackupCrypto`. **`InspectAsync`/`ImportAsync`**: si `LooksEncrypted`, descifrar a un temporal con la frase
  (nuevo parámetro `passphrase`) antes de la validación actual; si no, seguir el camino legacy. Mantener el
  contrato de no tocar los datos locales si algo falla.
- **`SettingsViewModel`/`SettingsView.axaml`**: sub-bloque "Frase de acceso" (fijar/cambiar con PIN);
  prompt de frase en el flujo de import; bloqueo de export sin frase; nuevo texto/advertencia del off-site.
- **DI** (`InfrastructureServiceExtensions.cs:39-49`): registrar `IBackupKeyStore` singleton y pasarlo a
  `DatabaseBackupService` y `DataTransferService`.
- **`Directory.Packages.props`**: agregar `System.Security.Cryptography.ProtectedData` (pin central).

**No tocar:** `EnsureHealthy`/`LatestBackup`/`ArchiveCorrupt` ni el patrón de los backups **locales**
(`*.db` en claro); el esquema EF; `Pbkdf2PinHasher` (es para el PIN, otro propósito); el contrato
best-effort de la copia off-site (sigue sin tirar hacia la UI).

---

## Tests / verificación

- **Round-trip** cifrar → descifrar: payload idéntico (incluye un caso **> `chunkSize`**, multi-bloque, para
  cubrir el troceo y el `isFinal`).
- **Frase incorrecta:** el descifrado lanza `DataPackageException` con el mensaje claro; **no** modifica los
  datos locales.
- **Backup/zip legacy sin cifrar:** `InspectAsync`/`ImportAsync` lo siguen importando (compat de lectura).
- **Manipulación / truncado:** alterar un byte del ciphertext o cortar el archivo → falla la verificación
  GCM (tag) o la detección de truncado; nunca produce datos corruptos silenciosos.
- **Restore en otra PC (simulado):** cifrar con la frase en "PC A", descifrar en una instalación limpia "PC
  B" (sin la caché DPAPI de A) usando **sólo la frase** → éxito (prueba la independencia de máquina).
- **Off-site sin frase:** `CopyOffsite` **no** escribe nada en claro y reporta "requiere atención".
- **Off-site con frase:** el archivo off-site es `GFBK` (no un SQLite legible) y rota correctamente con el
  patrón nuevo.
- **Export sin frase:** bloqueado con mensaje; con frase: genera `.gfbak`.
- **DPAPI key store:** set → get round-trip; el valor en disco **no** es la frase en claro; `vendor` **no**
  viaja en el export (ya cubierto por `DataTransferServiceTests`; extenderlo a `backup-pass.dpapi`).
- **Memoria:** cifrar un payload grande (p. ej. 300 MB simulados) no dispara OOM (valida el troceo).

Ubicación: `tests/GymForge.Integration.Tests/` (junto a `DataTransferServiceTests.cs`,
`DatabaseBackupOffsiteTests.cs`) + un test unitario de `BackupCrypto`.

---

## Consecuencias

- **Seguridad:** lo que sale de la PC viaja cifrado (AES-256-GCM autenticado). Carpeta de red y nube pasan a
  ser aceptables **con cifrado**; se levanta la condición de uso de F1-D2.
- **Recuperación cross-PC preservada:** la frase es el único secreto necesario para restaurar en otra PC;
  independiente de la máquina. La caché DPAPI es sólo para el automatismo y no condiciona el restore.
- **Compat:** backups viejos sin cifrar se siguen leyendo; los nuevos salen cifrados. Sin migración de
  esquema. El self-heal del arranque y los backups locales no cambian.
- **Dependencia nueva:** una, first-party (`System.Security.Cryptography.ProtectedData`), justificada.
- **Operación:** un paso humano nuevo (fijar y **custodiar** la frase). Es el costo del cifrado real.

## Riesgos

- **Pérdida de la frase → respaldo irrecuperable** (por diseño). Mitigación: aviso de una sola vez al fijar,
  advertencia en Config, "hoja de recuperación" opcional. **No** hay escrow (sería una puerta trasera).
- **Frase débil** elegida por recepción. Mitigación: longitud mínima + 600k iteraciones + GCM; el modelo de
  amenaza no es un adversario con recursos ilimitados sobre el medio.
- **Olvido de fijar la frase** → el off-site deja de copiar (no copia en claro). Mitigación: estado visible
  "requiere atención" y bloqueo del export con encaminamiento a fijarla; el backup **local** sigue intacto.
- **Caché DPAPI** legible por quien corre código como ese usuario Windows — **aceptable**: ese atacante ya
  tiene la base viva en claro en la misma PC. El cifrado defiende el **medio externo**, no la PC comprometida.
- **Memoria con fotos grandes** en el export. Mitigado por el troceo (1 MiB) y la API orientada a stream.
- **Payload grande / iteraciones** → ~0,5 s de PBKDF2 por archivo; los backups son infrecuentes: despreciable.

## Alternativas rechazadas

- **Derivar la clave sólo del PIN (4 dígitos):** 10.000 combinaciones, fuerza bruta offline trivial. No.
- **Clave aleatoria sólo con DPAPI (opción b pura):** no restaura en otra PC sin una clave de recuperación
  exportada aparte → mismo modo de falla que (a) con más piezas y peor footgun.
- **Sólo advertir (no cifrar):** descartado por el usuario; deja el dato sensible en claro en un medio que se
  pierde/sincroniza a la nube.
- **Cifrar también los backups locales:** no suma contra "medio robado" (la base viva está en claro al lado)
  y rompería el self-heal desatendido del arranque.
- **AES-GCM single-shot sobre un `byte[]`:** riesgo de OOM con el export de fotos; se trocea.
- **Agregar Argon2id (BouncyCastle):** mejor KDF, pero es dependencia nueva de terceros; PBKDF2-SHA256 a
  600k es suficiente para el modelo de amenaza y ya está en el BCL (Simplicidad > cleverness). Se puede
  migrar a futuro vía `algId` del header sin romper compat.

## Referencias

- Off-site: `src/GymForge.Infrastructure/Persistence/DatabaseBackupService.cs:19,22,111-136,153-184,190-203,205-212,248-256`
- Export/import: `src/GymForge.Infrastructure/Persistence/DataTransferService.cs:23,25-27,59-92,132-190,209-240`
- PBKDF2 a reusar: `src/GymForge.Infrastructure/Services/Pbkdf2PinHasher.cs:9-42`
- Config: `src/GymForge.Desktop/Views/Settings/SettingsView.axaml:488-608` (texto a cambiar en `:537`) ·
  `src/GymForge.Desktop/ViewModels/Settings/SettingsViewModel.cs:488-593`
- DI: `src/GymForge.Infrastructure/InfrastructureServiceExtensions.cs:32,39-49`
- Contrato backup: `src/GymForge.Application/Interfaces/IRepositories.cs:301-325`
  (`IDatabaseBackup`, `OffsiteBackupOutcome`) · `IDataTransfer.cs`
- Deps: `Directory.Packages.props` (sin cripto de terceros hoy)
- Deuda / políticas: `docs/TASKS.md` (F1-D2) · `docs/PROGRESS.md` · ADR-004 (F1 backup off-site)
