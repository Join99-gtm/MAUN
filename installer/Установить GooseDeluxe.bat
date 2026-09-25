@echo off
title GooseDeluxe installer
if not exist "%~dp0install.ps1" goto notextracted
if not exist "%~dp0Assets\Mods\GooseDeluxe\GooseDeluxe.dll" goto notextracted
echo Starting GooseDeluxe installer...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if not "%errorlevel%"=="0" (
  echo.
  echo Something went wrong. Details: install-log.txt next to this file.
  pause
)
goto :eof

:notextracted
echo.
echo  The ZIP is not extracted yet.
echo  Right-click GooseDeluxe-v0.1.zip -^> "Extract All...", then run this .bat
echo  from the extracted folder.
echo.
echo  (Snachala raspakuy ZIP: pravoy knopkoy -^> "Izvlech vse...")
echo.
pause
