# Publica GymForge como UN solo ejecutable autocontenido para Windows x64.
# No requiere .NET instalado en la máquina destino: doble click y arranca.
#   .\scripts\publish.ps1
# Salida: dist\GymForge.exe
#
# Si $env:GYMFORGE_SIGN_THUMBPRINT apunta a un certificado de firma de código, el exe se
# firma al final (ver scripts\sign-selfsigned.ps1 y docs\CODE-SIGNING.md).

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
    # El exe viejo (GymForge.Desktop.exe) quedaría duplicado: limpiarlo.
    Remove-Item (Join-Path $out 'GymForge.Desktop.exe') -ErrorAction SilentlyContinue
    $exe = Join-Path $out 'GymForge.exe'

    # Firma de código (opcional): evita el aviso de "editor desconocido".
    if ($env:GYMFORGE_SIGN_THUMBPRINT) {
        $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My |
            Where-Object Thumbprint -eq $env:GYMFORGE_SIGN_THUMBPRINT | Select-Object -First 1
        if ($cert) {
            $ts = if ($env:GYMFORGE_SIGN_TIMESTAMP) { $env:GYMFORGE_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
            $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
                -HashAlgorithm SHA256 -TimestampServer $ts
            Write-Host "Firmado con $($cert.Subject) [$($sig.Status)]" -ForegroundColor Cyan
        }
        else {
            Write-Host "No se encontro el certificado $($env:GYMFORGE_SIGN_THUMBPRINT): se publica SIN firmar." -ForegroundColor Yellow
        }
    }

    $mb  = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Listo: $exe ($mb MB)" -ForegroundColor Green
    Write-Host "Ese .exe es todo lo que hay que distribuir." -ForegroundColor Green
}
