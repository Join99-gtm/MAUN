@echo off
chcp 65001 >nul
title GooseDeluxe installer
echo Starting GooseDeluxe installer...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo Something went wrong. See install-log.txt next to this file.
  pause
)
