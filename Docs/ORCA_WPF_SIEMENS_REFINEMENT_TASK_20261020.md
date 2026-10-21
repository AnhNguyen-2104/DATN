# DATN — Siemens-inspired SCADA WPF refinement (Orca WPF session)
Date: 2026-10-20
Task ID: DATN-WPF-SIEMENS-REFINEMENT-20261020
Workspace: C:\Users\aml\orca\DATN
Target: the existing single-screen WPF DATN app, NOT a new app or new Orca session
Design decision: The user approved the HTML preview "DATN_Siemens_SCADA_Interactive_Preview.html" shown in ChatGPT and now authorizes its translation into real WPF. It is Siemens WinCC Unified-INSPIRED, without Siemens brand/logos, not an exact clone.

## FIRST: read and reconcile (M0)
- Read AGENTS.md, README.md, PROJECT_INDEX.md, Docs/TAG_MATRIX.md and Docs/PLC_README_ESTOP.md, the most recent checkpoints and Docs/ORCA_WPF_SINGLESCREEN_RESULT_20261020.md.
- Inspect Git status/diff, process IDs, currently open WPF and Visual Studio, previous Orca session log, release-candidate folder and screenshots.
- The previous single-screen M0-M5 is reported as DONE, with 47/47 tests and screen captures. Do NOT repeat its finished work or rebuild from scratch.
- The Git working tree has UNCOMMITTED WPF edits plus an unrelated modified Mitsubishi GX Works2 .gxw. DO NOT DISCARD, RESET, STASH, OVERWRITE OR MODIFY THAT PLC FILE. Preserve all untracked backups and files.
- Back up every WPF source file before edits to a NEW versioned Docs backup folder; log edited paths and hashes.
- If another worker is modifying same WPF source, coordinate or wait; do not edit concurrently.
- Determine whether an actual Siemens HTML reference is available locally; if not, use the precise design contract below and ask for reference only if indispensable. Do not claim file was opened if absent.

## PURPOSE
Visual redesign of CURRENT implementation from dense tiny-text dashboards to a polished, clear, Siemens-inspired industrial SCADA operator view; preserve all existing live camera/vision, TwinCAT read-only and conveyor-control authorization logic. The finished product MUST be English and accurate about commissioning.

## Exact visual language from accepted HTML
CSS-like design tokens adapted into WPF resource brushes:
background #07121E; navigation #0B1B2B; header #101E2E; primary panel #112335; secondary panel #0D1C2B; borders #30465B and #23384D; main text #E8F1FC; secondary text #92AABE; cyan #27C9EC; teal #39C7A2; warning amber #F4BB45; fault red #FF6E70; corner radii ~6px; typography Segoe UI; subtle flat separators, no excessive gradients/shadows. Use status color meaning consistently and color-blind-friendly labels.
Industrial layout discipline: 68px top header, compact left navigation, 8-14px card gaps, consistent section titles, alignment grid, no decorative empty panels. Screen should be visually polished and legible from working distance; body labels not microscopic. No Siemens trademarks/logos.
Do NOT implement HTML as WebView; reproduce the accepted visual design in native WPF XAML.

