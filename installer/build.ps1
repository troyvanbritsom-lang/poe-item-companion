param([string]$Compiler)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $Compiler) {
    $installationFolders = @((Join-Path $env:LOCALAPPDATA 'Programs'), ${env:ProgramFiles(x86)}, $env:ProgramFiles)
    foreach ($folder in $installationFolders) {
        $candidate = Join-Path $folder 'Inno Setup 6\ISCC.exe'
        if (Test-Path -LiteralPath $candidate) { $Compiler = $candidate; break }
    }
}
if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw 'Install Inno Setup 6.7.3 or later from jrsoftware.org, or pass its ISCC.exe path with -Compiler.'
}
& (Join-Path $root 'build.bat')
if ($LASTEXITCODE -ne 0) { throw 'Companion build failed.' }

Add-Type -AssemblyName System.Drawing
$art = Join-Path $root 'bin\installer-assets'
$null = New-Item -ItemType Directory -Force $art
foreach ($spec in @(@{ Name = 'wizard'; Width = 328; Height = 628 }, @{ Name = 'sigil'; Width = 110; Height = 110 })) {
    $image = New-Object Drawing.Bitmap($spec.Width, $spec.Height)
    $g = [Drawing.Graphics]::FromImage($image)
    $brass = [Drawing.Color]::FromArgb(217, 180, 111)
    $ink = [Drawing.Color]::FromArgb(14, 16, 19)
    $g.Clear($ink)
    $g.SmoothingMode = 'AntiAlias'
    $pen = New-Object Drawing.Pen($brass, 2)
    $brush = New-Object Drawing.SolidBrush($brass)
    $font = New-Object Drawing.Font('Georgia', 21, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    try {
        $x = $spec.Width / 2
        $y = if ($spec.Name -eq 'wizard') { 256 } else { 55 }
        $g.DrawEllipse($pen, [single]($x - 37), [single]($y - 37), 74, 74)
        $g.DrawLines($pen, [Drawing.PointF[]]@(
            (New-Object Drawing.PointF($x, ($y - 31))), (New-Object Drawing.PointF(($x + 23), $y)),
            (New-Object Drawing.PointF($x, ($y + 31))), (New-Object Drawing.PointF(($x - 23), $y)),
            (New-Object Drawing.PointF($x, ($y - 31)))))
        if ($spec.Name -eq 'wizard') {
            $g.DrawRectangle($pen, 24, 24, 280, 580)
            $g.DrawLine($pen, 77, 322, 251, 322)
            $g.DrawString('C O M P A N I O N', $font, $brush, 58, 342)
        }
        $image.Save((Join-Path $art ($spec.Name + '.bmp')), [Drawing.Imaging.ImageFormat]::Bmp)
    } finally { $font.Dispose(); $brush.Dispose(); $pen.Dispose(); $g.Dispose(); $image.Dispose() }
}

& $Compiler (Join-Path $PSScriptRoot 'Companion.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
