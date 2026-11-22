param(
    [string]$TestRoot = ""
)

$ErrorActionPreference = "Stop"

$IsSandbox = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($IsSandbox) {
    $LocalAppData = Join-Path $TestRoot "LocalAppData"
} else {
    $LocalAppData = $env:LOCALAPPDATA
}

$InstallDir = Join-Path $LocalAppData "AntigravityAtomTokenWidget"

Write-Host "============================================================" -ForegroundColor Yellow
Write-Host "  Antigravity 3D 原子 Token 計算器移除程式" -ForegroundColor Yellow
Write-Host "  Antigravity Atom Token Widget Uninstaller" -ForegroundColor Yellow
Write-Host "============================================================" -ForegroundColor Yellow
Write-Host ""

if (-not $IsSandbox) {
    $running = Get-Process -Name "AntigravityAtomTokenWidget" -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "[1/3] 正在關閉執行中的原子小工具 / Stopping widget..." -ForegroundColor Yellow
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 350
    } else {
        Write-Host "[1/3] 沒有執行中的原子小工具 / Widget not running." -ForegroundColor Cyan
    }

    Write-Host "[2/3] 正在移除開機自動啟動登錄檔 / Removing startup registry entry..." -ForegroundColor Cyan
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if (Test-Path -LiteralPath $runKey) {
        $prop = Get-ItemProperty -LiteralPath $runKey -Name "AntigravityAtomTokenWidget" -ErrorAction SilentlyContinue
        if ($null -ne $prop) {
            Remove-ItemProperty -LiteralPath $runKey -Name "AntigravityAtomTokenWidget" -Force
        }
    }
}

Write-Host "[3/3] 正在清理安裝資料夾 / Removing install directory..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "已完整移除 Antigravity 原子 Token 計算器。" -ForegroundColor Green
