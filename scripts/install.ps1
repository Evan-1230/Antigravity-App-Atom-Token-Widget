param(
    [string]$TestRoot = "",
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$IsSandbox = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($IsSandbox) {
    $LocalAppData = Join-Path $TestRoot "LocalAppData"
    if (-not (Test-Path -LiteralPath $LocalAppData)) {
        New-Item -ItemType Directory -Path $LocalAppData -Force | Out-Null
    }
} else {
    $LocalAppData = $env:LOCALAPPDATA
}

$PkgRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$InstallDir = Join-Path $LocalAppData "AntigravityAtomTokenWidget"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Antigravity 3D 原子 Token 計算器安裝程式" -ForegroundColor Cyan
Write-Host "  Antigravity Atom Token Widget Installer" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Stop only AntigravityAtomTokenWidget if already running
if (-not $IsSandbox) {
    $running = Get-Process -Name "AntigravityAtomTokenWidget" -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "[1/4] 正在關閉目前執行中的原子小工具 / Stopping running widget..." -ForegroundColor Yellow
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 350
    } else {
        Write-Host "[1/4] 準備安裝目錄 / Preparing install directory..." -ForegroundColor Cyan
    }
} else {
    Write-Host "[1/4] 準備沙盒安裝目錄 / Preparing sandbox install directory..." -ForegroundColor Cyan
}

# 2. Copy source & scripts into permanent install directory
foreach ($sub in @("", "assets", "src", "scripts")) {
    $targetSub = if ([string]::IsNullOrEmpty($sub)) { $InstallDir } else { Join-Path $InstallDir $sub }
    if (-not (Test-Path -LiteralPath $targetSub)) {
        New-Item -ItemType Directory -Path $targetSub -Force | Out-Null
    }
}

# Clear stale runtime learned_models.ini so bad timing cache from prior builds never lingers
$learnedIni = Join-Path $InstallDir "learned_models.ini"
if (Test-Path -LiteralPath $learnedIni) {
    Remove-Item -LiteralPath $learnedIni -Force -ErrorAction SilentlyContinue
}

$resolvedInstall = (Resolve-Path -LiteralPath $InstallDir).Path
if (-not [string]::Equals($PkgRoot, $resolvedInstall, [System.StringComparison]::OrdinalIgnoreCase)) {
    Get-ChildItem -LiteralPath (Join-Path $PkgRoot "src") -Filter "*.cs" | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $InstallDir ("src\" + $_.Name)) -Force
    }
    foreach ($scriptName in @("install.ps1", "uninstall.ps1")) {
        $srcScript = Join-Path $PkgRoot ("scripts\" + $scriptName)
        if (Test-Path -LiteralPath $srcScript) {
            Copy-Item -LiteralPath $srcScript -Destination (Join-Path $InstallDir ("scripts\" + $scriptName)) -Force
        }
    }
    foreach ($rootFile in @("Install_widget.bat", "Uninstall_widget.bat", "README.md", "README.tw.md")) {
        $srcFile = Join-Path $PkgRoot $rootFile
        if (Test-Path -LiteralPath $srcFile) {
            Copy-Item -LiteralPath $srcFile -Destination (Join-Path $InstallDir $rootFile) -Force
        }
    }
    $pkgIco = Join-Path $PkgRoot "assets\atom_widget.ico"
    if (Test-Path -LiteralPath $pkgIco) {
        Copy-Item -LiteralPath $pkgIco -Destination (Join-Path $InstallDir "assets\atom_widget.ico") -Force
    }
}

# 3. Generate procedural multi-size Atom .ico if missing
$icoPath = Join-Path $InstallDir "assets\atom_widget.ico"
if (-not (Test-Path -LiteralPath $icoPath)) {
    Write-Host "[2/4] 正在產生原子圖示 / Generating Atom icon..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.Drawing
    $sizes = @(16, 32, 48, 64)
    $pngChunks = New-Object System.Collections.Generic.List[byte[]]
    foreach ($sz in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad = [float]($sz * 0.06)
        $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 22, 24, 34))
        $g.FillEllipse($bgBrush, $pad, $pad, [float]($sz - $pad * 2), [float]($sz - $pad * 2))

        $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(225, 165, 178, 196), [float][Math]::Max(1.2, $sz * 0.05))
        $cx = [float]($sz * 0.5)
        $cy = [float]($sz * 0.5)
        $rx = [float]($sz * 0.38)
        $ry = [float]($sz * 0.16)
        foreach ($deg in @(30.0, 90.0, 150.0)) {
            $st = $g.Save()
            $g.TranslateTransform($cx, $cy)
            $g.RotateTransform([float]$deg)
            $g.DrawEllipse($ringPen, -$rx, -$ry, $rx * 2, $ry * 2)
            $g.Restore($st)
        }

        $nr = [float]($sz * 0.115)
        $pBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(232, 56, 48))
        $nBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(32, 86, 198))
        $g.FillEllipse($nBrush, $cx - $nr * 1.15, $cy - $nr * 0.9, $nr * 1.4, $nr * 1.4)
        $g.FillEllipse($pBrush, $cx - $nr * 0.25, $cy - $nr * 0.5, $nr * 1.45, $nr * 1.45)

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngChunks.Add($ms.ToArray())

        $ms.Dispose()
        $pBrush.Dispose()
        $nBrush.Dispose()
        $ringPen.Dispose()
        $bgBrush.Dispose()
        $g.Dispose()
        $bmp.Dispose()
    }

    $outStream = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $bw = New-Object System.IO.BinaryWriter($outStream)
    $bw.Write([UInt16]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $bytes = $pngChunks[$i]
        $bw.Write([byte]$sz)
        $bw.Write([byte]$sz)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([UInt16]1)
        $bw.Write([UInt16]32)
        $bw.Write([UInt32]$bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $bytes.Length
    }
    for ($i = 0; $i -lt $pngChunks.Count; $i++) {
        $bw.Write($pngChunks[$i])
    }
    $bw.Flush()
    $bw.Dispose()
    $outStream.Dispose()
} else {
    Write-Host "[2/4] 使用現有原子圖示 / Using existing Atom icon..." -ForegroundColor Cyan
}

