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

# En dev sembramos el gimnasio de demostración (PIN admin 1234) para no pasar por el
# onboarding en cada arranque. En producción la env var no está y aparece el asistente.
$env:GYMFORGE_SEED_COMPANY = '1'

dotnet run --project (Join-Path $PSScriptRoot '..\src\GymForge.Desktop')
