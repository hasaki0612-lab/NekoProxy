param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or -not $IsWindows){throw 'This silent install/uninstall smoke is restricted to a disposable GitHub Windows runner.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -ErrorAction Stop | Out-Null
$install=Join-Path $env:RUNNER_TEMP ('NekoProxy-install-'+[Guid]::NewGuid().ToString('N'))
$desktopLink=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'NekoProxy.lnk'
if(Test-Path -LiteralPath $desktopLink){throw 'Existing shortcut preserved; cannot run isolated installer smoke.'}
$data=Join-Path $env:LOCALAPPDATA 'NekoProxy'
if(Test-Path -LiteralPath $data){throw 'Existing data preserved; use a fresh hosted runner.'}
New-Item -ItemType Directory -Path $data | Out-Null
$canary=Join-Path $data 'installer-test-canary.txt'
Set-Content -LiteralPath $canary -Value 'isolated hosted-runner test, no credential'
$args=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR='+$install),'/TASKS=startmenu',('/LOG='+ (Join-Path $output 'install.log')))
$p=Start-Process -FilePath ([IO.Path]::GetFullPath($Setup)) -ArgumentList ($args | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru -Wait
if($p.ExitCode -ne 0){throw 'Installer returned failure'}
$exe=Join-Path $install 'NekoProxy.Desktop.exe'
if(-not(Test-Path -LiteralPath $exe) -or -not(Test-Path -LiteralPath $desktopLink)){throw 'Installed exe or desktop shortcut missing'}
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($desktopLink)
if($shortcut.TargetPath -ne $exe -or $shortcut.IconLocation -notlike ($exe+'*')){throw 'Shortcut target/icon mismatch'}
$startLink=Join-Path ([Environment]::GetFolderPath('Programs')) 'NekoProxy.lnk'
if(-not(Test-Path -LiteralPath $startLink)){throw 'Selected Start Menu shortcut missing'}
$uninstaller=Join-Path $install 'unins000.exe'
$p=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $output 'uninstall.log')+'"')) -WindowStyle Hidden -PassThru -Wait
if($p.ExitCode -ne 0 -or (Test-Path -LiteralPath $desktopLink) -or -not(Test-Path -LiteralPath $canary)){throw 'Uninstall/shortcut cleanup/data preservation failed'}
@{InstallerExit=0;DesktopShortcut=$true;MatchingExeIcon=$true;StartMenuShortcut=$true;UninstallExit=0;UserDataPreserved=$true;ProxySettingsTouched=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'installer-smoke.json')
