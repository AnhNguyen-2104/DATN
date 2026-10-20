@echo off
setlocal
set "DATN_ROOT=%~dp0"
set "CANDIDATE=%DATN_ROOT%ReleaseCandidate\Dashboard_20261020\PlateVision50_V2.dll"
if not exist "%CANDIDATE%" (
  echo Dashboard preview not built.
  pause
  exit /b 1
)
start "DATN DASHBOARD PREVIEW" /D "%DATN_ROOT%ReleaseCandidate\Dashboard_20261020" "%DATN_ROOT%Runtime\DotNet8\dotnet.exe" "%CANDIDATE%"
endlocal