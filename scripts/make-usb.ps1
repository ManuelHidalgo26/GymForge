# Arma el pendrive (o carpeta) de instalación para llevar al gimnasio.
# Copia el instalador, el certificado PUBLICO, el script que lo registra y el instructivo.
#
#   .\scripts\make-usb.ps1 -Destino E:\
#   .\scripts\make-usb.ps1 -Destino E:\ -IncluirPortable   -> agrega la version sin instalar
#
# Requiere haber corrido antes:
#   .\scripts\sign-selfsigned.ps1     (genera el certificado)
#   .\scripts\pack.ps1 -Version x.y.z (genera releases\GymForge-win-Setup.exe)
#
# NUNCA copia el .pfx: esa es la clave privada y va en un pendrive de respaldo aparte.
param(
    [Parameter(Mandatory = $true)][string]$Destino,
    [string]$ReleasesDir,
    [string]$CerPath = (Join-Path $env:LOCALAPPDATA 'GymForge\vendor\gymforge-code-signing.cer'),
    [switch]$IncluirPortable
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $ReleasesDir) { $ReleasesDir = Join-Path $root 'releases' }

if (-not (Test-Path $Destino)) { throw "No existe el destino '$Destino'. ¿Está conectado el pendrive?" }

$setup = Join-Path $ReleasesDir 'GymForge-win-Setup.exe'
if (-not (Test-Path $setup)) { throw "No está el instalador: $setup  (corré .\scripts\pack.ps1 primero)" }

# 1. Avisar si el instalador no quedó firmado: se puede llevar igual, pero el gimnasio
#    va a ver el cartel de "Editor desconocido" aunque registre el certificado.
$sig = Get-AuthenticodeSignature $setup
if ($sig.Status -eq 'NotSigned') {
    Write-Host "ATENCION: el instalador NO está firmado." -ForegroundColor Yellow
    Write-Host "  `$env:GYMFORGE_SIGN_THUMBPRINT = '<huella>'; .\scripts\pack.ps1 -Version x.y.z" -ForegroundColor Yellow
    Write-Host ""
}
else {
    Write-Host "Instalador firmado por: $($sig.SignerCertificate.Subject)" -ForegroundColor Cyan
}

# 2. Carpeta destino. Ojo: no llamarla $carpeta — PowerShell no distingue mayúsculas y
#    pisaría $Destino (la raíz del pendrive), que se usa más abajo.
$carpeta = Join-Path $Destino 'GymForge'
if (-not (Test-Path $carpeta)) { New-Item -ItemType Directory -Path $carpeta -Force | Out-Null }

# 3. Copiar. Solo material publico.
$archivos = @($setup)
if (Test-Path $CerPath) { $archivos += $CerPath }
else { Write-Host "No se encontró el certificado público ($CerPath): el pendrive va sin él." -ForegroundColor Yellow }
$archivos += (Join-Path $PSScriptRoot 'trust-cert.ps1')
$archivos += (Join-Path $PSScriptRoot 'LEEME-certificado.txt')
if ($IncluirPortable) {
    $portable = Join-Path $ReleasesDir 'GymForge-win-Portable.zip'
    if (Test-Path $portable) { $archivos += $portable }
    else { Write-Host "No está $portable, se omite." -ForegroundColor Yellow }
}

foreach ($a in $archivos) {
    Copy-Item $a -Destination $carpeta -Force
    Write-Host "  + $(Split-Path $a -Leaf)" -ForegroundColor DarkGray
}

# 4. Quitar la marca de "archivo bajado de internet" para que Windows no moleste de más.
Get-ChildItem $carpeta -File | Unblock-File

# 5. Huellas SHA256, para poder verificar que lo que se instala es lo que salió de acá.
$lineas = @("GymForge - SHA256 de los archivos ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))", "")
Get-ChildItem $carpeta -File | Where-Object Name -ne 'CHECKSUMS.txt' | ForEach-Object {
    $lineas += "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name
}
$lineas | Set-Content (Join-Path $carpeta 'CHECKSUMS.txt') -Encoding utf8

# 6. Red de seguridad: la clave privada jamás debe viajar en este pendrive.
$filtrados = Get-ChildItem $Destino -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.pfx', '.p12', '.key' }
if ($filtrados) {
    Write-Host ""
    Write-Host "PELIGRO: hay claves privadas en este pendrive. Borralas antes de llevarlo:" -ForegroundColor Red
    $filtrados | ForEach-Object { Write-Host "  $($_.FullName)" -ForegroundColor Red }
}

Write-Host ""
Write-Host "Pendrive listo: $carpeta" -ForegroundColor Green
Write-Host "En el gimnasio: abrir LEEME-certificado.txt y seguir los 3 pasos." -ForegroundColor Green
