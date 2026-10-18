param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('ydr-runner-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$source=Join-Path $fixture 'Fixture.cs'
$exe=Join-Path $fixture 'YandexDiskRescue.exe'
$code=@'
using System;
using System.IO;
using System.Threading;
class Fixture {
    static int Main(string[] args) {
        string root=AppDomain.CurrentDomain.BaseDirectory;
        if(args.Length>0 && args[0]=="--unrelated"){Thread.Sleep(30000);return 0;}
        File.WriteAllText(Path.Combine(root,"fixture.pid"),System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        string mode=File.ReadAllText(Path.Combine(root,"mode.txt"));
        if(mode=="hang"){Thread.Sleep(30000);return 0;}
        if(mode=="missing")return 0;
        File.WriteAllText(Path.Combine(root,"self-test-results.txt"),mode=="fail"?"FAIL: fixture":"PASS: fixture");
        return mode=="fail"?1:0;
    }
}
'@
[IO.File]::WriteAllText($source,$code)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe ('/out:'+$exe) $source
if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed.'}
function Invoke-Runner([string]$script,[string]$directory){
    $child=New-Object Diagnostics.Process
    $child.StartInfo.FileName='powershell.exe'
    $child.StartInfo.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$script+'" -OutputDirectory "'+$directory+'" -TimeoutSeconds 1 -Platform x86'
    $child.StartInfo.UseShellExecute=$false
    $child.StartInfo.CreateNoWindow=$true
    $child.StartInfo.RedirectStandardOutput=$true
    $child.StartInfo.RedirectStandardError=$true
    try{
        [void]$child.Start()
        if(-not $child.WaitForExit(12000)){$child.Kill();throw 'Runner fixture exceeded its own safety deadline.'}
        return @{Code=$child.ExitCode;Text=$child.StandardOutput.ReadToEnd()+$child.StandardError.ReadToEnd()}
    }finally{$child.Dispose()}
}
$unrelated=$null
try{
    $runner=Join-Path $PSScriptRoot 'run-tests.ps1'
    [IO.File]::WriteAllText((Join-Path $fixture 'mode.txt'),'hang')
    $unrelated=Start-Process -FilePath $exe -ArgumentList '--unrelated' -WindowStyle Hidden -PassThru
    $result=Invoke-Runner $runner $fixture
    if($result.Code -eq 0 -or $result.Text -notmatch 'timed out'){throw ('Missing distinct timeout failure: '+$result.Text)}
    if($unrelated.HasExited){throw 'Timeout terminated an unrelated process.'}
    $owned=[int][IO.File]::ReadAllText((Join-Path $fixture 'fixture.pid'))
    try{$stillRunning=[Diagnostics.Process]::GetProcessById($owned);$stillRunning.Dispose();throw 'Owned timed-out fixture is still running.'}catch [ArgumentException]{}
    foreach($mode in 'pass','fail','missing'){
        [IO.File]::WriteAllText((Join-Path $fixture 'mode.txt'),$mode)
        [IO.File]::WriteAllText((Join-Path $fixture 'self-test-results.txt'),'PASS: stale fixture result')
        $result=Invoke-Runner $runner $fixture
        if($mode -eq 'pass' -and ($result.Code -ne 0 -or $result.Text -notmatch 'PASS: fixture')){throw ('Successful fixture rejected: '+$result.Text)}
        if($mode -eq 'fail' -and ($result.Code -eq 0 -or $result.Text -notmatch 'FAIL: fixture')){throw ('Failed fixture accepted: '+$result.Text)}
        if($mode -eq 'missing' -and ($result.Code -eq 0 -or $result.Text -notmatch 'did not produce')){throw ('Stale result accepted: '+$result.Text)}
    }
    # A tiny fake source checkout checks build parameter forwarding without rebuilding the app.
    $checkout=Join-Path $fixture 'checkout';$tests=Join-Path $checkout 'tests';[void][IO.Directory]::CreateDirectory($tests)
    Copy-Item -LiteralPath $runner -Destination (Join-Path $tests 'run-tests.ps1')
    Copy-Item -LiteralPath $exe -Destination (Join-Path $checkout 'fixture.exe')
    $build=@'
param([string]$OutputDirectory,[string]$Platform)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixture.exe') -Destination (Join-Path $OutputDirectory 'YandexDiskRescue.exe')
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'mode.txt'),'pass')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build-arguments.txt'),$Platform+'|'+$OutputDirectory)
'@
    [IO.File]::WriteAllText((Join-Path $checkout 'build.ps1'),$build)
    $destination=Join-Path $checkout 'output with spaces'
    $result=Invoke-Runner (Join-Path $tests 'run-tests.ps1') $destination
    if($result.Code -ne 0 -or [IO.File]::ReadAllText((Join-Path $checkout 'build-arguments.txt')) -ne ('x86|'+$destination)){throw ('Build arguments were lost: '+$result.Text)}
    'PASS: runner timeout, owned-process cancellation, unrelated-process survival, success/failure, stale report rejection and build arguments.'
}finally{
    if($null -ne $unrelated){if(-not $unrelated.HasExited){$unrelated.Kill();[void]$unrelated.WaitForExit(5000)};$unrelated.Dispose()}
    # Keep fixtures for inspection; no process-by-name or recursive deletion is used.
}
