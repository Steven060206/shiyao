@echo off
setlocal
if not exist "%~dp0app\qq-sync.json" (
    echo Please run Setup.cmd before starting Shiyao.
    pause
    exit /b 1
)
if not exist "%~dp0app\MessagePurify.exe" (
    echo MessagePurify.exe is missing. Extract the complete download again.
    pause
    exit /b 1
)
start "" /D "%~dp0app" "%~dp0app\MessagePurify.exe"
exit /b %ERRORLEVEL%
