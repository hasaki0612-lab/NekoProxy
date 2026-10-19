param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$CoreRoot,
    [string]$Python = 'python',
    [string]$Dotnet = 'dotnet',
    [string]$BuildDirectory,
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a new evidence directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$env:NEKOPROXY_CORE_ROOT=[IO.Path]::GetFullPath($CoreRoot)
$env:NEKOPROXY_REPOSITORY_ROOT=$repo
$env:PYTHONDONTWRITEBYTECODE='1'
if($IsWindows){
    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
    $passed=0
    foreach($i in 1..24){
        $plain=[Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        $opened=$null
        try{
            $cipher=[Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
            $opened=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
            if([Convert]::ToBase64String($plain) -cne [Convert]::ToBase64String($opened)){throw 'CurrentUser DPAPI round trip mismatch'}
            $passed++
        }finally{[Array]::Clear($plain);if($opened){[Array]::Clear($opened)}}
    }
    @{CurrentUserDpapi=$true;RoundTrips=$passed;TimeUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'dpapi.json')
}
$fixtureRoot=Join-Path ([IO.Path]::GetTempPath()) ('NekoProxy-CI-ssh-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$start=[Diagnostics.ProcessStartInfo]::new($Python)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.ArgumentList.Add((Join-Path $repo 'tests/fixtures/ssh_server.py'));$start.ArgumentList.Add($fixtureRoot)
$fixture=[Diagnostics.Process]::Start($start)
$stdout=$fixture.StandardOutput.ReadToEndAsync();$stderr=$fixture.StandardError.ReadToEndAsync()
$env:NEKOPROXY_SSH_FIXTURE=Join-Path $fixtureRoot 'connection.json'
Push-Location $repo
try{
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while(-not(Test-Path -LiteralPath $env:NEKOPROXY_SSH_FIXTURE)){
        if($fixture.HasExited -or [DateTime]::UtcNow -gt $deadline){throw 'Localhost SSH fixture unavailable'}
        Start-Sleep -Milliseconds 100
    }
    if(-not $NoRestore){& $Dotnet restore NekoProxy.sln;if($LASTEXITCODE -ne 0){throw 'Restore failed'}}
    $buildArgs=@('build','NekoProxy.sln','-c','Release','--no-restore','-p:UsedAvaloniaProducts=')
    if($BuildDirectory){$buildArgs += '-p:OutDir='+[IO.Path]::GetFullPath($BuildDirectory).TrimEnd('/','\')+[IO.Path]::DirectorySeparatorChar}
    & $Dotnet @buildArgs 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw 'Release build failed'}
    $dll=if($BuildDirectory){Join-Path $BuildDirectory 'NekoProxy.Tests.dll'}else{Join-Path $repo 'tests/NekoProxy.Tests/bin/Release/net10.0/NekoProxy.Tests.dll'}
    & $Dotnet vstest $dll '--logger:trx;LogFileName=all.trx' ('--ResultsDirectory:'+$output) 2>&1 | Tee-Object -FilePath (Join-Path $output 'tests.log')
    $testExit=$LASTEXITCODE
    [xml]$trx=Get-Content -LiteralPath (Join-Path $output 'all.trx') -Raw
    $counts=$trx.TestRun.ResultSummary.Counters
    $counts | Select-Object total,executed,passed,failed,notExecuted | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'counts.json')
    if($testExit -ne 0 -or [int]$counts.total -lt 232 -or [int]$counts.passed -ne [int]$counts.total -or [int]$counts.notExecuted -ne 0){throw 'All tests, including OS credentials and localhost SSH, must execute and pass. No skip/fallback accepted.'}
}finally{
    Pop-Location
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'stop') -Value 'completed'
    if(-not $fixture.WaitForExit(6000)){$fixture.Kill($true);$fixture.WaitForExit()}
    $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output 'fixture-stdout.log')
    $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output 'fixture-stderr.log')
    $fixture.Dispose()
}
