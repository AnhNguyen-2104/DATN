# ORCA HANDOFF — WPF SESSION — DATN SINGLE-SCREEN STATION HMI
Date: 2026-10-20
Task ID: DATN-WPF-SINGLESCREEN-20261020
Agent: Orca, CURRENT existing session named **WPF** on AML-IPC
Workspace (ONLY): C:\Users\aml\orca\DATN
Solution: App\PlateVision50.sln
Application project: App\Vision\PlateVision50.csproj
Status: HANDOFF PREPARED — Orca must mark STARTED on receipt. This task is not completed merely because it was handed off.

## Objective
Implement the user-approved **English-only single-screen Station 01 and Station 02 industrial HMI** in the EXISTING C#/.NET 8 WPF application. The earlier HTML concept is a VISUAL REFERENCE, not a replacement for WPF. Do not deliver another description-only placeholder page. Preserve working camera and PLC monitoring integrations.

## Read before touching any code
1. Read AGENTS.md, README.md, PROJECT_INDEX.md, Docs\TAG_MATRIX.md and Docs\PLC_README_ESTOP.md.
2. Read Docs\LARGE_CAMERA_OVERVIEW_CHECKPOINT_20261020.md, Docs\DEBUG_F5_REPAIR_CHECKPOINT_20261020.md.
3. Check git status, active running PIDs/solutions, recent build logs, checkpoint files and any other Orca agents writing this workspace. Respect existing user changes and avoid overlapping edits.
4. Inspect actual WPF UI implementation in App\Vision\MainWindow.xaml, MainWindow.xaml.cs, Dashboard.cs, StationOverview.cs, and camera/ADS helpers. DO NOT infer bindings without reading source.
5. Do not copy/overwrite from C:\Users\aml\Desktop\DATN; it is not the active workspace.

## Functional UI contract
Navigation (English): LINE OVERVIEW | STATION 01 | STATION 02 | SHARED CONVEYOR | SETTINGS | ALARMS & LOGS. Preserve existing useful camera/vision functions; organize them under appropriate pages rather than deleting them. Read-only state shown should indicate LIVE/STALE/OFFLINE/NO DATA honestly.

### LINE OVERVIEW
- Two equal side-by-side station cards in upper portion: Station 01 / Gantry and Station 02 / SCARA.
- Each card owns its OWN PLC/controller connection state, camera live panel/status, robot/drive/mode/busy/fault status, Robot actual TCP X/Y/Z/Rz, local product track ID/vision pose and compact local sensors (operator labels only).
- Distinguish **robot TCP in machine/robot frame** from **camera-measured product X/Y/theta**; never use camera pose as fake TCP.
- SCARA has no validated live PLC, Camera 02 or robot data. Show NOT COMMISSIONED, UNMAPPED or NO DATA; no fabricated connected/ready/position numbers.
- SHARED CONVEYOR panel appears BELOW the two station cards, NOT above them. Show Conveyor Run Status, measured speed (mm/s), encoder distance/position and command state if truly available.
- Overview must not contain raw PLC symbol names, EtherCAT terminal IDs, module names, binary flags, or user-facing MD200 drive registration. Display "Conveyor Speed", "Run Status", "Encoder Distance", not "write Hz to MD200".
- Compact global alarm bar/status at bottom. Global START/STOP remains informational / NOT COMMISSIONED; HMI E-STOP button is never represented as a certified emergency stop.

### STATION 01 / GANTRY (real detail screen, NOT a placeholder)
- Single-screen dashboard with no MAIN VERTICAL PAGE SCROLL at 1366x768 and 1649x928 desktop sizes. Left navigation and title compact; all high-priority data visible without wheel scroll.
- Camera 01 preview is a dominant panel (~40% horizontal width on desktop, preserve aspect ratio and current live-source + detection overlay). Do NOT create a second camera connection, alter camera IP or fabricate frames.
- Neighboring Gantry / Robot Telemetry: controller link quality, robot ready, servo state, robot mode, busy/fault, actual TCP X, Y, Z (mm), Rz (deg), homed/reference state. If actual TCP signals are NOT proven mapped, show NO DATA / UNMAPPED, NOT zeros or measured product coordinates.
- Product Tracking: Track ID, product center X/Y/theta, detect status, confidence, frame quality/FPS if available, queue count, 4-point glue sequence / current vertex and dispense-cycle status.
- Station Sensors & I/O: offer a compact searchable/grouped view of up to 18 relevant operator-labeled signals (e.g. product present, camera trigger, home, positive/negative limits, glue pressure, glue level, vacuum, safety door indication, local start/stop/reset, valve feedback, alarm acknowledge); classify each as LIVE / NO DATA / UNMAPPED; only expose as LIVE if real symbol exists and reads successfully. Never invent PLC tags or presume physical sensor wiring.
- Process & Vision Parameters: up to 12 operator-relevant readouts (recipe, dot count, glue spacing/pitch, delay, speed, station vision offset, calibration status, conveyor sync). Use real values only, else "NOT CONFIGURED".
- Local Alarm / Events: compact critical latest event strip; detailed logs via in-panel tab/expansion and Alarms page.
- Button to Station 01 Settings. No robot motion/JOG/valve test changes.

