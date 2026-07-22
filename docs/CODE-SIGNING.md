# Firma de código (Windows)

Sin firma, al instalar `GymForge-win-Setup.exe` Windows muestra el aviso de
**SmartScreen "editor desconocido"**, que asusta a un dueño de gimnasio no técnico.
Firmar el ejecutable y el instalador con un certificado de una CA reconocida resuelve
(o atenúa) ese aviso.

El pipeline ya está listo en `scripts/pack.ps1`: firma automáticamente si hay un
certificado configurado por variable de entorno, y empaqueta sin firmar si no lo hay.

## Opciones (de gratis a caro)

| Opción | Costo | ¿Saca el aviso? | Notas |
|--------|-------|-----------------|-------|
| **Autofirmado** (lo que usamos hoy) | Gratis | Solo en las PCs donde instalamos el certificado a mano | Ideal para el piloto y para gimnasios donde el equipo va a instalar. Ver abajo. |
| SignPath Foundation | Gratis | Sí | **No aplica**: solo para proyectos open source con licencia OSI y repo público. |
| Certum Open Source | ~EUR 30/año | Sí (reputación se acumula) | El certificado "real" más barato. Validación de identidad de la persona. |
| **Azure Trusted Signing** | ~USD 10/mes | Sí | Recomendado cuando empecemos a vender a gimnasios que instalan solos. Pide historial verificable de la organización. |
| OV Code Signing (CA clásica) | USD 150–300/año | Sí, con el tiempo | Se entrega como `.pfx`. |
| EV Code Signing | USD 300–600/año | Sí, **inmediato** desde la primera instalación | La clave vive en un token USB/HSM. |

No existe certificado gratuito de una CA reconocida: lo que se paga no es el archivo sino
la validación de identidad (persona o razón social/CUIT). CAs habituales: DigiCert,
Sectigo/Comodo, GlobalSign, SSL.com; para AR/LATAM cualquiera sirve.

---

## Autofirmado (gratis) — el camino actual

Un certificado que generamos nosotros. Windows no confía en él por defecto, pero **sí**
en cada PC donde instalemos su parte pública. Como al piloto lo instalamos nosotros, el
resultado en la PC del gimnasio es el mismo que con un certificado pago: el instalador
aparece firmado por **GymForge** en vez de "Editor desconocido".

Límite honesto: en una PC que no tenga el certificado instalado (por ejemplo, alguien que
baja el `.exe` de GitHub), el aviso sigue apareciendo igual que sin firmar.

### 1. Generar el certificado (una sola vez, en la máquina de desarrollo)

```powershell
.\scripts\sign-selfsigned.ps1 -BackupPfx
```

Crea el certificado en el store del usuario (válido 5 años), exporta la parte pública a
`%LOCALAPPDATA%\GymForge\vendor\gymforge-code-signing.cer` y, con `-BackupPfx`, un `.pfx`
de respaldo con la clave privada (pide una password).

Para respaldar directo a un pendrive: `-PfxPath E:\gymforge-code-signing.pfx`.

> **El `.pfx` es la llave para firmar como "GymForge".** Va en un pendrive guardado, aparte
> del que se lleva al gimnasio. Si se pierde, hay que generar un certificado nuevo y volver
> a instalarlo en **todas** las PCs que ya lo tenían. Ver [DISTRIBUCION.md](DISTRIBUCION.md).

Correr el script de nuevo reusa el certificado existente; `-Force` genera uno nuevo.

### 2. Empaquetar firmado

```powershell
$env:GYMFORGE_SIGN_THUMBPRINT = '<la huella que imprime el script>'
.\scripts\pack.ps1 -Version 0.3.0
```

Para el ejecutable portable (`publish.ps1`) la firma sale sola con esa misma variable.
Un exe suelto también se puede firmar a mano, sin necesidad del Windows SDK:

```powershell
.\scripts\sign-selfsigned.ps1 -SignFile .\dist\GymForge.exe
```

### 3. Instalar el certificado en la PC del gimnasio (una vez por PC)

El pendrive se arma solo con `.\scripts\make-usb.ps1 -Destino F:\` (ver
[DISTRIBUCION.md](DISTRIBUCION.md)). Ahí van `gymforge-code-signing.cer` y `trust-cert.ps1`
junto al instalador. En la PC del gimnasio, en PowerShell **como administrador**:

```powershell
.\trust-cert.ps1 -CerPath .\gymforge-code-signing.cer
```

Después de eso, `GymForge-win-Setup.exe` y las actualizaciones aparecen como editor
verificado. Para desinstalarlo: `.\trust-cert.ps1 -Remove`.

Si el instalador llegó descargado del navegador, Windows le pone una marca de "archivo de
internet" y SmartScreen puede avisar igual (su reputación es un servicio en la nube de
Microsoft, que no conoce nuestro certificado). Se evita copiando el instalador por pendrive
o red local, o quitando la marca: `Unblock-File .\GymForge-win-Setup.exe`.

---

## Cómo firmar con un certificado de CA (cuando compremos uno)

Con `signtool.exe` disponible (viene con el Windows SDK).

**Certificado en archivo PFX (OV):**
```powershell
$env:GYMFORGE_SIGN_PFX      = 'C:\ruta\segura\gymforge.pfx'
$env:GYMFORGE_SIGN_PFX_PASS = '<password del pfx>'
.\scripts\pack.ps1 -Version 0.3.0
```

**Certificado en el store de Windows / token EV (por huella):**
```powershell
# La huella SHA1 se ve en certmgr.msc → Detalles → Huella digital (sin espacios).
$env:GYMFORGE_SIGN_THUMBPRINT = 'A1B2C3...'
.\scripts\pack.ps1 -Version 0.3.0
```

Opcional, cambiar el servidor de timestamp (default `http://timestamp.digicert.com`):
```powershell
$env:GYMFORGE_SIGN_TIMESTAMP = 'http://timestamp.sectigo.com'
```

Sin ninguna de esas variables, `pack.ps1` empaqueta **sin firmar** (útil en dev) y avisa
en amarillo.

## Verificar la firma

```powershell
signtool verify /pa /v .\releases\GymForge-win-Setup.exe
# o, ya instalado:
Get-AuthenticodeSignature "$env:LOCALAPPDATA\GymForge\current\GymForge.exe"
```

## Seguridad

- **Nunca** commitear el `.pfx` ni su password (ni el de la CA ni el autofirmado). Guardar
  el PFX fuera del repo y pasar la password por variable de entorno o un secret manager.
- El `.cer` (parte pública) sí se puede repartir libremente: es lo que se instala en la PC
  del gimnasio y no permite firmar nada.
- El token EV no exporta la clave privada (por diseño): firmá desde la máquina que lo tiene
  conectado o desde un runner con el HSM.
