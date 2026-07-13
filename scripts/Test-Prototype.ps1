param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Prototype.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
& $dotnet run --project (Join-Path $root 'prototypes\DesktopSpike.Verification\DesktopSpike.Verification.csproj') -c $Configuration --no-build
exit $LASTEXITCODE
