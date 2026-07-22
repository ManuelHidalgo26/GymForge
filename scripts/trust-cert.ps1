# Instala el certificado publico de GymForge como raiz de confianza en ESTA PC.
# Se corre UNA sola vez en cada PC del gimnasio, ANTES de instalar GymForge.
# Despues de esto, el instalador y la app aparecen firmados por un editor verificado
# en lugar de "Editor desconocido".
#
# Requiere PowerShell como ADMINISTRADOR.
#
#   .\trust-cert.ps1                              -> busca el .cer al lado del script
#   .\trust-cert.ps1 -CerPath D:\gymforge.cer     -> ruta explicita
#   .\trust-cert.ps1 -Remove                      -> desinstala el certificado
param(
    [string]$CerPath = (Join-Path $PSScriptRoot 'gymforge-code-signing.cer'),
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Hay que ejecutar este script como administrador (clic derecho en PowerShell -> Ejecutar como administrador)."
}

if (-not (Test-Path $CerPath)) {
    # Fallback: el .cer que genera sign-selfsigned.ps1 en la maquina de desarrollo.
    $vendor = Join-Path $env:LOCALAPPDATA 'GymForge\vendor\gymforge-code-signing.cer'
    if (Test-Path $vendor) { $CerPath = $vendor }
    else { throw "No se encontro el certificado: $CerPath" }
}

$cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $CerPath
Write-Host "Certificado: $($cert.Subject)  huella $($cert.Thumbprint)" -ForegroundColor Cyan
Write-Host "Vence: $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Cyan

if ($Remove) {
    $found = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Thumbprint -eq $cert.Thumbprint
    if ($found) {
        $found | Remove-Item -Force
        Write-Host "Certificado desinstalado." -ForegroundColor Green
    }
    else { Write-Host "No estaba instalado." -ForegroundColor Yellow }
    return
}

Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Write-Host "Listo: esta PC ya confia en el editor de GymForge." -ForegroundColor Green
Write-Host "Ahora si, ejecutá GymForge-win-Setup.exe" -ForegroundColor Green
