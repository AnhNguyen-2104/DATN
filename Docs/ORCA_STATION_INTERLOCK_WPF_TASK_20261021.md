# DATN WPF — ORCA IMPLEMENTATION CONTRACT: STATION INTERLOCK + CAMERA AT STATIONS
Task ID: DATN-WPF-STATION-INTERLOCK-20261021
Approval: user has approved implementing the previously accepted SCADA/HTML preview (not approval for field commissioning, PLC online writes, or TwinCAT changes).
Target machine: AML-IPC. Orca currently seen in session working at E:\Orca\DATN (photo: 2026-10-21 around 23:35).
Status: READY FOR ORCA TO IMPLEMENT; this file is task specification, not a claim that any change has been made.

## FIRST: resolve exact working copy; prevent loss of other agents' work
1. **AUTHORITATIVE USER CORRECTION (2026-10-21): THE ONE AND ONLY ACTIVE WORKSPACE FOR THIS TASK IS E:\Orca\DATN.** Set CWD E:\Orca\DATN and only edit files under this root. Exact WPF solution E:\Orca\DATN\App\PlateVision50.sln; source project E:\Orca\DATN\App\Vision\PlateVision50.csproj. Historical AGENTS.md references to C:\Users\aml\orca\DATN or Desktop\DATN are superseded FOR THIS TASK by this explicit user correction. Other copies are not active; do not edit, copy, merge or sync with C:. Validate this working root before editing, otherwise STOP and report.
2. Current E: git branch backup/datn-full-source-20261021 already has UNCOMMITTED modifications: App/PlateVision50.sln, PLC/TwinCAT/DATN.project.~u staged, and PLC/TwinCAT/DATN/DATN.tsproj modified, plus Docs/BUILD_FIX_20261021. Do not reset, checkout, clean, stash, overwrite or commit these other modifications. This task's scope is WPF only. Orca must read AGENTS.md, README.md, PROJECT_INDEX.md, Docs/TAG_MATRIX.md, Docs/PLC_README_ESTOP.md before modifying source.
3. Existing C: WPF work (per prior reports) includes camera-first overview / AUTO-MANUAL, read-only status, and PC-only START/STOP VISION; don't replace it blindly or duplicate it. Identify which features currently exist in E: and preserve behavior.
4. Make **restorable backup of every WPF file before edit** and record hashes, changed paths and rollback location. Avoid editing active Release/Runtime or TwinCAT files. No new solution/project.

## ACCEPTED DESIGN — user-facing layout
Use screenshot accepted in chat 2026-10-21 (STATION 01 screenshot with camera IP, compact line interlock and 2-column operational concept) and prior HTML Station Interlock preview as visual reference, **not** as evidence of PLC/live sensor behavior. User's earlier constraint: professional industrial Siemens-like navy/steel UI, tightly packed panels, maximize useful area; avoid large empty space, oversized/fancy/gradient buttons, decorative colors. Existing DATN visual identity remains.

### Navigation: exactly these operational tabs in main toolbar
LINE OVERVIEW; STATION 01 (GANTRY); STATION 02 (SCARA); SHARED CONVEYOR; SETTINGS; ALARMS & LOGS.
Remove **VISIBLE** VISION WORKSPACE and CAMERA / SOURCE tabs from normal navigation. Preserve existing internal camera SDK, live image detection, trigger line, product tracking, FIFO, ACK, settings logic and diagnostics; relocate needed controls, DO NOT delete underlying working functionality.
Do not introduce a duplicate project or separate vision workspace.

