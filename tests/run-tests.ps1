param([string]$OutputDirectory = 'dist\YandexDiskRescue')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$executable=Join-Path (Join-Path $root $OutputDirectory) 'YandexDiskRescue.exe'
if(-not(Test-Path -LiteralPath $executable)){& (Join-Path $root 'build.ps1')}
# Run the real executable so x86 checks execute in an x86 process.
$process=New-Object Diagnostics.Process
$process.StartInfo.FileName=$executable
$process.StartInfo.Arguments='--self-test'
$process.StartInfo.UseShellExecute=$false
$process.StartInfo.CreateNoWindow=$true
[void]$process.Start()
$process.WaitForExit()
Get-Content -LiteralPath (Join-Path (Split-Path -Parent $executable) 'self-test-results.txt') -Encoding UTF8
if($process.ExitCode -ne 0){throw 'Self tests failed.'}