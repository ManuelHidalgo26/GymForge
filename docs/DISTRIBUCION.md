# Distribución: pendrive y descarga web

Dos caminos para que GymForge llegue a la PC del gimnasio. El pendrive es el que se usa en
el piloto (vamos nosotros a instalar); la descarga web sirve para actualizaciones y para
gimnasios que instalan solos.

---

## Los dos pendrives (no confundirlos)

| | Qué lleva | Dónde vive |
|---|---|---|
| **Pendrive de respaldo** | `gymforge-code-signing.pfx` — la clave privada, permite firmar software como "GymForge" | Guardado. Nunca sale, nunca se presta. |
| **Pendrive de instalación** | Instalador + `.cer` (parte pública) + scripts | Va al gimnasio. Si se pierde, no pasa nada. |

`make-usb.ps1` nunca copia el `.pfx` y, además, avisa en rojo si encuentra uno en el
pendrive que se está armando.

---

## 1. Respaldo de la clave privada (una sola vez)

Con el pendrive de respaldo conectado (supongamos que es `E:`):

```powershell
.\scripts\sign-selfsigned.ps1 -PfxPath E:\gymforge-code-signing.pfx
```

Pide una password dos veces (mínimo 8 caracteres) y escribe el archivo directo en el
pendrive. **Anotá esa password en papel o en un gestor de contraseñas**: sin ella el
respaldo no sirve y no hay forma de recuperarla.

Para tener una segunda copia, repetir el comando con otra ruta (otro pendrive, un disco
externo). Lo que **no** hay que hacer es subirlo a Drive/OneDrive/el repo.

### Si se pierde la clave privada

No es una catástrofe, pero es molesto: hay que generar un certificado nuevo
(`sign-selfsigned.ps1 -Force`), volver a firmar, y **reinstalar el `.cer` nuevo en cada PC**
que ya tenía el viejo. Por eso conviene el respaldo.

---

## 2. Pendrive de instalación

```powershell
# 1. Empaquetar firmado
$env:GYMFORGE_SIGN_THUMBPRINT = '<huella que imprime sign-selfsigned.ps1>'
.\scripts\pack.ps1 -Version 0.3.0

# 2. Volcarlo al pendrive (F: en este ejemplo)
.\scripts\make-usb.ps1 -Destino F:\
```

Queda una carpeta `F:\GymForge` con:

- `GymForge-win-Setup.exe` — el instalador
- `gymforge-code-signing.cer` — el certificado público
- `trust-cert.ps1` — lo registra en la PC del gimnasio
- `LEEME-certificado.txt` — los 3 pasos, en criollo, para seguir en el mostrador
- `CHECKSUMS.txt` — SHA256 de todo, para verificar que no se corrompió nada

Con `-IncluirPortable` también copia `GymForge-win-Portable.zip` (la versión que corre sin
instalar, útil para probar en una PC prestada sin tocarla).

En el gimnasio: PowerShell como administrador → `.\trust-cert.ps1` → doble clic en el Setup.

---

## 3. Descarga web (GitHub Releases)

Los mismos archivos, publicados en el repo. Sirve para reinstalar sin viajar y para mandarle
el link a un gimnasio.

```powershell
vpk upload github --repoUrl https://github.com/ManuelHidalgo26/GymForge --publish --releaseName "GymForge 0.3.0" --tag v0.3.0
```

Necesita `GITHUB_TOKEN` con permiso sobre el repo. Subí también el `.cer` y el
`trust-cert.ps1` como assets del release: son públicos y sin ellos el que descarga no puede
sacarse el cartel de "Editor desconocido".

Aclaración honesta sobre la descarga web: al bajar el instalador del navegador, Windows le
pone la marca de "archivo de internet" y **SmartScreen puede avisar igual aunque el usuario
haya registrado el certificado** — su reputación es un servicio en la nube de Microsoft que
no conoce nuestro certificado autofirmado. Se destraba con "Más información → Ejecutar de
todas formas", o antes de ejecutar:

```powershell
Unblock-File .\GymForge-win-Setup.exe
```

Para que ese aviso desaparezca del todo en PCs ajenas hace falta un certificado de una CA
reconocida (ver [CODE-SIGNING.md](CODE-SIGNING.md)).

---

## Actualizaciones

`pack.ps1` genera los paquetes de Velopack (`.nupkg` + `RELEASES`) junto al instalador, así
que el release queda listo para auto-actualización. Hoy la app solo corre
`VelopackApp.Build().Run()` (hooks de instalación): **todavía no busca actualizaciones sola**.
Hasta que se agregue el `UpdateManager`, actualizar = correr el Setup nuevo encima, que
respeta la base de datos en `%LOCALAPPDATA%\GymForge`.