### STATION 01 (Gantry) — one-stop camera preparation
Within Station 01 top strip: labeled editable CAMERA 01 IP (e.g. user-entered Hikrobot/Basler IP, not hard-coded 192.168.1.100), optional port/device selection only if SDK requires it; small CONNECT / DISCONNECT, clearly truthful CONNECTING/CONNECTED/OFFLINE/NO FRAMES states, reconnect/failure messages. Reuse existing connection/disconnection code/event pipeline; do not invent a new camera driver.
Large Camera 01 live viewport takes majority of Station 01; preserve current overlay attached to correct tracked product: track ID, center X/Y in mm when calibrated, angle theta, 50% FOV trigger line, quality/queue info; no fake coordinates or demo video in real mode.
Gantry robot/PLC-Q status and relevant sensors to right/below, compact. Camera connect must NOT enable robot motion or belt by itself.
Vision tracking must be capable of automatic activation via line AUTO request once commissioned; preserve PC-only existing controls only as internal/maintenance pathway. No duplicate competing tracking ownership.

### STATION 02 (SCARA) — future ready only
Same camera IP+CONNECT/DISCONNECT placement for Camera 02 using installed SDK only if hardware exists. If not commissioned, show NOT INSTALLED/NOT COMMISSIONED and keep commands disabled; never show fake online/live camera. Separate SCARA state, no reuse Gantry M1000 path. SCARA not commissioned is explicitly EXCLUDED from required interlock for Gantry-only operating phase.

### LINE OVERVIEW — operating dashboard
Keep TWO camera panels, CAM 01 / GANTRY and CAM 02 / SCARA, each visible; camera 1 currently primary installed, camera 2 RESERVED; no fake frames. Compact status bands beneath.
Bottom / adjacent consolidated **SHARED CONVEYOR + MACHINE COMMANDS** group:
- Editable preset conveyor speed in mm/s with clear range validation, SET/Apply only when safe; display actual measured speed only from verified encoder data; display derived Hz **as information only** if calibration factor confirmed (never assert 120 mm/s == 18 Hz without measured calibrated ratio).
- Compact, plain START, STOP, RESET buttons (not giant filled neon buttons) and interlock checklist with exact failure reasons. No secondary large command bank.
- Start disabled/greyed while any necessary condition UNKNOWN, stale, disconnected, unsafe, or uncommissioned; show distinct statuses: NOT READY, READY TO REQUEST, START REQUESTED/ACK PENDING, RUNNING ACKNOWLEDGED, STOP REQUESTED, FAULT, OFFLINE.
- Read-only observed prerequisites: ADS port 851 online/fresh; hardware EStop auxiliary feedback healthy (display only, not safety-rated); Camera 1 CONNECTED **AND FRAME FRESH**; PLC-Q communication and fault/ready verified via actual existing symbol; validated conveyor speed calibration + physical chain; TwinCAT master global command handshake compiled, deployed and commissioned. PLC-Q M1001 or similar name is NOT trustworthy until tag mapping is verified.
- Incomplete/uncommissioned SCARA shown warning but does not disable Gantry-only AUTO, as long as configuration explicitly excludes it.
- Distinguish ADS live from actual machine safety; show error source/last timestamp so operator knows which prerequisite blocks start.

### START / STOP / RESET semantic contract
Operator prepares Camera 1 by IP CONNECT at Station 01 and sets preset conveyor speed mm/s on LINE OVERVIEW or SHARED CONVEYOR (one shared source of truth, not two independent values). After full verified commissioning, a **single LINE OVERVIEW START** should request the TwinCAT master to start automatic line: PC vision tracking + shared conveyor at the PRESET speed; do not ask for an additional speed value or use fixed Hz. STOP requests normal coordinated stop of conveyor + stops PC tracking, with timeout/error/ACK states. RESET requests fault reset only when stop/no hazardous motion verified. Normal HMI STOP IS NOT emergency stop; keep physical EStop independent.
**IMPORTANT CURRENT REALITY: PLC global command handshake and mm/s conversion calibration are NOT VERIFIED COMMISSIONED. Do not wire live START/STOP/RESET or MD200 output! Leave global start/belt write commands disabled/fail-closed in actual runtime.** Implement UI state model + ready display based on real READ-ONLY signals, clear pending commissioning; add a test-only injected mock/state provider for offline preview/tests, never confuse mock with ADS. Existing PC-only START/STOP VISION can remain available under explicit separate limited scope if safe; do not label that as full-line START.
Never auto-start conveyor simply because camera connects or all indicators become green: ONLY by operator clicking START after all prerequisites and commissioning are verified. On loss of signal/timeouts, fail closed, remove ability to START, and do not claim hardware can safely stop based solely on HMI.

