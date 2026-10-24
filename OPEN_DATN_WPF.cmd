@echo off
setlocal
set "SOLUTION=%~dp0App\DATN_WPF.sln"
if not exist "%SOLUTION%" (
    echo WPF solution not found: "%SOLUTION%"
    pause
    exit /b 1
)
start "" "%SOLUTION%"
endlocal
