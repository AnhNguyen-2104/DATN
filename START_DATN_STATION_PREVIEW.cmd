@echo off
setlocal
set "ROOT=%~dp0"
set "APP=%ROOT%ReleaseCandidate\StationOverview_SensorLog_20261020\PlateVision50_V2.dll"
set "DOTNET=%ROOT%Runtime\DotNet8\dotnet.exe"
if not exist "%APP%" (
  echo Station dashboard preview not found: %APP%
  pause
  exit /b 1
)
if not exist "%DOTNET%" (
  echo .NET 8 runtime not found.
  pause
  exit /b 1
)
start "DATN Station Dashboard Preview" /D "%ROOT%ReleaseCandidate\StationOverview_SensorLog_20261020" "%DOTNET%" "%APP%" %*
endlocal
