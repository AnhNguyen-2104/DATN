@echo off
setlocal
set "DIR=%~dp0PLC\Mitsubishi\Sources_20261019\GXWorks2\Candidates"
if not exist "%DIR%" (
 echo Mitsubishi files not found.
 pause
 exit /b 1
)
explorer.exe "%DIR%"
endlocal
