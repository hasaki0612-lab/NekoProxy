param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet restore NekoProxy.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build NekoProxy.sln -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet test NekoProxy.sln -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
