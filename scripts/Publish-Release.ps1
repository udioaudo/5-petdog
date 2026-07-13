param(
    [string]$Runtime = 'win-x64',
    [string]$Version = '1.0.0',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw '未找到项目本地.NET SDK。' }
$env:DOTNET_ROOT = Split-Path -Parent $dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\release'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $releaseRoot "小狗效率屋-v$Version-x64\portable"
}
$out = [System.IO.Path]::GetFullPath($OutputDirectory)
$expectedPrefix = $releaseRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $out.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '正式发布目录必须位于 artifacts\release 内。'
}
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Path $out -Force | Out-Null

& $dotnet publish (Join-Path $root 'prototypes\DesktopSpike\DesktopSpike.csproj') `
    -c Release -r $Runtime --self-contained true -o $out `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output $out