# 4. Compile via Windows built-in .NET Framework 4.x csc.exe
Write-Host "[3/4] 正在本機編譯 3D 原子小工具 / Compiling locally via .NET csc.exe..." -ForegroundColor Cyan
$fwCandidates = @(
    @{ Dir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"; Plat = "x64" },
    @{ Dir = "C:\Windows\Microsoft.NET\Framework\v4.0.30319"; Plat = "x86" }
)
$fwDir = $null
$plat = "x64"
foreach ($cand in $fwCandidates) {
    if (Test-Path -LiteralPath (Join-Path $cand.Dir "csc.exe")) {
        $fwDir = $cand.Dir
        $plat = $cand.Plat
        break
    }
}
if ($null -eq $fwDir) {
    throw "找不到 Windows 內建 .NET Framework 4.x 編譯器 (csc.exe)。 / Could not locate .NET Framework 4.x csc.exe."
}

$cscExe = Join-Path $fwDir "csc.exe"
$exeOut = Join-Path $InstallDir "AntigravityAtomTokenWidget.exe"

$cscArgs = @(
    "/nologo",
    "/optimize+",
    "/target:winexe",
    "/platform:$plat",
    "/win32icon:$icoPath",
    "/out:$exeOut",
    "/r:$fwDir\WPF\PresentationFramework.dll",
    "/r:$fwDir\WPF\PresentationCore.dll",
    "/r:$fwDir\WPF\WindowsBase.dll",
    "/r:$fwDir\WPF\UIAutomationClient.dll",
    "/r:$fwDir\WPF\UIAutomationTypes.dll",
    "/r:$fwDir\System.Xaml.dll",
    "/r:$fwDir\System.Windows.Forms.dll",
    "/r:$fwDir\System.Drawing.dll",
    (Join-Path $InstallDir "src\Models.cs"),
    (Join-Path $InstallDir "src\NativeMethods.cs"),
    (Join-Path $InstallDir "src\AntigravityDataReader.cs"),
    (Join-Path $InstallDir "src\Atom3DRenderer.cs"),
    (Join-Path $InstallDir "src\WidgetWindow.cs"),
    (Join-Path $InstallDir "src\WindowTracker.cs"),
    (Join-Path $InstallDir "src\SettingsWindow.cs"),
    (Join-Path $InstallDir "src\TrayController.cs"),
    (Join-Path $InstallDir "src\Program.cs")
)

$compileOutput = & $cscExe @cscArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    $compileOutput | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "編譯失敗 (ExitCode=$LASTEXITCODE)。 / Compilation failed."
}

if (-not $NoLaunch -and -not $IsSandbox) {
    Write-Host "[4/4] 正在啟動 Antigravity 3D 原子 Token 小工具 / Launching widget..." -ForegroundColor Green
    Start-Process -FilePath $exeOut -WorkingDirectory $InstallDir
} else {
    Write-Host "[4/4] 編譯完成（未啟動） / Build completed (NoLaunch)." -ForegroundColor Green
}

Write-Host ""
Write-Host "完成！安裝位置: $InstallDir" -ForegroundColor Cyan
