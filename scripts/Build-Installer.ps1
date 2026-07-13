param(
    [string]$Version = '1.0.0',
    [switch]$SkipPublish,
    [string]$TestAppDataDirectory = '',
    [string]$OutputBaseFilename = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts\release\小狗效率屋-v$Version-x64"))
$portable = Join-Path $releaseRoot 'portable'
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'Publish-Release.ps1') -Version $Version -OutputDirectory $portable
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if (-not (Test-Path -LiteralPath (Join-Path $portable 'HMinus.DesktopSpike.exe'))) {
    throw '未找到正式版便携程序，请先执行发布。'
}
$iscc = & (Join-Path $PSScriptRoot 'Install-InnoSetup.ps1')
if (-not (Test-Path -LiteralPath $iscc)) { throw '未找到Inno Setup编译器。' }

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$baseName = if ([string]::IsNullOrWhiteSpace($OutputBaseFilename)) { "小狗效率屋-Setup-$Version-x64" } else { $OutputBaseFilename }
$defines = @(
    "/DMySourceDir=$portable",
    "/DMyOutputDir=$releaseRoot",
    "/DMyOutputBaseFilename=$baseName"
)
if (-not [string]::IsNullOrWhiteSpace($TestAppDataDirectory)) {
    $defines += "/DMyAppDataDir=$([System.IO.Path]::GetFullPath($TestAppDataDirectory))"
}
& $iscc @defines (Join-Path $root 'installer\HMinus.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$installer = Join-Path $releaseRoot "$baseName.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw '安装包编译完成但未找到输出文件。' }
Write-Output $installer
