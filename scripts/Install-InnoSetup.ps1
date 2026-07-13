param(
    [string]$Version = '6.7.3',
    [string]$ExpectedSha256 = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732',
    [long]$ExpectedLength = 10592232
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $root '.tools'
$installDir = Join-Path $toolsRoot 'innosetup'
$iscc = Join-Path $installDir 'ISCC.exe'
if (Test-Path -LiteralPath $iscc) {
    Write-Output $iscc
    exit 0
}

New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
$installer = Join-Path $toolsRoot "innosetup-$Version.exe"
$urlVersion = $Version.Replace('.', '_')
$url = "https://github.com/jrsoftware/issrc/releases/download/is-$urlVersion/innosetup-$Version.exe"

for ($attempt = 1; $attempt -le 12; $attempt++) {
    $currentLength = if (Test-Path -LiteralPath $installer) { (Get-Item -LiteralPath $installer).Length } else { 0 }
    if ($currentLength -eq $ExpectedLength) { break }
    if ($currentLength -gt $ExpectedLength) { Remove-Item -LiteralPath $installer -Force; $currentLength = 0 }
    $curlArguments = @('-L', '--fail', '--retry', '3', '--retry-all-errors')
    if ($currentLength -gt 0) { $curlArguments += @('-C', '-') }
    $curlArguments += @('-o', $installer, $url)
    & curl.exe @curlArguments
    if ($LASTEXITCODE -ne 0 -and $attempt -eq 12) { throw 'Inno Setup下载失败。' }
}

if (-not (Test-Path -LiteralPath $installer) -or (Get-Item -LiteralPath $installer).Length -ne $ExpectedLength) {
    throw 'Inno Setup下载不完整。'
}
$actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
if ($actualHash -ne $ExpectedSha256) { throw "Inno Setup校验失败：$actualHash" }
Unblock-File -LiteralPath $installer

$log = Join-Path $toolsRoot 'innosetup-install.log'
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="' + $installDir + '" /LOG="' + $log + '"'
$process = Start-Process -FilePath $installer -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $iscc)) {
    throw "Inno Setup本地安装失败，退出码：$($process.ExitCode)"
}
Write-Output $iscc
