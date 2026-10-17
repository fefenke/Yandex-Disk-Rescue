param(
    [Parameter(Mandatory=$true)][string]$TestClass,
    [string]$Method = 'Run',
    [string]$OutputDirectory = 'dist/verify-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path (Join-Path $root $OutputDirectory) 'YandexDiskRescue.exe'
$assembly = [Reflection.Assembly]::LoadFrom($exe)
$type = $assembly.GetType('DiskRescue.' + $TestClass, $true)
$test = $type.GetMethod($Method, [Reflection.BindingFlags]'Public,NonPublic,Static')
if ($null -eq $test) { throw "Test method missing: $TestClass.$Method" }
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('ydr-focused-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($sandbox)
try {
    $arguments = New-Object 'object[]' 1
    $arguments[0] = $sandbox.PSObject.BaseObject
    $null = $test.Invoke($null, $arguments)
    Write-Output "PASS: $TestClass.$Method"
} catch {
    $cause = $_.Exception
    while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
    throw $cause
}
