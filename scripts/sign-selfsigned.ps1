# Certificado de firma de código AUTOFIRMADO (gratis) para GymForge.
#
# Sirve para el piloto y para gimnasios donde nosotros instalamos: en las PCs donde se
# instala el certificado (ver scripts\trust-cert.ps1), GymForge queda firmado por un
# "editor verificado" y desaparece el aviso de editor desconocido. En una PC ajena que
# NO tiene el certificado, el aviso sigue apareciendo: para eso hace falta un certificado
# de una CA reconocida (ver docs\CODE-SIGNING.md).
#
#   .\scripts\sign-selfsigned.ps1                     -> crea (o reusa) el cert y exporta el .cer
#   .\scripts\sign-selfsigned.ps1 -BackupPfx          -> además exporta un .pfx de respaldo
#   .\scripts\sign-selfsigned.ps1 -Trust              -> confía el cert en ESTA PC (requiere admin)
#   .\scripts\sign-selfsigned.ps1 -SignFile dist\GymForge.exe   -> firma un exe suelto
#   .\scripts\sign-selfsigned.ps1 -Force              -> genera uno nuevo aunque ya exista
#
# El certificado (con su clave privada) vive en el store del usuario actual y se respalda
# en %LOCALAPPDATA%\GymForge\vendor — igual que la clave de licenciamiento. NO va al repo.
param(
    [string]$Publisher = 'GymForge',
    [int]   $Years     = 5,
    [string]$OutDir    = (Join-Path $env:LOCALAPPDATA 'GymForge\vendor'),
    [switch]$BackupPfx,
    [string]$PfxPath,
    [switch]$Trust,
    [switch]$Force,
    [string]$SignFile,
    [string]$Timestamp = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$subject = "CN=$Publisher"

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 1. Buscar un certificado de firma de código válido que ya tengamos.
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $subject -and
        $_.NotAfter -gt (Get-Date) -and
        $_.HasPrivateKey -and
        ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($cert -and -not $Force) {
    Write-Host "Certificado existente: $($cert.Thumbprint) (vence $($cert.NotAfter.ToString('yyyy-MM-dd')))" -ForegroundColor Cyan
}
else {
    if ($cert -and $Force) {
        Write-Host "-Force: se genera un certificado nuevo (el anterior queda en el store)." -ForegroundColor Yellow
        Write-Host "OJO: las PCs que ya confiaban en el anterior necesitan instalar el .cer nuevo." -ForegroundColor Yellow
    }
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -FriendlyName "$Publisher - firma de codigo" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddYears($Years)
    Write-Host "Certificado creado: $($cert.Thumbprint) (vence $($cert.NotAfter.ToString('yyyy-MM-dd')))" -ForegroundColor Green
}

# 2. Exportar la parte pública (.cer): esto es lo que se instala en la PC del gimnasio.
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$cerPath = Join-Path $OutDir 'gymforge-code-signing.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Certificado publico (para distribuir): $cerPath" -ForegroundColor Green

# 3. Respaldo con clave privada (.pfx). Si se pierde, hay que reconfigurar todas las PCs.
#    OJO: este archivo es la llave para firmar como "GymForge". Va a un pendrive guardado,
#    NUNCA al pendrive de instalación que se lleva al gimnasio.
if ($BackupPfx -or $PfxPath) {
    $pfx = if ($PfxPath) { $PfxPath } else { Join-Path $OutDir 'gymforge-code-signing.pfx' }
    $dir = Split-Path $pfx -Parent
    if ($dir -and -not (Test-Path $dir)) { throw "No existe la carpeta destino: $dir (¿está conectado el pendrive?)" }

    Write-Host ""
    Write-Host "Elegí una password para el respaldo y anotala en un lugar seguro." -ForegroundColor Cyan
    Write-Host "Sin esa password el respaldo no sirve, y no hay forma de recuperarla." -ForegroundColor Cyan
    $pass  = Read-Host "Password para el .pfx" -AsSecureString
    $pass2 = Read-Host "Repetir password"      -AsSecureString
    $p1 = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pass))
    $p2 = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($pass2))
    if ($p1 -ne $p2)    { throw "Las passwords no coinciden. No se generó el respaldo." }
    if ($p1.Length -lt 8) { throw "Usá al menos 8 caracteres. No se generó el respaldo." }
    Remove-Variable p1, p2

    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pass -Force | Out-Null
    Write-Host "Respaldo con clave privada: $pfx" -ForegroundColor Green
    Write-Host "Guardalo fuera de esta PC. NO lo copies al pendrive de instalación." -ForegroundColor Yellow
}

# 4. Confiar el certificado en esta misma PC (opcional, para probar el resultado real).
if ($Trust) {
    if (-not (Test-Admin)) {
        Write-Host "-Trust requiere PowerShell como administrador. Salteado." -ForegroundColor Yellow
    }
    else {
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Write-Host "Certificado instalado como raiz de confianza en esta PC." -ForegroundColor Green
    }
}

# 5. Firmar un archivo suelto (no necesita el Windows SDK: usa Authenticode de PowerShell).
if ($SignFile) {
    if (-not (Test-Path $SignFile)) { throw "No existe el archivo a firmar: $SignFile" }
    $sigArgs = @{ FilePath = $SignFile; Certificate = $cert; HashAlgorithm = 'SHA256' }
    if ($Timestamp -and $Timestamp -ne 'none') { $sigArgs.TimestampServer = $Timestamp }
    $sig = Set-AuthenticodeSignature @sigArgs
    switch ($sig.Status) {
        'Valid' {
            Write-Host "Firmado y verificado: $SignFile" -ForegroundColor Green
        }
        'UnknownError' {
            # Raiz no confiable: esperado mientras esta PC no tenga instalado el .cer.
            Write-Host "Firmado: $SignFile" -ForegroundColor Green
            Write-Host "(figura como 'editor no verificado' hasta instalar el certificado con trust-cert.ps1)" -ForegroundColor DarkGray
        }
        default {
            Write-Host "Firma con estado '$($sig.Status)': $($sig.StatusMessage)" -ForegroundColor Yellow
        }
    }
}

# 6. Cómo usarlo con el empaquetado.
Write-Host ""
Write-Host "Para que pack.ps1 firme el instalador con este certificado:" -ForegroundColor Cyan
Write-Host "  `$env:GYMFORGE_SIGN_THUMBPRINT = '$($cert.Thumbprint)'" -ForegroundColor White
Write-Host "  .\scripts\pack.ps1 -Version 0.3.0" -ForegroundColor White
Write-Host ""
Write-Host "En la PC del gimnasio, antes de instalar:" -ForegroundColor Cyan
Write-Host "  .\scripts\trust-cert.ps1 -CerPath gymforge-code-signing.cer   (como administrador)" -ForegroundColor White
