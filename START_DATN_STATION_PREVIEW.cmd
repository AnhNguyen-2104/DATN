@echo off
rem DATN single-screen station preview launcher.
rem Starts the verified single-screen release candidate only.
rem It never writes ADS tags, never energizes the conveyor and never commands a robot.
rem Runtime resolution: workspace embedded .NET 8 first, otherwise the system dotnet on PATH.
setlocal
set "ROOT=%~dp0"
set "APPDIR=%ROOT%ReleaseCandidate_COMPACT_SCADA_20261021"
set "EXE=%APPDIR%\PlateVision50_V2.exe"
set "DLL=%APPDIR%\PlateVision50_V2.dll"
set "DOTNET=%ROOT%Runtime\DotNet8\dotnet.exe"

if exist "%EXE%" (
  pushd "%APPDIR%"
  start "DATN Station Dashboard Preview" "%EXE%" %*
  popd
  endlocal
  exit /b 0
)

if not exist "%DLL%" (
  echo Station dashboard preview not found: %DLL%
  pause
  exit /b 1
)
if not exist "%DOTNET%" set "DOTNET=dotnet"
where %DOTNET% >nul 2>nul
if errorlevel 1 (
  echo .NET 8 runtime not found. Install the .NET 8 desktop runtime or restore Runtime\DotNet8.
  pause
  exit /b 1
)
pushd "%APPDIR%"
start "DATN Station Dashboard Preview" "%DOTNET%" "PlateVision50_V2.dll" %*
popd
endlocal
