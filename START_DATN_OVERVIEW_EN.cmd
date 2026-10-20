@echo off
setlocal
set "ROOT=%~dp0"
set "APP=%ROOT%ReleaseCandidate\OverviewLargeCameras_20261020\PlateVision50_V2.dll"
if not exist "%APP%" (
  echo The camera/I-O English preview has not been built: "%APP%"
  exit /b 1
)
where dotnet.exe >nul 2>nul
if errorlevel 1 (
  echo .NET runtime not found. Install .NET 8 Desktop Runtime.
  exit /b 1
)
start "DATN English Overview Preview" /D "%ROOT%ReleaseCandidate\OverviewLargeCameras_20261020" dotnet.exe "%APP%" %*
endlocal
