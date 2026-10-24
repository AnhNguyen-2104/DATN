@echo off
setlocal
set "GD2=C:\Program Files (x86)\MELSOFT\GPPW2\GD2.exe"
set "PROJECT=%~dp0PLC\Mitsubishi\DATN_PLC.gxw"
if not exist "%PROJECT%" (
    echo Mitsubishi PLC project not found: "%PROJECT%"
    pause
    exit /b 1
)
if exist "%GD2%" (
    start "" "%GD2%" "%PROJECT%"
) else (
    start "" "%PROJECT%"
)
endlocal
