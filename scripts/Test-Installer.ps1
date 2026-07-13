param(
    [string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $root "artifacts\release\小狗效率屋-v$Version-x64"
$portableExe = Join-Path $releaseRoot 'portable\HMinus.DesktopSpike.exe'
if (-not (Test-Path -LiteralPath $portableExe)) { throw '请先生成正式版便携程序。' }

$runId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $root "artifacts\installer-test\$runId"
$installDir = Join-Path $testRoot 'program'
$dataDir = Join-Path $testRoot 'local-data'
$testBaseName = "HMinus-Installer-Test-$runId"
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
Set-Content -LiteralPath (Join-Path $dataDir 'preserve-check.txt') -Value 'installer lifecycle test' -Encoding utf8

$buildOutput = @(& (Join-Path $PSScriptRoot 'Build-Installer.ps1') -Version $Version -SkipPublish -TestAppDataDirectory $dataDir -OutputBaseFilename $testBaseName)
$testInstaller = $buildOutput[-1]
if (-not (Test-Path -LiteralPath $testInstaller)) { throw '测试安装包生成失败。' }

function Wait-PathState([string]$Path, [bool]$ShouldExist, [int]$TimeoutSeconds = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if ((Test-Path -LiteralPath $Path) -eq $ShouldExist) { return }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "路径状态未按时更新：$Path（期望存在=$ShouldExist）"
}

function Invoke-CheckedProcess([string]$FilePath, [string]$Arguments, [int]$TimeoutSeconds = 120) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "进程超时：$FilePath"
    }
    if ($process.ExitCode -ne 0) { throw "进程失败（$($process.ExitCode)）：$FilePath $Arguments" }
}

$installLog = Join-Path $testRoot 'install.log'
$installArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="' + $installDir + '" /MERGETASKS="!desktopicon" /LOG="' + $installLog + '"'
Invoke-CheckedProcess $testInstaller $installArgs 180
$installedExe = Join-Path $installDir 'HMinus.DesktopSpike.exe'
$uninstaller = Join-Path $installDir 'unins000.exe'
if (-not (Test-Path -LiteralPath $installedExe)) { throw '安装后未找到主程序。' }
if (-not (Test-Path -LiteralPath $uninstaller)) { throw '安装后未找到卸载程序。' }

$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\小狗效率屋.lnk'
if (-not (Test-Path -LiteralPath $startMenuShortcut)) { throw '开始菜单快捷方式未创建。' }
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) '小狗效率屋.lnk'
if (Test-Path -LiteralPath $desktopShortcut) { throw '桌面快捷方式不应默认创建。' }

$app = Start-Process -FilePath $installedExe -PassThru
Start-Sleep -Seconds 5
$app.Refresh()
if ($app.HasExited) { throw '安装后的主程序未能保持运行。' }

# 覆盖安装必须能够处理正在运行的程序，并保留单一卸载项。
Invoke-CheckedProcess $testInstaller $installArgs 180
Start-Sleep -Seconds 2
if (-not $app.HasExited) {
    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    throw '覆盖安装未关闭正在运行的旧程序。'
}
$uninstallEntries = @(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq '小狗效率屋' })
if ($uninstallEntries.Count -ne 1) { throw "卸载列表应只有一项，实际为$($uninstallEntries.Count)项。" }

# 默认静默卸载不传DELETEAPPDATA，必须保留测试数据。
Invoke-CheckedProcess $uninstaller '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' 120
Wait-PathState $startMenuShortcut $false
if (-not (Test-Path -LiteralPath (Join-Path $dataDir 'preserve-check.txt'))) { throw '默认卸载错误地删除了本机数据。' }
if (Test-Path -LiteralPath $startMenuShortcut) { throw '卸载后开始菜单快捷方式仍然存在。' }

# 重装后显式选择删除数据，只能清除编译时固定的测试数据目录。
Invoke-CheckedProcess $testInstaller $installArgs 180
$uninstaller = Join-Path $installDir 'unins000.exe'
Set-Content -LiteralPath (Join-Path $testRoot 'outside-managed-directory.txt') -Value 'must remain' -Encoding utf8
Invoke-CheckedProcess $uninstaller '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DELETEAPPDATA' 120
Wait-PathState $dataDir $false
if (Test-Path -LiteralPath $dataDir) { throw '显式删除数据后，受管理测试目录仍然存在。' }
if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'outside-managed-directory.txt'))) { throw '卸载程序删除了受管理目录之外的文件。' }

Remove-Item -LiteralPath $testInstaller -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
Write-Output 'PASS  安装、启动、覆盖安装、快捷方式、默认保留数据和显式删除数据验证通过。'

