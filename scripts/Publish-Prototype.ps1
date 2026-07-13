param(
    [string]$Runtime = 'win-x64',
    [string]$OutputName = 'phase0-spike-v3'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw '未找到项目本地.NET SDK。' }
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$previewRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\preview'))
$out = [System.IO.Path]::GetFullPath((Join-Path $previewRoot $OutputName))
$expectedPrefix = $previewRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $out.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '发布目录必须位于 artifacts\preview 内。'
}

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
& $dotnet publish (Join-Path $root 'prototypes\DesktopSpike\DesktopSpike.csproj') -c Release -r $Runtime --self-contained true -o $out -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $root 'prototypes\README.md') -Destination (Join-Path $out '体验说明.md')
Write-Output $out

