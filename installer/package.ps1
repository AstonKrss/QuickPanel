# QuickPanel 打包脚本
# 用法: powershell -ExecutionPolicy Bypass -File package.ps1
# 产出: dist/QuickPanel-v<version>-setup/ （含 exe + 安装/卸载脚本 + 说明）

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'QuickPanel.csproj'

Write-Host "=== QuickPanel 打包 ===" -ForegroundColor Cyan

# 1. 读取版本号
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "版本: $version"

# 2. 发布 Release 构建
Write-Host ""
Write-Host "发布 Release 构建..." -ForegroundColor Yellow
Push-Location $root
$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnetExe = $cmd.Source }
}
if (-not (Test-Path $dotnetExe)) {
    Pop-Location
    Write-Host "找不到 dotnet CLI，请安装 .NET 8 SDK" -ForegroundColor Red
    exit 1
}
& $dotnetExe publish $proj -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -p:PublishSingleFile=true -o (Join-Path $root 'dist/_publish')
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "发布失败" -ForegroundColor Red; exit 1 }
Pop-Location

# 3. 组装安装包目录
$setupDir = Join-Path $root "dist/QuickPanel-v$version-setup"
if (Test-Path $setupDir) { Remove-Item $setupDir -Recurse -Force }
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

Copy-Item (Join-Path $root 'dist/_publish/QuickPanel.exe') $setupDir -Force
Copy-Item (Join-Path $PSScriptRoot 'install.ps1') $setupDir -Force
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $setupDir -Force
Copy-Item (Join-Path $root 'README.md') $setupDir -Force -ErrorAction SilentlyContinue

# 4. 生成一键安装批处理（双击即可）
$bat = @"
@echo off
chcp 65001 >nul
title QuickPanel 安装
echo.
echo   QuickPanel 安装程序
echo   ==================
echo.
echo   将安装到: %LOCALAPPDATA%\QuickPanel
echo.
pause
powershell -ExecutionPolicy Bypass -File "%~dp0install.ps1"
pause
"@
Set-Content (Join-Path $setupDir '安装.bat') $bat -Encoding UTF8

# 5. 生成一键卸载批处理
$unbat = @"
@echo off
chcp 65001 >nul
title QuickPanel 卸载
powershell -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
pause
"@
Set-Content (Join-Path $setupDir '卸载.bat') $unbat -Encoding UTF8

# 6. 打 zip
$zip = Join-Path $root "dist/QuickPanel-v$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $setupDir '*') -DestinationPath $zip -CompressionLevel Optimal

# 7. 清理临时发布目录
Remove-Item (Join-Path $root 'dist/_publish') -Recurse -Force -ErrorAction SilentlyContinue

# 8. 同步一份裸 exe 到 dist
Copy-Item (Join-Path $setupDir 'QuickPanel.exe') (Join-Path $root 'dist/QuickPanel.exe') -Force

Write-Host ""
Write-Host "=== 打包完成 ===" -ForegroundColor Green
Write-Host "  安装包目录: $setupDir"
Write-Host "  ZIP:        $zip"
$zipSize = [math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host "  大小:       $zipSize MB"
Write-Host ""
Write-Host "  用户双击「安装.bat」即可安装"
