param([string]$OutputDirectory = 'dist\YandexDiskRescue')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$executable = Join-Path (Join-Path $root $OutputDirectory) 'YandexDiskRescue.exe'
if (-not (Test-Path -LiteralPath $executable)) { & (Join-Path $root 'build.ps1') }
# Invoke the test suite directly, without starting a GUI or touching cloud files.
$assembly = [Reflection.Assembly]::LoadFrom($executable)
$test = $assembly.GetType('DiskRescue.Tests').GetMethod('Run',[Reflection.BindingFlags]'Public,Static')
$code = $test.Invoke($null,@())
$result = Join-Path (Split-Path -Parent $executable) 'self-test-results.txt'
Get-Content -LiteralPath $result -Encoding UTF8
if ($code -ne 0) { throw 'Self tests failed.' }