### SHARED CONVEYOR
Reflect the SAME preset mm/s and actual feedback as LINE OVERVIEW; preserve manual controls in MANUAL only. AUTO is default and disarms manual HMI. If MANUAL selected, explicit operator confirmation; no implicit enable/servo/run. STOP/RESET distinction kept. Control authority belongs to TwinCAT, NOT a direct PLC-Q command in WPF.
Do not change TwinCAT ST, ADS tag names, PLC-Q project or I/O maps under this WPF task.

## Implementation steps Orca should perform
A. Inspect git diff and current WPF source to avoid clobbering ongoing build-fix work: App/Vision/MainWindow.xaml, MainWindow.xaml.cs, StationDashboards.cs, StationOverview.cs, Dashboard.cs, camera/source classes, tracking and FIFO/ADS controller. Identify actual controls/classes and current route of camera connect, preview mode.
B. Make per-file backup and source plan; preserve existing reliable Hik/Basler device enumeration, camera selection and image-only video testing. Small scoped edits. No markup-only fake connected UI. Keep Station 01 and STATION 02 camera panes balanced and LINE OVERVIEW dual camera.
C. Add robust station-level camera config binding and connection states; move/hide obsolete navigation but do not remove downstream source-picker functionality required for local video QA (put advanced source options under station Settings if needed).
D. Implement compact shared interlock checklist, preset speed mm/s binding/validation and safe state machine; no real hardware writes. Explicit user-facing reason for disabled Start. Preserve AUTO/MANUAL mode fail-closed.
E. Build **existing** App/PlateVision50.sln with .NET/WPF offline. Address relevant compiler errors; distinguish inherited vendor SDK warnings. Reuse approved test suite; add tests for interlock fail-closed, stale/no camera frames, ADS disconnect, PLC-Q fault, invalid speed, SCARA excluded, AUTO→MANUAL disarm, STOP tracking, safety descriptions.
F. Produce versioned ReleaseCandidate only; do not overwrite Release or START_DATN.cmd. UI QA visually using available computer-use or screenshot for 1366x768 and ~1649x928 where possible; no camera SDK/network reconfiguration, servo, PLC, real belt testing. Compare actual UI with user-approved preview; check no clipping, small buttons, no excess gaps.
G. Report in Docs/ORCA_STATION_INTERLOCK_IMPLEMENTATION_RESULT_20261021.md: exact files changed, backups, build/tests results, screenshot preview artifacts/paths, live commissioning blockers. Make a concise user-visible final update in Orca session. NO git push, NO PLC commands, NO runtime deploy.

## Critical acceptance requirements (demonstrate each)
1. No VISION WORKSPACE / CAMERA / SOURCE in normal tab navigation; Station 01 has IP+CONNECT and camera live view; Station 02 reserved truthful.
2. TWO camera panels at LINE OVERVIEW; Cam01 primary, Cam02 reserved.
3. Same speed preset mm/s visible in Overview and Shared Conveyor; no unverified frequency conversion and no forced 120 mm/s setpoint.
4. One compact START/STOP/RESET region; START disabled with explicit reason until true PLC commissioning; offline mock may demonstrate ready/run but is conspicuously TEST ONLY.
5. TRACKING and conveyor requested together in the commissioned control specification; PC vision should not misrepresent a belt-start success while actual PLC handshake missing.
6. AUTO default, MANUAL never auto-arms legacy tags, physical EStop untouched.
7. Build/test proof and GUI screenshot review before claiming complete; no access to live machine actuation.
