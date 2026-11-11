@echo off
chcp 65001 >nul
title QuickPanel 卸载
powershell -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
pause
