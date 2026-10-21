@echo off
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%ReleaseCandidate_OVERVIEW_MODE_20261021\PlateVision50_V2.exe"
if not exist "%EXE%" (
  echo [DATN] Preview executable is missing. Build the Release Candidate first.
  pause
  exit /b 1
)
tasklist /FI "IMAGENAME eq PlateVision50_V2.exe" /NH | findstr /I "PlateVision50_V2.exe" >nul
if not errorlevel 1 (
  echo [DATN] An existing WPF DATN instance is active.
  echo Close the existing UI normally when safe before launching this preview.
  echo No process will be killed or restarted automatically.
  pause
  exit /b 2
)
echo [DATN] Starting offline-design Release Candidate.
echo [DATN] PLC motion commands remain disarmed and uncommissioned.
start "DATN Overview Mode Candidate" "%EXE%"
