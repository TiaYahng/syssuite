# 以管理员身份运行真机探针（SMART / 传感器）。
#
# 原理：`dotnet test` 的宿主进程不带 manifest，无法自动提权（这正是之前
# 非提权验证失败的原因）。本脚本用 ShellExecute(runas) 拉起一个提权的
# PowerShell，在提权会话里跑 vstest 的过滤用例，把结果写到 %TEMP%。
#
# 用法：
#   pwsh -NoProfile -File ./rules/Probe-Elevated.ps1                          # 默认跑 SMART
#   pwsh -NoProfile -File ./rules/Probe-Elevated.ps1 -Probe Sensors           # 跑传感器
#   pwsh -NoProfile -File ./rules/Probe-Elevated.ps1 -Probe All              # 两个都跑
#
# 说明：NVMe 健康日志其实**不需要**提权（走 IOCTL_STORAGE_QUERY_PROPERTY），
# 只有 ATA 直通需要。这里统一提权只是为了顺带覆盖 ATA 盘与传感器驱动的完整读权限。
[CmdletBinding()]
param(
    [ValidateSet('Smart', 'Sensors', 'All')]
    [string]$Probe = 'Smart'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# 探针名 → (测试类过滤器, 结果文件名)
$probeMap = @{
    Smart   = @{ Filter = 'FullyQualifiedName~SmartProbeTests';  Result = 'syssuite-smart-probe.txt' }
    Sensors = @{ Filter = 'FullyQualifiedName~SensorProbeTests'; Result = 'syssuite-sensor-probe.txt' }
}

$selected = if ($Probe -eq 'All') { @('Smart', 'Sensors') } else { @($Probe) }

$logOut = Join-Path $env:TEMP 'syssuite-elevated-probe.log'
$doneFlag = Join-Path $env:TEMP 'syssuite-elevated-probe.done'

# 清掉上一轮痕迹，避免读到陈旧结果
$stale = @($logOut, $doneFlag) + ($selected | ForEach-Object { Join-Path $env:TEMP $probeMap[$_].Result })
foreach ($path in $stale) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -Confirm:$false }
}

$filterList = ($selected | ForEach-Object { $probeMap[$_].Filter }) -join '|'
$resultList = ($selected | ForEach-Object { $probeMap[$_].Result }) -join ';'

# 提权会话内执行的脚本：写成一个独立文件，避免多层引号转义地狱
$innerScript = Join-Path $env:TEMP 'syssuite-inner-probe.ps1'
$inner = @'
param([string]$RepoRoot, [string]$Filter, [string]$ResultList, [string]$LogOut, [string]$DoneFlag)
Set-Location $RepoRoot
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("IsElevated = $([System.Security.Principal.WindowsPrincipal]::new([System.Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator))")
try {
    $output = & dotnet test -c Debug --no-build --nologo --filter $Filter 2>&1 | Out-String
    $lines.Add($output)
}
catch {
    $lines.Add("EXCEPTION: $($_.Exception.Message)")
}
foreach ($name in ($ResultList -split ';')) {
    $path = Join-Path $env:TEMP $name
    $lines.Add("$name Exists = $(Test-Path -LiteralPath $path)")
}
$lines -join "`n" | Out-File -LiteralPath $LogOut -Encoding utf8
"done" | Out-File -LiteralPath $DoneFlag -Encoding utf8
'@
Set-Content -LiteralPath $innerScript -Value $inner -Encoding utf8

$arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -RepoRoot "{1}" -Filter "{2}" -ResultList "{3}" -LogOut "{4}" -DoneFlag "{5}"' -f `
    $innerScript, $repoRoot, $filterList, $resultList, $logOut, $doneFlag

Write-Host "[probe] 请求提权运行 $Probe 探针（会弹出 UAC，请点「是」）"
try {
    $process = Start-Process -FilePath 'pwsh' -ArgumentList $arguments -Verb RunAs -PassThru -ErrorAction Stop
}
catch {
    Write-Host "[probe] 提权被拒绝或不可用：$($_.Exception.Message)"
    Write-Host "[probe] 本环境无法交互式确认 UAC。请手动以管理员身份运行："
    Write-Host "         pwsh -NoProfile -File `"$PSCommandPath`" -Probe $Probe"
    exit 2
}

Write-Host "[probe] 已拉起提权进程 PID=$($process.Id)，等待完成..."
$process.WaitForExit(180000) | Out-Null

if (Test-Path -LiteralPath $logOut) {
    Write-Host "[probe] 提权会话日志："
    Get-Content -LiteralPath $logOut
}
else {
    Write-Host "[probe] 未生成日志，提权会话可能未执行（UAC 未确认？）"
}

foreach ($name in $selected) {
    $path = Join-Path $env:TEMP $probeMap[$name].Result
    if (Test-Path -LiteralPath $path) {
        Write-Host "[probe] ----- $name 探针结果 -----"
        Get-Content -LiteralPath $path
    }
    else {
        Write-Host "[probe] 未生成 $name 探针结果文件。"
    }
}
