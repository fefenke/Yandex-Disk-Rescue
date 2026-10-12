param([switch]$Package, [string]$OutputDirectory = 'dist\YandexDiskRescue')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.7.1 or later, then run build.ps1 again.' }
$output = $OutputDirectory
if(-not [IO.Path]::IsPathRooted($output)) { $output = Join-Path $root $output }
[void][IO.Directory]::CreateDirectory($output)
# Original icon: a cloud and download arrow, with no Yandex brand artwork.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object Drawing.Bitmap(64,64)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::FromArgb(89,101,233))
$white = New-Object Drawing.SolidBrush([Drawing.Color]::White)
$graphics.FillEllipse($white,12,21,24,22)
$graphics.FillEllipse($white,25,13,25,29)
$graphics.FillEllipse($white,40,24,15,18)
$graphics.FillRectangle($white,22,28,28,15)
$pen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(89,101,233),5)
$graphics.DrawLine($pen,33,27,33,47)
$graphics.DrawLine($pen,24,38,33,47)
$graphics.DrawLine($pen,42,38,33,47)
$icon = [Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconPath = Join-Path $root 'assets\app.ico'
$stream = [IO.File]::Create($iconPath)
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $pen.Dispose(); $white.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
$executable = Join-Path $output 'YandexDiskRescue.exe'
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + $executable),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/win32icon:' + $iconPath),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'App.config') -Destination ($executable + '.config')
foreach ($name in 'README.md','README.en.md','LICENSE','Start Yandex Disk Rescue.cmd') {
    if (Test-Path -LiteralPath (Join-Path $root $name)) { Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $output $name) }
}
$screenshots = Join-Path $output 'assets'
[void][IO.Directory]::CreateDirectory($screenshots)
foreach ($name in 'preview.png','preview-en.png') {
    if(Test-Path -LiteralPath (Join-Path $root ('assets\'+$name))) { Copy-Item -LiteralPath (Join-Path $root ('assets\'+$name)) -Destination (Join-Path $screenshots $name) }
}
if ($Package) {
    $zip=Join-Path $root 'dist\YandexDiskRescue-win-x64.zip'
    $packageFiles=@($executable,($executable+'.config'))
    foreach($name in 'README.md','README.en.md','LICENSE','Start Yandex Disk Rescue.cmd') { if(Test-Path (Join-Path $output $name)) { $packageFiles += Join-Path $output $name } }
    $packageFiles += $screenshots
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $zip -Force
    Write-Output $zip
}
Write-Output $executable
