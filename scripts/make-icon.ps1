# Genera src/GymForge.Desktop/Assets/gymforge.ico (multi-resolución, PNG-comprimido)
# Diseño: mancuerna blanca sobre cuadrado redondeado violeta de marca (#6366F1).
# Reproducible: correrlo regenera el ícono desde cero. Requiere Windows (GDI+).

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $repoRoot 'src\GymForge.Desktop\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size

    # Fondo: cuadrado redondeado con degradé violeta (marca #6366F1)
    $margin = $s * 0.02
    $bgRect = New-RoundedRectPath $margin $margin ($s - 2 * $margin) ($s - 2 * $margin) ($s * 0.22)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF(0, $s)),
        [System.Drawing.Color]::FromArgb(255, 0x7B, 0x7E, 0xF7),
        [System.Drawing.Color]::FromArgb(255, 0x53, 0x56, 0xDE))
    $g.FillPath($grad, $bgRect)

    # Mancuerna blanca centrada: barra + platos internos (altos) + externos (cortos)
    $white = [System.Drawing.Brushes]::White
    $cy = $s / 2

    function FillBar([float]$cx, [float]$w, [float]$h, [float]$r) {
        $p = New-RoundedRectPath ($cx - $w / 2) ($cy - $h / 2) $w $h $r
        $g.FillPath($white, $p)
        $p.Dispose()
    }

    FillBar ($s * 0.5) ($s * 0.56) ($s * 0.085) ($s * 0.04)   # barra
    FillBar ($s * 0.315) ($s * 0.105) ($s * 0.44) ($s * 0.05) # plato interno izq
    FillBar ($s * 0.685) ($s * 0.105) ($s * 0.44) ($s * 0.05) # plato interno der
    FillBar ($s * 0.19) ($s * 0.095) ($s * 0.30) ($s * 0.045) # plato externo izq
    FillBar ($s * 0.81) ($s * 0.095) ($s * 0.30) ($s * 0.045) # plato externo der

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    # La coma evita que PowerShell desenrolle el byte[] en el pipeline
    return , $ms.ToArray()
}

# ── Render en todas las resoluciones ─────────────────────────────────────────
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @{}
foreach ($sz in $sizes) { $images[$sz] = Render-IconPng $sz }

# Preview para revisión visual
[System.IO.File]::WriteAllBytes((Join-Path $assets 'gymforge-icon-preview.png'), $images[256])

# ── Ensamblar el .ico (entradas PNG, válidas desde Windows Vista) ────────────
$icoPath = Join-Path $assets 'gymforge.ico'
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([uint16]0)               # reservado
$bw.Write([uint16]1)               # tipo: ícono
$bw.Write([uint16]$sizes.Count)    # cantidad de imágenes

$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $png = $images[$sz]
    if ($sz -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$sz) }  # ancho (0 = 256)
    if ($sz -ge 256) { $bw.Write([byte]0) } else { $bw.Write([byte]$sz) }  # alto
    $bw.Write([byte]0)             # paleta
    $bw.Write([byte]0)             # reservado
    $bw.Write([uint16]1)           # planos
    $bw.Write([uint16]32)          # bits por pixel
    $bw.Write([uint32]$png.Length) # tamaño del blob
    $bw.Write([uint32]$offset)     # offset del blob
    $offset += $png.Length
}
foreach ($sz in $sizes) { $bw.Write([byte[]]$images[$sz]) }

[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()

Write-Host "Listo: $icoPath ($([math]::Round((Get-Item $icoPath).Length / 1KB, 1)) KB, $($sizes.Count) resoluciones)"
