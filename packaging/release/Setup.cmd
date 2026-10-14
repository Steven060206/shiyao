@echo off
setlocal
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" %*
set "setupExit=%ERRORLEVEL%"
echo.
pause
exit /b %setupExit%
