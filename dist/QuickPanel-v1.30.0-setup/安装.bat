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
