# ORCA WORK ORDER — TWO FLEXIBLE CAMERAS + COMPACT INDUSTRIAL UI
Task: DATN-WPF-DUAL-TRANSPORT-20261022 | Approved by user 2026-10-22
Authoritative workspace: **E:\Orca\DATN ONLY**
Solution: E:\Orca\DATN\App\PlateVision50.sln | Project: E:\Orca\DATN\App\Vision\PlateVision50.csproj
Source: existing branch backup/datn-full-source-20261021, with UNCOMMITTED Orca modifications.
This replaces the Station02-only-USB portion of Docs/ORCA_STATION02_BASLER_USB3_ADDENDUM_20261022.md and the Station01-IP-only portion of the older station-interlock work order. Preserve all valid previous work.

## 0. Mandatory preparation
- Review AGENTS.md, Docs/ORCA_STATION_INTERLOCK_IMPLEMENTATION_RESULT_20261021.md, git status/diff and current camera architecture before editing. The previous interlock implementation already built with 0 errors and unit tests passed; preserve these regressions. No newly created DATN solution/project, no C: copies, no Git reset/checkout/stash.
- Back up each WPF source file before edit (with source SHA-256) to timestamped Docs/WPF_BACKUP_DUAL_CAMERA_20261022 and keep all existing uncommitted changes. Do NOT touch PLC/TwinCAT, Mitsubishi code, EtherCAT mappings, machine runtime, installer, network adapters or drivers.
- Record actual source classes: Dependencies/Devices/UnifiedCameraService.cs, Dependencies/BaslerCamera/BaslerCameraService.cs, HikCameraService/HikDirectIpCamera, CameraSession, MainWindow.xaml(.cs), StationOverview.cs, StationDashboards.cs, vision/trigger/FIFO pipelines.

## 1. Core change — BOTH STATIONS MUST SUPPORT USB AND GIGE/IP
In BOTH STATION 01 GANTRY and STATION 02 SCARA offer the SAME compact camera connection header:
  CAM | TYPE [AUTO / USB / IP] | DEVICE [Model - Serial - Vendor] | IP [only for IP mode] | SCAN | CONNECT | DISCONNECT | status
For USB mode: NO IP required or visible; SCAN enumerates compatible cameras via installed SDK(s), showing model, serial, transport and vendor; allow exact physical-device selection. Basler USB3 Vision using existing pylon USB/GenTL is priority. Do not claim generic USB webcam/UVC support unless a compatible image capture backend exists; display 'Unsupported USB device' with reason.
For IP mode: editable IPv4 / hostname (validate) plus local NIC selection only when necessary; discover GigE devices and connect using corresponding VENDOR SDK. Hikrobot GigE via existing Hik-specific path; Basler GigE via pylon (not HikDirectIpCamera). Do not assume any camera works by simply pinging its IP. AUTO scans ALL supported USB/GigE; select vendor+serial/address. Avoid incompatible transport dispatch.
Available controls identical on both stations, NOT hard-coded Station01 IP or Station02 USB. Persist per-station preferred transport/device and IP as settings (no credentials). Do not overwrite IP/device setting of other station. Empty/unreachable/not found -> clear concise error, not fake Connected.

## 2. Camera ownership, frames and state integrity
- Each station has its own camera session/service, selected device ID, connection lifecycle, frame-received handler and UI dispatch: Station01 => Cam01 viewport + Overview Cam01; Station02 => Cam02 viewport + Overview Cam02. Two concurrent streams must work; start/stop/reconnect/dispose of one CANNOT stop, blank, rename, or reroute the other.
- Prevent two slots from opening the same exclusive camera serial simultaneously: show 'In use by Station 01/02'. UI and backend must enforce ownership.
- States: OFFLINE, SCANNING, CONNECTING, CONNECTED/NO FRAME, LIVE (fresh frame), LOST/RECONNECTING, ERROR. Use actual frame timestamp and transport discovery; no fabricated frames, FPS, coordinates or 'LIVE' badges.
- Free event handlers/SDK objects on reconnect and disposal; robust async cancellation, timeouts, UI-thread marshaling, no duplicated frame loops or leaks. Safe unplug behavior: camera alone goes LOST, other camera unaffected; SCADA should never mistake stale frame for a current one.
- Preview can show either feed in each Station and in LINE OVERVIEW simultaneously. Preserve Camera01 product tracking, 50% FOV trigger line, per-product overlays, PLC read-only FIFO; do not route Camera02 frames through Camera01 analytics or robot commands accidentally. Camera02 vision analytics only if explicitly configured separately later, not an implicit robot enable.
- SCARA robot remains NOT COMMISSIONED. Camera02 acquisition can be LIVE while SCARA robot is disabled. Neither camera connection nor image acquisition may send PLC/motor commands.
- If USB hardware not connected, still fully enable SCAN (returns 0), show 'No USB camera', disable CONNECT with reason. AML already has Basler pylon 8.1/USB3 driver but no Basler device was detected on prior check; do NOT declare physical testing passed without device.

