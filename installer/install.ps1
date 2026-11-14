# QuickPanel 安装脚本（无需 Inno Setup）
# 用法: powershell -ExecutionPolicy Bypass -File install.ps1
# 作用: 复制 exe 到 %LOCALAPPDATA%\QuickPanel，创建开始菜单和桌面快捷方式，注册卸载信息

param(
    [ValidateSet('Ask','Yes','No')]
    [string]$AutoStart = 'Ask',
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
try {
$AppName = 'QuickPanel'
$InstallDir = Join-Path $env:LOCALAPPDATA $AppName
$SourceExe = Join-Path $PSScriptRoot 'QuickPanel.exe'

function Say($msg, $color = 'Gray') { Write-Host $msg -ForegroundColor $color }
$LogPath = Join-Path $env:TEMP 'QuickPanel-install.log'
function Log($msg) { try { Add-Content -Path $LogPath -Value ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $msg) -Encoding UTF8 } catch {} }
Log "=== install start ==="
Log "InstallDir=$InstallDir"
Log "SourceExe=$SourceExe exists=$(Test-Path $SourceExe)"

Say "=== $AppName 安装程序 ===" 'Cyan'
Say ""

if (-not (Test-Path $SourceExe)) {
    Say "错误: 找不到 QuickPanel.exe（应与本脚本在同一目录）" Red
    exit 1
}

# 1. 关闭正在运行的实例
$running = Get-Process $AppName -ErrorAction SilentlyContinue
if ($running) {
    Say "检测到 $AppName 正在运行，正在关闭..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# 2. 创建安装目录
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Say "创建目录: $InstallDir"
}

# 3. 复制文件
Copy-Item $SourceExe (Join-Path $InstallDir 'QuickPanel.exe') -Force
Say "复制程序文件完成"

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
Say "创建开始菜单快捷方式"

# 桌面
$desktop = [Environment]::GetFolderPath('Desktop')
$desktopLnk = Join-Path $desktop 'QuickPanel.lnk'
New-Shortcut $desktopLnk $exePath
Say "创建桌面快捷方式"

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
Say "注册卸载信息"

# 6. 询问是否开机自启
Say ""
if ($AutoStart -eq 'Ask') {
    $auto = Read-Host "是否开机自动启动？(Y/n)"
} else {
    $auto = if ($AutoStart -eq 'Yes') { 'y' } else { 'n' }
}
if ($auto -ne 'n' -and $auto -ne 'N') {
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'QuickPanel' -Value "`"$exePath`""
    Say "已设置开机自启" Green
} else {
    Say "跳过开机自启"
}

# 7. 启动程序
Say ""
Say "启动 QuickPanel..." Cyan
Start-Process $exePath
Start-Sleep -Seconds 3

Say ""
Say "=== 安装完成 ===" Green
Say "  安装位置: $InstallDir"
Say "  卸载方式: 设置 → 应用 → QuickPanel → 卸载"
Say "  或运行:   $InstallDir\uninstall.ps1"
Log "=== install OK ==="

if ($Quiet) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "QuickPanel 已安装到：`n$InstallDir`n`n已创建桌面与开始菜单快捷方式，并设置为开机启动。`n可以在「设置」里关闭开机启动。",
        "QuickPanel 安装完成", 'OK', 'Information') | Out-Null
}

} catch {
    # v1.40：任何一步失败都弹可见的错误框 + 写日志，不再无声无息
    Say "" 'Gray'
    Say ("安装失败：{0}" -f $_.Exception.Message) 'Red'
    Say ("详细日志：{0}" -f $LogPath) 'Yellow'
    Log ("ERROR: " + $_.Exception.Message)
    Log $_.ScriptStackTrace
    if ($Quiet) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            ("安装失败：{0}`n`n详细日志：{1}`n`n请把日志文件发给开发者。" -f $_.Exception.Message, $LogPath),
            "QuickPanel 安装失败", 'OK', 'Error') | Out-Null
    }
    exit 1
}