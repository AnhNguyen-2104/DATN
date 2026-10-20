@echo off
setlocal
set "XAE=C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe"
set "SOLUTION=%~dp0PLC\TwinCAT\DATN.sln"
if not exist "%SOLUTION%" (
 echo TwinCAT solution not found: "%SOLUTION%"
 pause
 exit /b 1
)
if exist "%XAE%" (
 start "" "%XAE%" "%SOLUTION%"
) else (
 start "" "%SOLUTION%"
)
endlocal
