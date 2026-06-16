# Empaqueta GymForge como instalador de Windows con Velopack: genera un Setup.exe
# (instala con acceso directo + menú inicio) y los paquetes para auto-actualización.
#   .\scripts\pack.ps1                 -> usa la versión del .csproj
#   .\scripts\pack.ps1 -Version 0.3.0  -> versión explícita
# Salida: releases\  (GymForge-win-Setup.exe + *.nupkg + RELEASES, listos para GitHub Releases).
#
# Requiere la tool de Velopack:  dotnet tool install -g vpk
# La firma de código (para evitar el aviso de SmartScreen) se agrega con
# --signParams cuando tengas un certificado; sin eso el instalador funciona igual.
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

# 2. Empaquetar con Velopack.
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
vpk pack `
    --packId GymForge `
    --packVersion $Version `
    --packDir $pub `
    --mainExe GymForge.exe `
    --packTitle "GymForge" `
    --icon (Join-Path $root 'src\GymForge.Desktop\Assets\gymforge.ico') `
    --outputDir $rel
if ($LASTEXITCODE -ne 0) { throw "vpk pack falló" }

Write-Host ""
Write-Host "Listo. Instalador en: $(Join-Path $rel 'GymForge-win-Setup.exe')" -ForegroundColor Green
Write-Host "Para publicar a GitHub Releases: vpk upload github --repoUrl https://github.com/ManuelHidalgo26/GymForge" -ForegroundColor Green