## Screens and interaction contract (M1-M2)
1) LINE OVERVIEW: two balanced station cards SIDE-BY-SIDE above a single SHARED CONVEYOR strip spanning full width beneath BOTH stations. Station 01/gantry and Station 02/SCARA each show dedicated controller health, live-own-camera area, robot ready/mode/fault, actual TCP X/Y/Z/Rz, camera-measured product pose separately, local sensor health and quick navigation. Shared Conveyor operator labels = Run Status, Measured Speed, Encoder Distance, Sync State; no raw tag/module/drive identifiers. Global alarm/status bar near footer. Never present HMI E-stop as certified safety.
2) STATION 01 / GANTRY: detail fits an entire visible page at 1366x768 and 1649x928 without outer vertical scrolling. Camera 01 is the dominant left-hand panel. Beside camera, balanced robot/PLC telemetry (link, mode, servo, ready, busy, fault), explicit ACTUAL TCP position (X,Y,Z,Rz, robot frame; NOT MAPPED when unavailable), product Track ID/vision X/Y/theta, queue, dispensing cycle. Lower pane contains grouped searchable Sensors & I/O (preserve the existing 25 station signals: 10 verified read-only and remainder unavailable), Process Parameters (12), and Local Events, switched by *functional* tabs instead of long lists. One clear Station 01 Settings entry. Preserve the live Camera 01 frame/overlay/trigger pipeline; don't replace real camera image with static design reference.
3) STATION 02 / SCARA: same visual system, large Camera 02 panel, its own PLC/robot/TCP/sensors/parameters, no invented telemetry. Camera2/robot/tray/gripper still NOT COMMISSIONED or UNMAPPED until mapped. Same grouped in-panel tabs, same no-outer-scroll requirement.
4) SHARED CONVEYOR: clear motor/run state, speed mm/s and encoder, separate accessible details. The existing disarmed-by-default Start/Stop permissions and protections MUST remain UNCHANGED. No hardware command testing.
5) SETTINGS: centralized page with real tabs System, Station 01, Station 02, Shared Conveyor, I/O Monitoring, User/Permissions. Each Station gets OWN vision, robot, recipe and local I/O configuration sections; only existing authorized edits may operate, and do not introduce new ADS writes. Operator screens NEVER display raw ADS tags, MD200, terminal names or physical PLC addresses. Diagnostics may keep raw symbols for engineering only.
6) ALARMS & LOGS + VISION WORKSPACE/CAMERA SOURCE remain fully functional; no removed existing features.

## UI quality and feedback (M3-M4)
- Preserve all existing named WPF bindings, tab navigation, camera overlays, ADS quality checks, station and tracking states. TCP is not camera pose and cannot be inferred from it.
- No fabricated LIVE data. Continue handling STALE/UNAVAILABLE/OFFLINE/UNMAPPED. Red for critical fault, amber for unknown/warning, teal for healthy.
- Build Debug AND versioned ReleaseCandidate, run existing 47 non-hardware unit tests and record actual totals; do not assume results.
- LAUNCH THE NEW WPF CANDIDATE in its own path (do not overwrite the active Release or production launcher). Use Computer Use through Orca if available to visually INSPECT and INTERACT with the actual app: click LINE OVERVIEW, STATION 01, STATION 02, switch Sensors groups/search and Parameters, navigate SETTINGS subsections, inspect ALARMS and SHARED CONVEYOR without pressing hardware action controls. Verify keyboard focus and response.
- Capture and READ BACK actual WPF desktop screenshots at 1366x768 AND 1649x928 for Overview and both Stations. Detect clipping, text too small, unbalanced whitespace, uneven camera vs TCP sizing, overflow, broken alignment and visual inconsistencies. ITERATE until acceptable. Do not claim Computer Use tested if you used only PowerShell/UIAutomation: disclose fallback and what exactly was visually checked.
- Do not configure physical camera IP or connect/actuate hardware during visual testing; demo/offline preview is allowed if safely isolated.
- Write concise QA verdict with evidence and screenshot paths in Docs/ORCA_WPF_SIEMENS_RESULT_20261020.md. New milestone checkpoint after each substantial step. Only mark DONE when real navigation and layout inspection pass.

## Safety and Git
- No PLC/TwinCAT download, activation, ADS writes, MC writes, servo/JOG/robot commands, conveyor START/STOP tests, E-stop actuation, or physical sensor activation.
- Do not edit PLC files; especially preserve existing modified Mitsubishi GXW file.
- Do not delete folders, reset Git, push to GitHub or overwrite Release without an explicit separate user instruction.
- Work only in existing session WPF. Report progress at M0 baseline, M1 design, M2 code, M3 build/test, M4 screenshots, M5 final.
- Acknowledge task explicitly in WPF and start M0; don't just provide a plan.

FIRST WPF RESPONSE: "Received DATN-WPF-SIEMENS-REFINEMENT-20261020 in existing WPF session. I will preserve completed single-screen functions and Git changes, apply the Siemens-inspired design, build/test offline, and perform real UI interaction/screenshot reviews before claiming completion. No PLC or equipment writes."