# QuickPanel 安装脚本（无需 Inno Setup）
# 用法: powershell -ExecutionPolicy Bypass -File install.ps1
# 作用: 复制 exe 到 %LOCALAPPDATA%\QuickPanel，创建开始菜单和桌面快捷方式，注册卸载信息

$ErrorActionPreference = 'Stop'
$AppName = 'QuickPanel'
$InstallDir = Join-Path $env:LOCALAPPDATA $AppName
$SourceExe = Join-Path $PSScriptRoot 'QuickPanel.exe'

Write-Host "=== $AppName 安装程序 ===" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $SourceExe)) {
    Write-Host "错误: 找不到 QuickPanel.exe（应与本脚本在同一目录）" -ForegroundColor Red
    exit 1
}

# 1. 关闭正在运行的实例
$running = Get-Process $AppName -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "检测到 $AppName 正在运行，正在关闭..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# 2. 创建安装目录
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Write-Host "创建目录: $InstallDir"
}

# 3. 复制文件
Copy-Item $SourceExe (Join-Path $InstallDir 'QuickPanel.exe') -Force
Write-Host "复制程序文件完成"

# 4. 创建快捷方式
function New-Shortcut($Path, $Target) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($Path)
    $sc.TargetPath = $Target
    $sc.WorkingDirectory = Split-Path $Target
    $sc.IconLocation = $Target
    $sc.Description = 'QuickPanel - Windows 快捷启动面板'
    $sc.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

$exePath = Join-Path $InstallDir 'QuickPanel.exe'

# 开始菜单
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$startLnk = Join-Path $startMenu 'QuickPanel.lnk'
New-Shortcut $startLnk $exePath
Write-Host "创建开始菜单快捷方式"

# 桌面
$desktop = [Environment]::GetFolderPath('Desktop')
$desktopLnk = Join-Path $desktop 'QuickPanel.lnk'
New-Shortcut $desktopLnk $exePath
Write-Host "创建桌面快捷方式"

# 5. 注册卸载信息（控制面板 → 程序和功能）
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickPanel"
New-Item -Path $uninstallKey -Force | Out-Null
$uninstallScript = Join-Path $InstallDir 'uninstall.ps1'
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $uninstallScript -Force -ErrorAction SilentlyContinue

Set-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value 'QuickPanel'
# v1.29：版本号取自 exe，不再写死（此前固定 1.16.0）
$displayVersion = (Get-Item $exePath).VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($displayVersion)) { $displayVersion = '1.0.0' }
Set-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $displayVersion
Set-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value 'AstonKrss'
Set-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value $exePath
Set-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value "powershell.exe -ExecutionPolicy Bypass -File `"$uninstallScript`""
Set-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord
$size = [math]::Round((Get-Item $exePath).Length / 1KB)
Set-ItemProperty -Path $uninstallKey -Name 'EstimatedSize' -Value $size -Type DWord
Write-Host "注册卸载信息"

# 6. 询问是否开机自启
Write-Host ""
$auto = Read-Host "是否开机自动启动？(Y/n)"
if ($auto -ne 'n' -and $auto -ne 'N') {
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'QuickPanel' -Value "`"$exePath`""
    Write-Host "已设置开机自启" -ForegroundColor Green
} else {
    Write-Host "跳过开机自启"
}

# 7. 启动程序
Write-Host ""
Write-Host "启动 QuickPanel..." -ForegroundColor Cyan
Start-Process $exePath
Start-Sleep -Seconds 3

Write-Host ""
Write-Host "=== 安装完成 ===" -ForegroundColor Green
Write-Host "  安装位置: $InstallDir"
Write-Host "  卸载方式: 设置 → 应用 → QuickPanel → 卸载"
Write-Host "  或运行:   $InstallDir\uninstall.ps1"
