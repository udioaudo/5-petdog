param(
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts\release\小狗效率屋-v$Version-x64"))
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\release'))
if (-not $releaseRoot.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '发布目录必须位于artifacts\release内。'
}
$installer = Join-Path $releaseRoot "小狗效率屋-Setup-$Version-x64.exe"
$portableExe = Join-Path $releaseRoot 'portable\HMinus.DesktopSpike.exe'
if (-not (Test-Path -LiteralPath $installer)) { throw '未找到正式安装包。' }
if (-not (Test-Path -LiteralPath $portableExe)) { throw '未找到正式便携程序。' }

Copy-Item -LiteralPath (Join-Path $root 'prototypes\README.md') -Destination (Join-Path $releaseRoot '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'docs\10-正式版发布与安装规范.md') -Destination (Join-Path $releaseRoot '发布与安装规范.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'docs\11-v1.0.0测试报告.md') -Destination (Join-Path $releaseRoot '测试报告.md') -Force

$portableZip = Join-Path $releaseRoot "小狗效率屋-Portable-$Version-x64.zip"
if (Test-Path -LiteralPath $portableZip) { Remove-Item -LiteralPath $portableZip -Force }
Compress-Archive -LiteralPath $portableExe -DestinationPath $portableZip -CompressionLevel Optimal

$releaseNotes = @'
# 小狗效率屋 v{0} 发布说明

- 正式安装包：`小狗效率屋-Setup-{0}-x64.exe`
- 便携程序备份：`小狗效率屋-Portable-{0}-x64.zip`
- 支持系统：Windows 10/11 x64
- 安装范围：当前用户，无需管理员权限
- 数据目录：`%LocalAppData%\HMinus\DesktopSpike`

本版没有自动到期、批量清空或开机启动。本机数据未加密，复制的密码、验证码、密钥和敏感截图也可能被保存。安装包暂未进行受信任代码签名，Windows可能显示SmartScreen提示。
'@ -f $Version
Set-Content -LiteralPath (Join-Path $releaseRoot '发布说明.md') -Value $releaseNotes -Encoding utf8

$hashTargets = @($installer, $portableExe, $portableZip)
$hashLines = foreach ($path in $hashTargets) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $relative = [System.IO.Path]::GetRelativePath($releaseRoot, $path)
    "$hash  $relative"
}
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value $hashLines -Encoding utf8
Get-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt')
