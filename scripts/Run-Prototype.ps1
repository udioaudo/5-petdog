param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Prototype.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dll = Join-Path $root "prototypes\DesktopSpike\bin\$Configuration\net10.0-windows\HMinus.DesktopSpike.dll"
Start-Process -FilePath $dotnet -ArgumentList @($dll)
Write-Output $dll
