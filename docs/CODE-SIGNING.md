# Firma de código (Windows)

Sin firma, al instalar `GymForge-win-Setup.exe` Windows muestra el aviso de
**SmartScreen "editor desconocido"**, que asusta a un dueño de gimnasio no técnico.
Firmar el ejecutable y el instalador con un certificado de una CA reconocida resuelve
(o atenúa) ese aviso.

El pipeline ya está listo en `scripts/pack.ps1`: firma automáticamente si hay un
certificado configurado por variable de entorno, y empaqueta sin firmar si no lo hay.

## Qué certificado comprar

| Opción | Costo aprox. | SmartScreen | Notas |
|--------|--------------|-------------|-------|
| **EV Code Signing** | USD 300–600/año | Reputación **inmediata** (sin aviso desde la primera instalación) | La clave vive en un token USB/HSM. Es lo que da mejor experiencia al cliente. |
| **OV Code Signing** | USD 150–300/año | La reputación se **construye con el tiempo** (el aviso puede seguir apareciendo al principio) | Se entrega como archivo `.pfx`. Más barato y simple de automatizar. |
| **Azure Trusted Signing** | ~USD 10/mes | Como OV/EV según validación | Servicio de Microsoft; requiere validación de la organización. Alternativa moderna y barata. |

CAs habituales: DigiCert, Sectigo/Comodo, GlobalSign, SSL.com. Para AR/LATAM cualquiera
sirve; se valida la identidad de la persona o la razón social (CUIT).

Recomendación: si el presupuesto lo permite, **EV** por la reputación inmediata. Si no,
**OV** (PFX) y la reputación se acumula a medida que se instala.

## Cómo firmar

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

- **Nunca** commitear el `.pfx` ni su password. Guardar el PFX fuera del repo y pasar la
  password por variable de entorno o un secret manager.
- El token EV no exporta la clave privada (por diseño): firmá desde la máquina que lo tiene
  conectado o desde un runner con el HSM.
