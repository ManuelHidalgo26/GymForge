# Corre la app de escritorio GymForge (la real, no el mockup).
#   .\scripts\run.ps1            -> arranca la app
#   .\scripts\run.ps1 -Reset     -> borra la base local y arranca de cero (seed nuevo)
param([switch]$Reset)

if ($Reset) {
    $db = Join-Path $env:LOCALAPPDATA 'GymForge'
    if (Test-Path $db) {
        Get-ChildItem "$db\gymforge.db*" -ErrorAction SilentlyContinue | Remove-Item -Force
        Write-Host "Base local borrada: se regenera con datos de ejemplo (PIN admin: 1234)." -ForegroundColor Yellow
    }
}

dotnet run --project (Join-Path $PSScriptRoot '..\src\GymForge.Desktop')
