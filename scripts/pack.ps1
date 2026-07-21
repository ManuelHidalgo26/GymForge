# Empaqueta GymForge como instalador de Windows con Velopack: genera un Setup.exe
# (instala con acceso directo + menú inicio) y los paquetes para auto-actualización.
#   .\scripts\pack.ps1                 -> usa la versión del .csproj
#   .\scripts\pack.ps1 -Version 0.3.0  -> versión explícita
# Salida: releases\  (GymForge-win-Setup.exe + *.nupkg + RELEASES, listos para GitHub Releases).
#
# Requiere la tool de Velopack:  dotnet tool install -g vpk
#
# Firma de código (evita el aviso de SmartScreen "editor desconocido"). Opt-in por
# env var; sin certificado empaqueta sin firmar (dev) y el instalador funciona igual.
# Ver docs/CODE-SIGNING.md. Configuración:
#   Certificado en el store de Windows (EV/token en HSM):
#     $env:GYMFORGE_SIGN_THUMBPRINT = '<huella SHA1 del cert>'
#   Archivo PFX (OV en software):
#     $env:GYMFORGE_SIGN_PFX      = 'C:\ruta\gymforge.pfx'
#     $env:GYMFORGE_SIGN_PFX_PASS = '<password>'
#   Opcional: $env:GYMFORGE_SIGN_TIMESTAMP (default http://timestamp.digicert.com)
param([string]$Version = "0.2.0")

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pub  = Join-Path $root 'publish'
$rel  = Join-Path $root 'releases'

# 1. Publish self-contained SIN single-file (Velopack empaqueta el directorio, no un exe único).
dotnet publish (Join-Path $root 'src\GymForge.Desktop') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $pub
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló" }

# 2. Firma de código (opcional): arma los parámetros de signtool según la env var.
#    Velopack firma el exe de la app y el Setup.exe con estos parámetros.
$timestamp = if ($env:GYMFORGE_SIGN_TIMESTAMP) { $env:GYMFORGE_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
$signParams = $null
if ($env:GYMFORGE_SIGN_THUMBPRINT) {
    $signParams = "/fd sha256 /sha1 $($env:GYMFORGE_SIGN_THUMBPRINT) /tr $timestamp /td sha256"
    Write-Host "Firma: certificado del store (thumbprint)." -ForegroundColor Cyan
}
elseif ($env:GYMFORGE_SIGN_PFX) {
    if (-not (Test-Path $env:GYMFORGE_SIGN_PFX)) { throw "No existe el PFX: $($env:GYMFORGE_SIGN_PFX)" }
    $signParams = "/fd sha256 /f `"$($env:GYMFORGE_SIGN_PFX)`" /p `"$($env:GYMFORGE_SIGN_PFX_PASS)`" /tr $timestamp /td sha256"
    Write-Host "Firma: certificado PFX." -ForegroundColor Cyan
}
else {
    Write-Host "Sin certificado (GYMFORGE_SIGN_PFX / GYMFORGE_SIGN_THUMBPRINT): se empaqueta SIN firmar." -ForegroundColor Yellow
}

# 3. Empaquetar con Velopack.
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
$vpkArgs = @(
    'pack',
    '--packId', 'GymForge',
    '--packVersion', $Version,
    '--packDir', $pub,
    '--mainExe', 'GymForge.exe',
    '--packTitle', 'GymForge',
    '--icon', (Join-Path $root 'src\GymForge.Desktop\Assets\gymforge.ico'),
    '--outputDir', $rel
)
if ($signParams) { $vpkArgs += @('--signParams', $signParams) }

vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack falló" }

Write-Host ""
Write-Host "Listo. Instalador en: $(Join-Path $rel 'GymForge-win-Setup.exe')" -ForegroundColor Green
Write-Host "Para publicar a GitHub Releases: vpk upload github --repoUrl https://github.com/ManuelHidalgo26/GymForge" -ForegroundColor Green
