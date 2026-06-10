# Publica GymForge como UN solo ejecutable autocontenido para Windows x64.
# No requiere .NET instalado en la máquina destino: doble click y arranca.
#   .\scripts\publish.ps1
# Salida: dist\GymForge.Desktop.exe

$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root 'dist'

dotnet publish (Join-Path $root 'src\GymForge.Desktop') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $out

if ($LASTEXITCODE -eq 0) {
    $exe = Join-Path $out 'GymForge.Desktop.exe'
    $mb  = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Listo: $exe ($mb MB)" -ForegroundColor Green
    Write-Host "Ese .exe es todo lo que hay que distribuir." -ForegroundColor Green
}