### STATION 02 / SCARA
- SAME structure and density as Station 01: Camera 02, controller, robot ready/mode/servo/busy/fault, actual TCP X/Y/Z/Rz, pose/tracking, sensors & I/O, parameters, alarms.
- Sensors: tray present, gripper open/closed, part picked/released, vacuum, home, axis limits, queue acknowledgment, local reset/alarms — only when implemented/verified. Otherwise clearly UNMAPPED/NOT COMMISSIONED.
- Parameters: pick/place height, approach height, pick/place delay, gripper timing, tray recipe, robot speed override, camera calibration.
- No offline placeholders masquerading as LIVE. Do not enable SCARA commands.

### SHARED CONVEYOR page
- Source of tracking for both stations. Show line speed in mm/s, encoder position, run status, station synchronization as available.
- Any existing HMI conveyor START/STOP writes must remain DISARMED by default and require existing operator authorization; normal STOP is NOT hardware E-stop. Do not call or test these controls.

### SETTINGS page
- Central Settings with distinct sections: SYSTEM, STATION 01 / GANTRY, STATION 02 / SCARA, SHARED CONVEYOR, I/O MONITORING, USER / PERMISSIONS.
- Separate per-station camera options, vision calibration/ROI/trigger, robot monitoring and process/recipe configuration. Do not visually mix station settings.
- Settings show current values, units, validity/read-only/access level and an explicit edit/apply UX where permitted. No new ADS writes or new robot permissions.
- Display operator-friendly names. Technical ADS symbol mapping only on engineering diagnostics/configuration surfaces, not main operator UI.

## Screen-fit and visual quality
- Keep existing dark industrial brand palette and English labels. Information density must remain readable (do not shrink to microscopic fonts).
- Both Station pages must fit without main vertical scroll at 1366x768 AND 1649x928 (account for Windows titlebar/taskbar); if all 18 sensors + 12 params cannot fit simultaneously, make internal grouped SENSOR/PARAMETER switches or compact panels within the available area. All groups reachable with clicks, no unbounded page length. No horizontal scroll on desktop.
- Camera must be visually dominant in its station view but not hide TCP/IO states.
- No overlap, clipping, empty detail-page placeholders, or truncated core status. Verify font readability, responsive resizing and focus keyboard navigation.
- Ensure line Overview always places Conveyor BELOW BOTH Station cards.

## Implementation rules / protection
- YOU MAY edit C# WPF XAML, code-behind, read-only view models and tests only within active Git workspace.
- Before each edit: create restorable file backup (versioned under Docs), preserve git untracked files. Do not delete existing camera functionality, trigger pipeline, track-ID, image-processing logic, or raw ADS diagnostic pages.
- Read and reuse existing ADS read bindings and data quality. Never create imaginary live PLC symbols; add nullable/not-configured UI models as needed.
- No PLC project changes, TwinCAT Activate/Download, ADS writes/tests, simulated E-stop actuation, conveyor movement, robot JOG, servo enable, camera network changes, device reconfiguration or connection to physical robot for QA.
- Never overwrite Release/ or current running binaries. Build into unique ReleaseCandidate folder, use non-hardware preview launch path. Source-only changes permitted.
- Do not push to GitHub until you have a verified build and visual checks. Do not auto-push without the user's separate instruction for this task.

## Milestones (checkpoint after each, no redoing completed steps)
M0. Recovery + baseline: read above files, Git status, process/PID/log and backup, note exact current solution.
M1. UI architecture: map existing named controls, ADS bindings and station objects; detail any unimplemented TCP/sensors.
M2. Implement Overview / 2 station detail screens and Settings in WPF, preserve camera and ADS logic.
M3. Compile Debug + Release offline, run non-actuating test suite; fix errors, record warnings.
M4. Launch separate preview; use actual desktop screenshot/Computer Use if available. Inspect Station 01 and Station 02 at 1366x768 and 1649x928, switching sensor groups/settings. If computer-use unavailable, use Windows UI Automation/screenshots and explicitly disclose limitation. NO hardware button tests.
M5. Write Docs\ORCA_WPF_SINGLESCREEN_RESULT_20261020.md including changed files, builds, screenshots, feature completeness, remaining UNMAPPED signals, errors, safety invariants and exact launcher. Mark verified DONE only after acceptance checks.

## First response Orca must send in WPF session
"Acknowledged WPF task DATN-WPF-SINGLESCREEN-20261020. I will work ONLY in C:\Users\aml\orca\DATN. I will read AGENTS and checkpoints, inspect Git/PIDs, preserve backups, then implement the English two-station single-screen WPF UI without PLC or hardware operations. I will report M0/M1 before major edits."

## Strict coordination
ChatGPT delegated in this existing **WPF** Orca session. Do not spawn more sessions unnecessarily. Keep progress visible here; if interrupted, consult Docs checkpoint and resume from first unfinished milestone, never repeat destructive actions.
