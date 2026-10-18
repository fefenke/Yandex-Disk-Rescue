param(
    [string]$OutputDirectory = 'dist\YandexDiskRescue',
    [ValidateSet('x64','x86')][string]$Platform = 'x64',
    [ValidateRange(1,900)][int]$TimeoutSeconds = 900
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$output=if([IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory}else{Join-Path $root $OutputDirectory}
$executable=Join-Path $output 'YandexDiskRescue.exe'
if(-not(Test-Path -LiteralPath $executable)){& (Join-Path $root 'build.ps1') -OutputDirectory $output -Platform $Platform}
$report=Join-Path $output 'self-test-results.txt'
# A previous successful report must not substitute for a missing result from this run.
if(Test-Path -LiteralPath $report){Remove-Item -LiteralPath $report}
# Run the real executable so x86 checks execute in an x86 process.
$process=New-Object Diagnostics.Process
$process.StartInfo.FileName=$executable
$process.StartInfo.Arguments='--self-test'
$process.StartInfo.UseShellExecute=$false
$process.StartInfo.CreateNoWindow=$true
try{
    [void]$process.Start()
    if(-not $process.WaitForExit($TimeoutSeconds*1000)){
        # Kill only the exact process started above, never other app instances.
        try{if(-not $process.HasExited){$process.Kill();[void]$process.WaitForExit(5000)}}catch [InvalidOperationException]{}
        throw ('Self tests timed out after '+$TimeoutSeconds+' seconds. Owned test process: '+$process.Id+'.')
    }
    if(-not(Test-Path -LiteralPath $report)){throw 'Self tests did not produce a result report.'}
    Get-Content -LiteralPath $report -Encoding UTF8
    if($process.ExitCode -ne 0){throw 'Self tests failed.'}
}finally{$process.Dispose()}
