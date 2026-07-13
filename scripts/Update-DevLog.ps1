param(
    [string]$Completed,
    [string]$Todo,
    [string]$Verification,
    [string]$Risk,
    [string]$Decision
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $projectRoot '开发日志'
$date = Get-Date -Format 'yyyy-MM-dd'
$logPath = Join-Path $logDirectory "$date.md"

if (-not (Test-Path -LiteralPath $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

if (-not (Test-Path -LiteralPath $logPath)) {
    $initialContent = @"
# 开发日志：$date

## 今日目标

- [ ] 从开发路线图选择一个最小目标

## 已完成事项

## 验证与测试

## 决策与变更

## 风险与问题

## 待办事项

## 结束状态

- 当前阶段：请根据 docs/02-开发路线图.md 填写
- 是否可构建：尚未验证
- 是否可测试：尚未验证
- 建议下一步：完成当前最小目标后再选择下一项
"@
    Set-Content -LiteralPath $logPath -Value $initialContent -Encoding utf8
}

function Add-LogItem {
    param([string]$Heading, [string]$Value, [switch]$Checkbox)

    if ([string]::IsNullOrWhiteSpace($Value)) { return }

    $text = Get-Content -LiteralPath $logPath -Raw -Encoding utf8
    $headingLine = "## $Heading"
    $headingIndex = $text.IndexOf($headingLine, [System.StringComparison]::Ordinal)
    if ($headingIndex -lt 0) { throw "日志中缺少章节：$Heading" }

    $insertAt = $headingIndex + $headingLine.Length
    $line = if ($Checkbox) { "`r`n`r`n- [ ] $Value" } else { "`r`n`r`n- $Value" }
    $updated = $text.Insert($insertAt, $line)
    Set-Content -LiteralPath $logPath -Value $updated -Encoding utf8
}

Add-LogItem -Heading '已完成事项' -Value $Completed
Add-LogItem -Heading '验证与测试' -Value $Verification
Add-LogItem -Heading '决策与变更' -Value $Decision
Add-LogItem -Heading '风险与问题' -Value $Risk
Add-LogItem -Heading '待办事项' -Value $Todo -Checkbox

Write-Output $logPath
