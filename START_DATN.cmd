@echo off
setlocal
set "DATN_ROOT=%~dp0"
if not exist "%DATN_ROOT%Runtime\DotNet8\dotnet.exe" (
  echo DATN Runtime not found.
  pause
  exit /b 1
)
if not exist "%DATN_ROOT%Release\PlateVision50_V2.dll" (
  echo DATN Line Control build not found.
  pause
  exit /b 1
)
start "DATN LINE CONTROL" /D "%DATN_ROOT%Release" "%DATN_ROOT%Runtime\DotNet8\dotnet.exe" "%DATN_ROOT%Release\PlateVision50_V2.dll"
endlocal
