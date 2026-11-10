# QuickPanel 卸载脚本
# 用法: powershell -ExecutionPolicy Bypass -File uninstall.ps1
# 或从 设置 → 应用 → QuickPanel → 卸载

$ErrorActionPreference = 'SilentlyContinue'
$AppName = 'QuickPanel'
$InstallDir = Join-Path $env:LOCALAPPDATA $AppName

Write-Host "=== $AppName 卸载程序 ===" -ForegroundColor Cyan
Write-Host ""

$answer = Read-Host "确认卸载 QuickPanel？用户配置（$env:APPDATA\QuickPanel）将保留。(y/N)"
if ($answer -ne 'y' -and $answer -ne 'Y') {
    Write-Host "已取消"
    exit 0
}

# 1. 关闭进程
$running = Get-Process $AppName -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "关闭 $AppName..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# 2. 移除开机自启
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'QuickPanel' -ErrorAction SilentlyContinue
Write-Host "移除开机自启"

# 3. 删除快捷方式
$startLnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\QuickPanel.lnk'
Remove-Item $startLnk -Force -ErrorAction SilentlyContinue
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'QuickPanel.lnk'
Remove-Item $desktopLnk -Force -ErrorAction SilentlyContinue
Write-Host "删除快捷方式"

# 4. 移除卸载注册项
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QuickPanel' -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "移除注册表项"

# 5. 删除程序文件
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "删除程序文件: $InstallDir"
}

# 6. 询问是否删除用户配置
Write-Host ""
$cfg = Read-Host "是否同时删除用户配置和图标缓存？(y/N)"
if ($cfg -eq 'y' -or $cfg -eq 'Y') {
    $dataDir = Join-Path $env:APPDATA $AppName
    if (Test-Path $dataDir) {
        Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "已删除配置目录: $dataDir"
    }
} else {
    Write-Host "保留配置: $env:APPDATA\$AppName"
}

Write-Host ""
Write-Host "=== 卸载完成 ===" -ForegroundColor Green
