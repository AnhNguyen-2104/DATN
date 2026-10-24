@echo off
setlocal
set "DIR=%~dp0PLC\Mitsubishi"
if not exist "%DIR%" (
    echo Mitsubishi folder not found: "%DIR%"
    pause
    exit /b 1
)
explorer.exe "%DIR%"
endlocal
