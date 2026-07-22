# Emisión de licencias de GymForge (uso interno del vendedor).
#
#   .\scripts\licencia.ps1              -> asistente: pregunta gimnasio, plan, CUIT y meses
#   .\scripts\licencia.ps1 list         -> licencias emitidas, con vencimiento y estado
#   .\scripts\licencia.ps1 renew        -> renovar a un cliente del registro
#   .\scripts\licencia.ps1 show GYMF... -> verificar una clave que reporta un cliente
#
# La clave privada que firma vive en %LOCALAPPDATA%\GymForge\vendor\ y NO está en el repo.
# El registro de lo emitido queda en esa misma carpeta (licencias-emitidas.csv).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet run --project (Join-Path $root 'tools\GymForge.LicenseGen') -v q --property WarningLevel=0 -- @args