## 3. UI simplification — user explicitly says 'too many unnecessary words'
Simplify EVERY operational tab: LINE OVERVIEW, STATION 01, STATION 02, SHARED CONVEYOR, SETTINGS, ALARMS & LOGS. Keep established navy Siemens-style industrial identity, compact small plain buttons, dense layout maximizing viewport space.
- Short consistent labels ONLY: 'CAM 1', 'CAM 2', 'USB', 'IP', 'SCAN', 'CONNECT', 'DISCONNECT', 'LIVE', 'OFFLINE', 'READY', 'FAULT', 'START', 'STOP', 'RESET', 'SPEED (mm/s)', 'ACTUAL (mm/s)', 'ADS', 'E-STOP', 'PLC-Q', 'AUTO', 'MANUAL'.
- Remove explanatory prose, repeated section captions, duplicate statuses, huge headers, decorative subtitles and redundant footer paragraphs. Shift explanations to tooltips or a collapsed Diagnostics / Details panel. Only show one terse primary failure reason next to disabled START; full interlock checklist can be expandable and MUST remain accessible.
- Keep crucial real safety warnings always legible: NOT COMMISSIONED, E-STOP fault, PLC disconnected, NO FRAMES, stale data, and STOP not being safety-rated. Never hide/misrepresent dangerous states to reduce text.
- Keep two camera viewports predominant in LINE OVERVIEW and a large feed on each station. Place station camera controls in compact one-row/two-row bar above live view; align stations identically. Reclaim wasted vertical/horizontal padding without clipping at 1366x768 or 1649x928, scale fonts/buttons modestly (industrial readability > tiny font).
- Nav stays exactly 6 tabs: LINE OVERVIEW, STATION 01, STATION 02, SHARED CONVEYOR, SETTINGS, ALARMS & LOGS. Keep VISION WORKSPACE and CAMERA / SOURCE hidden from regular navigation, preserve advanced internal source diagnostics accessible from Settings where needed.
- Keep shared mm/s setpoint one source of truth between Overview and Conveyor; Hz displayed only if real measured calibration approved. Preserve compact LINE START/STOP/RESET and fail-closed TwinCAT interlocks, physical EStop independent.
- Remove hardcoded example text from production status, fake ONLINE and gratuitous multicolored/oversized START/STOP. Small restrained controls; convey severity with subtle color plus text.

## 4. Test, screenshots, acceptance and deliverables
- Implement in existing C#/XAML. Verify SDK variants supported: Basler USB3, Basler GigE, Hikrobot GigE; if Hikrobot USB not supported in existing installed SDK, DO NOT falsely mark as supported. Report model/transport matrix clearly in Settings or QA, not busy operational screen.
- Run full existing solution build Debug x64 and Release x64; run tests App/Tests/Tests.csproj; add tests for USB/IP transport selection per station, no IP required for USB, wrong vendor dispatch rejected, two independent sessions/frames, same camera double-claim rejected, disconnect/unplug isolation, error states, stale frame, no fake LIVE, no effect on interlocks, no motion commands, previous speed/interlock regressions.
- Review actual WPF visually at 1366x768 and 1649x928 using computer use / screenshot. Demonstrate Cam01 IP + Cam02 USB offline simulation AND Cam01 USB + Cam02 IP selection; do not invent physical video/connected hardware. Confirm small controls, no excessive text, no clipped warnings.
- Create versioned ReleaseCandidate (not overwrite active Release or START_DATN.cmd), save before/after screenshots and a new detailed report Docs/ORCA_DUAL_STATION_CAMERA_UI_RESULT_20261022.md listing exact changed files, backups, build/test logs, unsupported camera classes, real camera test status and any remaining blockers. No Git push until requested.
- KEEP PLC actuator commands disabled/fail-closed; NO TwinCAT Activate/Download/ADS write/servo enable/belt motion or camera driver install. Confirm commissioning not completed.
- Orca should actually implement and test, not stop at a proposal/mock. If SDK/runtime unavailable or competing file edits make modification unsafe, document a precise blocker rather than claim success.

## Acceptance summary (must verify, not assert)
1. Station01 supports supported USB and IP cameras with working SCAN/SELECT/CONNECT/DISCONNECT.
2. Station02 supports supported USB and IP cameras with the same controls (no more hardcoded disabled camera strip).
3. Cam1/Cam2 independently display simultaneous live frames in their Station and Overview viewports (hardware-dependent part only claimed when hardware is present).
4. Compact UI across every tab with far fewer visible explanatory words but clear truthful faults/safety information.
5. Existing interlock, speed mm/s and vision tests build/pass; no actual machine motion/PLC writes.
