# DATN WPF — Orca continuation handoff (2026-10-21 20:xx, AML-IPC)

## Objective
Complete and verify the user's requested WPF line-operation UX in the existing Orca **WPF** session. Line Overview should be the routine operating screen, Station 01/02 should be camera-first, and direct tag/manual controls may be exposed only in MANUAL mode. No actual machine actuation is authorized.

## Current evidence and completed work — DO NOT repeat
Read `AGENTS.md` in C:\Users\aml\orca\DATN and the checkpoint `Docs\ANGENT_OVERVIEW_AUTO_MANUAL_CAMERA_CHECKPOINT_20261021.md` first.
- Active source is C:\Users\aml\orca\DATN; never use older Desktop\DATN as the active source.
- Four WPF source files already edited: App\Vision\MainWindow.xaml, StationOverview.cs, StationDashboards.cs, Dashboard.cs.
- Camera 1/2 take majority of Station dashboards; overview has PC-only START/STOP VISION; MANUAL toggle defaults to AUTO and always disarms legacy HMI output on transition; Conveyor Manual Controls hidden while AUTO.
- Global START/STOP/RESET and mm/s→Hz SET/START BELT remain LOCKED pending PLC command contract and validated speed mapping.
- Backup already exists at Docs\BACKUP_ANGENT_OVERVIEW_MODE_20261021-174855 (manifest), and later StationOverview incremental backup.
- RC built successfully at ReleaseCandidate_OVERVIEW_MODE_20261021\PlateVision50_V2.exe: last build 0 errors / 11 warnings in vendor camera wrappers. Offline App/Tests reported TOTAL_TEST_FAILURES=0. Static UI verification script returned 20/20 PASS.
- There is a safe preview launcher START_DATN_OVERVIEW_MODE_PREVIEW.cmd.
- Prior live WPF Debug PID 28008 was running at 18:01, but at 20:09 `Get-Process PlateVision50_V2` returned none: recheck live state immediately before preview.
- Nothing was deployed to PLC/TwinCAT or active Release. Preserve these facts and all uncommitted edits.

## Tasks in order
1. Read AGENTS.md, referenced checkpoint, check log/PID/current files/git status. Detect if a previous QA or running preview is already underway and continue from its next incomplete step; do not replay completed code edits/tests.
2. With PC-only/offline-safe controls, start the existing ReleaseCandidate preview only if no running WPF and if safety preconditions hold. Use Computer Use / actual screenshot inspection to verify layout at 1366x768 and 1649x928 (or nearest available resolution without changing unrelated machine settings): camera-first Station 01 and 02, balanced panels, text not clipped, buttons visible, overview only for normal operation. Document evidence/limitations.
3. Inspect AUTO/MANUAL transitions in the UI without triggering machine commands: AUTO hides manual control panel; MANUAL exposes details after confirmation but does not arm by itself; return AUTO disarms. Confirm START/STOP VISION state responds properly to source/no source; do NOT disconnect/reconfigure camera or start equipment just to test.
4. Fix only confirmed WPF visual/logic defects with per-file backup, and rebuild/test offline as necessary in versioned RC. Do not overwrite active Release or alter live project launch.
5. Write progress/result to Docs with file list, tests, screenshots/evidence (if obtainable), deficiencies, and exact remaining blocker. Tell user when ready to review.

## Strict safety and authority
No ADS/PLC/MC writes, no TwinCAT Run/Stop/activation/download, no robot/servo/belt movement, no physical I/O tests, no camera IP/network changes, no hardware E-Stop manipulation. Do not enable/commission real START/STOP. Disabled controls must remain disabled until onsite authorized commissioning and verified physical safety wiring. Do not claim HMI STOP is safety-rated. Do not fake SCARA/camera data. No git push, destructive cleanups, duplicate Orca session or unrequested background service.
An executable WPF preview may poll existing read-only ADS; do not test physical commands. If runtime launch could have unintended actuation, stop and report the blocker.

## Acceptance
Actual UI inspection plus build/test evidence; preserve existing preview code and camera overlay; a clear status distinguishing software-ready UI and hardware commissioning blocked. No duplicate work and no silent modifications to controller.
