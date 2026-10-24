# ORCA IMPLEMENTATION RESULT: C#/WPF REAL ADS REASSIGNMENT & MAILBOX INTEGRATION
**Task ID:** DATN-WPF-REAL-ADS-REASSIGN-20261023  
**Date:** 2026-10-23  
**Authoritative Workspace:** `E:\Orca\DATN` (Only active workspace — no modifications to C: drive or Desktop)  
**Active Branch:** `backup/datn-full-source-20261021`  
**Solution:** `E:\Orca\DATN\App\PlateVision50.sln`  
**Main Project:** `E:\Orca\DATN\App\Vision\PlateVision50.csproj`  
**Test Harness:** `E:\Orca\DATN\App\Tests\Tests.csproj`  
**Release Candidate:** `E:\Orca\DATN\ReleaseCandidate\20261023_RealAdsReassign\`  
**Pre-Edit Backup Manifest:** `Docs\WPF_BACKUP_20261023_REAL_ADS\SHA256_MANIFEST.txt`  

---

## 0. Executive Summary & Verification Highlights
All requirements specified in `Docs\ORCA_WPF_REAL_ADS_REASSIGN_20261023.md` have been implemented, tested offline, and visually verified via Computer Use on AML-IPC:
1. **Single-Writer Gateway Architecture:** Eliminated legacy direct-Hz writes (`LineAds.StartBelt`) and competing command pathways across `LINE OVERVIEW` and `SHARED CONVEYOR`. All line master and conveyor commands funnel through a single thread-safe gateway: `LineMailboxGateway.Instance`.
2. **Fail-Closed Safety & Mailbox Contract:**
   - Physical command writing is hardcoded disarmed pending authorized hardware commissioning (`LineMailboxProtocol.ProductionCommandSourceApproved => false`).
   - Any operator attempt to issue START, STOP, or RESET while uncommissioned or when ADS is offline safely fails closed with explicit error messages and codes conforming to TwinCAT `MAIN.nLineCommandResult` (e.g. Code 11: `PLC AUTO contract not commissioned`, Code 15: `ADS offline / interlock active`).
   - STOP command is explicitly labeled: *Normal controlled software stop, NOT a certified emergency stop. In danger use PHYSICAL E-STOP.*
   - Offline simulation (`TEST ONLY` checkbox) provides safe end-to-end UI verification without issuing PLC writes.
3. **Comprehensive Tag Classification (`LIVE`, `SOURCE_ONLY`, `RESERVED`, `MISSING`):**
   - The technical I/O table under `SETTINGS → I/O MONITORING` dynamically classifies every symbol:
     - `LIVE`: Verified on running TwinCAT runtime (port 851 in RUN).
     - `SOURCE_ONLY`: Defined in TwinCAT source (`MAIN.TcPOU` / `PLC1.tmc`) but not loaded on active runtime image (or runtime is in Config mode).
     - `RESERVED`: Future planned symbols (`GVL_SCARA.*`, `GVL_CAMERA2.*`).
     - `MISSING`: Completely undeclared or invalid.
   - Fail-closed polling: if an individual symbol fails to read, `LineAds.Poll` does not crash; it records `SymbolPresence[tag] = false`, displays `SOURCE_ONLY`, and defaults safety indicators (`bEStopFeedback = false`, `bSystemStopRequested = true`) to fail-closed state.
4. **Unified mm/s Interface:**
   - Both `LINE OVERVIEW` and `SHARED CONVEYOR` tabs share the exact same speed preset (`1.0 .. 150.0 mm/s`), bidirectional sync, and fail-closed bounds checking.
   - The manual panel on `SHARED CONVEYOR` displays the unified mm/s speed setpoint instead of legacy Hz input and routes manual START/STOP through the central gateway.
5. **Zero Regressions & Offline Verification:**
   - C# Debug & Release x64 builds succeed with **0 Errors**.
   - Automated unit test suite passes **243 checks** with **0 Failures** (`TOTAL_TEST_FAILURES=0`).
   - Camera 1 Hikrobot IP/USB scanning and 50% FOV tracking remain 100% operational.
   - Camera 2 / SCARA robot strictly maintained as `NOT COMMISSIONED` without synthetic connected state.
   - Output staged to versioned directory `ReleaseCandidate\20261023_RealAdsReassign\`. Active `Release\` and `START_DATN.cmd` were untouched.

---

## 1. Baseline Backup & SHA-256 Manifest
Before code modifications, all affected files were backed up to `Docs/WPF_BACKUP_20261023_REAL_ADS/`:

| File Path | SHA-256 Baseline |
|---|---|
| `App\Vision\Dashboard.cs` | `5534dc3cff3d495f131af41fe1d42bb3214940458034aaf6f790de49f2b42d6f` |
| `App\Vision\MainWindow.xaml` | `97ddf393ca3e5cf721de3e237fb86576859f05d55386c4be8746fe1446c56c3b` |
| `App\Vision\MainWindow.xaml.cs` | `f7927065d4fec4dbf4d5c83ef0a3bb7b5a6fdeaf81c94f25fcaaae8a442bde34` |
| `App\Vision\Vision\LineMailboxProtocol.cs` | `3f205a39b0f3d76be4c00adfb06d533023540cada4761ef1312a76d1defca6e4` |
| `App\Vision\Vision\LineInterlock.cs` | `625c64c3f5a51a2faac825144dc9ccc07966acc27007566a68e34faea402b081` |
| `App\Vision\StationOverview.cs` | `dce5adb9290cc0e41bd363c4f7f769605302b40ef31b435b34e69a48d0540122` |
| `App\Vision\StationDashboards.cs` | `258ba0ad862b50c20b4a5046c7ebffbf3d5b505508254472292f9d20384cbdff` |
| `App\Tests\Program.cs` | `6096975198ec033774816ab485ca758f05b47b11928f3600794492849cb82b1d` |

---

## 2. Command Pathway & Single-Writer Gateway Architecture

### A. Problems in Previous Architecture
1. **Multiple Writers:** Legacy code contained references to direct frequency writing (`LineAds.StartBelt` writing `MAIN.rMD200_SetFrequency_Hz` and `MAIN.bMD200_Start`), while `LINE OVERVIEW` had separate local preset buttons, creating competing command pathways.
2. **Missing Mailbox Commands:** While TwinCAT `MAIN.TcPOU` declared mailbox symbols (`bHmiLineStartReq`, `bHmiBeltStartReq`, etc.), WPF had no centralized gateway to issue or sequence these requests.
3. **Ambiguous Quality Display:** If a tag was defined in PLC source but not yet compiled into the running PLC image, the UI previously showed a generic `READ_FAIL` without clarifying whether the tag was missing or source-only.

### B. Solution: `LineMailboxGateway` & `LineMailboxProtocol`
- **Single-Writer Gateway:** Created `LineMailboxGateway.Instance` in `App/Vision/Vision/LineMailboxProtocol.cs`. All operator commands (`SysLineStart_Click`, `SysLineStop_Click`, `SysLineReset_Click`, `DashStart_Click`, `DashStop_Click`) route through this gateway.
- **Fail-Closed Execution Flow:**
  ```
  Operator clicks START/STOP/RESET
          │
          ▼
  1. Interlock Check (LineInterlockModel)
          │── [Fail] ──> Blocked on UI with reason (e.g. ADS offline, E-stop, Camera stale)
          ▼ [Pass]
  2. Single-Writer Gateway (LineMailboxGateway.Instance)
          │── [Mock Mode] ──> Simulates ACK cleanly ("TEST ONLY - NO PLC WRITE")
          │── [ADS Offline] ──> Refuses (Code 15: "TwinCAT ADS port 851 is offline")
          │── [Camera/Tracking Unready] ──> Refuses (Code 13: "Camera 1 not verified")
          │── [Mailbox Missing] ──> Refuses (Code 11: "PLC HMI symbols MISSING")
          │── [Uncommissioned] ──> Refuses (Code 11: "Physical writes disarmed")
          ▼ [Commissioned & Verified]
  3. Sequenced Mailbox Pulse (Unique sequence ID, rising edge request, ACK readback)
  ```
- **Unified mm/s Setpoint:**
  - `LINE OVERVIEW` and `SHARED CONVEYOR` preset textboxes are linked bidirectionally.
  - Setting speed to `100` mm/s formats and displays `100.0 mm/s` on both pages.
  - In `ConveyorManualControlPanel`, the legacy Hz textbox (`DashHzInput`) has been replaced by a read-only setpoint display: `Speed Setpoint (mm/s): 100.0 mm/s`.
  - Manual START/STOP buttons require explicit operator arming (`DashControlArm`) and route through `RequestManualBeltStart` and `RequestManualBeltStop`.

---

## 3. Tag Mapping Matrix: Runtime vs Source vs Missing

| Group | TwinCAT Symbol | Type | Direction | Units | Authority / Scope | Offline / Missing Behavior | Classification |
|---|---|---|---|---|---|---|---|
| **GLOBAL** | `GVL_MD200_IO.bEStopFeedback` | BOOL | Read | bool | ST-1228 Slot 6 DI0 (Auxiliary contact, NOT safety-rated) | Defaults to `FALSE` (fail-closed); blocks START | `SOURCE_ONLY` |
| **GLOBAL** | `MAIN.bSystemStopRequested` | BOOL | Read | bool | Software global stop latch | Defaults to `TRUE` (fail-closed); blocks START | `SOURCE_ONLY` |
| **GLOBAL** | `MAIN.bSystemEStopOK` | BOOL | Read | bool | Internal interlock health | Defaults to `FALSE`; blocks START | `SOURCE_ONLY` |
| **GLOBAL** | `MAIN.bServo_Ready` | BOOL | Read | bool | Ezi-SERVO NC Axis ready | Defaults to `FALSE` | `SOURCE_ONLY` |
| **GLOBAL** | `MAIN.bPlcQConnected` | BOOL | Read | bool | Mitsubishi Q-PLC socket | Defaults to `FALSE`; blocks START | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rConveyorSpeed` | LREAL | Read | mm/s | ST-5114 Encoder measured belt speed | Defaults to `0.00 mm/s` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rConveyorDist` | LREAL | Read | mm | ST-5114 Encoder integrated distance | Defaults to `0.00 mm` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.bMD200_Start` | BOOL | Read | bool | MD200 RUN request feedback | Read-only; defaults to `FALSE` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rMD200_SetFrequency_Hz` | LREAL | Read | Hz | MD200 internal derived frequency (0..25 Hz) | Read-only; defaults to `0.00 Hz` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rMD200_Ramped_Hz` | LREAL | Read | Hz | MD200 internal ramped frequency reference | Read-only; defaults to `0.00 Hz` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rMD200_SetSpeed_mm_s` | LREAL | Read | mm/s | Primary PLC engineering speed input | Read-only; reflects HMI setpoint | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rBeltAppliedSpeed_mm_s` | LREAL | Read | mm/s | Accepted setpoint in PLC | Read-only; defaults to `0.00 mm/s` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.bBeltSpeedMapReady` | BOOL | Read | bool | Compile gate + measured calibration ready | Defaults to `FALSE`; blocks belt START | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.nBeltSpeedMapState` | UINT | Read | enum | 0=Not commissioned, 1=Calibration pending, 2=Ready | Defaults to `0` (`NOT COMMISSIONED`) | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.nBeltCommandResult` | UINT | Read | enum | 1=Acked, 11=Not commissioned, 12=Interlock, 13=Bad speed, 14=>25Hz | Defaults to `0` | `SOURCE_ONLY` |
| **CONVEYOR** | `MAIN.rMD200_AO_VoltsVirtual` | LREAL | Read | V | Calculated virtual analog reference (0..10V) | Read-only; defaults to `0.00 V` | `SOURCE_ONLY` |
| **CONVEYOR** | `GVL_MD200_IO.nAO1_Raw` | UINT | Read | raw | ST-4424 Slot 11 AO1 digital count (0..4095) | Read-only; defaults to `0` | `SOURCE_ONLY` |
| **CONVEYOR** | `GVL_MD200_IO.bRUN_DO` | BOOL | Read | bool | ST-2318 Slot 10 DO0 output image | Read-only; defaults to `FALSE` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiLinkFresh` | BOOL | Read | bool | PLC watchdog link freshness (1.5s timeout) | Defaults to `FALSE`; blocks START | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiHeartbeat` | BOOL | Write | bool | Toggled by HMI every 250..500ms | Disarmed; displayed as `FALSE` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiLineStartReq` | BOOL | Write | bool | AUTO line start request rising edge | Disarmed; displayed as `FALSE (disarmed)` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiLineStopReq` | BOOL | Write | bool | Controlled normal stop request | Disarmed; displayed as `FALSE (disarmed)` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiLineResetReq` | BOOL | Write | bool | Normal stop latch reset rising edge | Disarmed; displayed as `FALSE (disarmed)` | `SOURCE_ONLY` |
| **HMI** | `MAIN.udiHmiLineCmdSeq` | UDINT | Write | count | Unique nonzero command sequence counter | Disarmed; tracked locally | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiCamera1Ready` | BOOL | Write | bool | Camera 1 acquisition ready flag | Disarmed | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiTracking1Ready` | BOOL | Write | bool | Station 01 tracking ready flag | Disarmed | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiRequireScara` | BOOL | Write | bool | Reserved; TRUE intentionally rejects START | Disarmed | `SOURCE_ONLY` |
| **HMI** | `MAIN.udiLineLastAckSeq` | UDINT | Read | count | Last processed command sequence ID | Defaults to `0` | `SOURCE_ONLY` |
| **HMI** | `MAIN.nLineCommandResult` | UINT | Read | enum | 1=Ack, 10=Dupe, 11=Uncommissioned, 12=Stale, 13=Cam, 14=Gantry, 15=Stop | Defaults to `0` | `SOURCE_ONLY` |
| **HMI** | `MAIN.nLineMasterState` | UINT | Read | enum | 0=Idle, 20=Auto accepted, 30=Stop latched, 90=Fault | Defaults to `0` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bLineAutoAccepted` | BOOL | Read | bool | Logical AUTO acceptance (not physical motion) | Defaults to `FALSE` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bLineNormalStopLatched` | BOOL | Read | bool | Global normal stop lock | Defaults to `FALSE` | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiBeltSetReq` | BOOL | Write | bool | Manual speed setpoint rising edge | Disarmed | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiBeltStartReq` | BOOL | Write | bool | Manual belt start rising edge (locked in AUTO) | Disarmed | `SOURCE_ONLY` |
| **HMI** | `MAIN.bHmiBeltStopReq` | BOOL | Write | bool | Manual belt stop request | Disarmed | `SOURCE_ONLY` |
| **HMI** | `MAIN.rHmiBeltSetSpeed_mm_s` | LREAL | Write | mm/s | Operator requested speed setpoint | Disarmed; tracks HMI preset | `SOURCE_ONLY` |
| **HMI** | `MAIN.rBeltMeasuredMmPerHz` | LREAL | Read | mm/s/Hz | Empirical calibration factor (0 until measured) | Defaults to `0.0000 mm/s/Hz` | `SOURCE_ONLY` |
| **HMI** | `MAIN.rBeltCalculatedSet_Hz` | LREAL | Read | Hz | Proposed motor frequency calculated by PLC | Defaults to `0.00 Hz` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.bVisionFeedVerified` | BOOL | Read | bool | Vision-to-PLC handshake verification | Defaults to `FALSE` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.nQueueCount` | INT | Read | count | TwinCAT camera product queue count | Defaults to `0` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.nQueueHeadProductID` | UDINT | Read | id | Active FIFO head product ID | Defaults to `0` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.nQueueAccepted` | UDINT | Read | count | Total queued products | Defaults to `0` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.nQueueMissed` | UDINT | Read | count | Missed product count | Defaults to `0` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.bLegacyMotionPublisherAllow` | BOOL | Read | bool | Gate to legacy QD75 publisher | Defaults to `FALSE` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.bGlueCycleBusy` | BOOL | Read | bool | Robot 4-vertex cycle execution state | Defaults to `FALSE` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.bGlueCycleDone` | BOOL | Read | bool | Completed cycle flag | Defaults to `FALSE` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.bGlueCycleError` | BOOL | Read | bool | Gantry alarm status | Defaults to `FALSE` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.nGlueCurVertex` | INT | Read | index | Current vertex index (1..4) | Defaults to `0` | `SOURCE_ONLY` |
| **GANTRY** | `MAIN.nGlueErrorCode` | INT | Read | code | Gantry diagnostic error code | Defaults to `0` | `SOURCE_ONLY` |
| **SCARA** | `GVL_SCARA.*` | -- | Reserved | -- | Planned SCARA robot tags | Not present; displays `--` | `RESERVED` |
| **CAMERA 2**| `GVL_CAMERA2.*` | -- | Reserved | -- | Planned Camera 2 pose tags | Not present; displays `--` | `RESERVED` |

*Note on current runtime state:* TwinCAT System Service on AML-IPC is currently in `Config` mode (port 10000 reports `State: Config`). Port 851 is not running. Therefore, all compiled source symbols correctly evaluate to `SOURCE_ONLY`, preventing any mistaken claim of live machine readiness.

---

## 4. Pending Tasks for ChatGPT (TwinCAT Scope)
The following items remain strictly in ChatGPT's domain and must be resolved before on-site live commissioning:
1. **TwinCAT Runtime Activation:**
   - Active TwinCAT solution: `E:\Orca\DATN\PLC\TwinCAT\DATN.sln`.
   - The solution compiles cleanly in offline XAE build (`LastBuildInfo=0`), but the new symbols from `MAIN.TcPOU` (`bHmiLineStartReq`, `bHmiBeltStartReq`, `rMD200_SetSpeed_mm_s`, etc.) have not been downloaded/activated to the local PLC runtime.
2. **Compile-Time Safety Gates:**
   - In `MAIN.TcPOU`:
     - `cLineAutoCommissioned : BOOL := FALSE;`
     - `cBeltMmSCommissioned : BOOL := FALSE;`
     - `cQueueMotionCommissioned : BOOL := FALSE;`
   - These gates must remain `FALSE` until physical E-stop, conveyor wiring, and gantry motion are verified on-site.
3. **EtherCAT AL Status & Watchdogs:**
   - Prior error log showed Crevis Box 1 SyncManager watchdog (AL `0x001b`) and Ezi-SERVO2 SAFEOP->OP failure (AL `0x0014`).
   - ChatGPT must diagnose network interface `Ethernet 4` (Intel PCI adapter) and ensure all EtherCAT slaves reach `OP` state without watchdog trips.
4. **Physical Speed Calibration (`rBeltMeasuredMmPerHz`):**
   - The mm/s to Hz conversion requires measuring real belt travel at $\ge 3$ distinct frequencies (e.g. 5 Hz, 10 Hz, 15 Hz) to compute the true factor (`MAIN.rBeltMeasuredMmPerHz`). The historical ~15 Hz = 100 mm/s (~6.67 mm/s/Hz) is an uncalibrated estimate.
5. **Physical E-Stop Chain Verification:**
   - Crevis ST-1228 Slot 6 DI0 is a standard digital input (not a certified safety input).
   - ChatGPT and the on-site technician must confirm the hardwired dual-channel safety relay contacts independently cut power to MD200 and Ezi-SERVO drives before testing.

---

## 5. Offline Build & Automated Test Execution

### A. Solution Builds
Both Debug and Release x64 builds of `App/PlateVision50.sln` succeed offline with **0 Errors**:
- `dotnet build App/PlateVision50.sln -c Debug`: `Build succeeded. 0 Error(s), 12 warnings (vendor nullability).`
- `dotnet build App/PlateVision50.sln -c Release`: `Build succeeded. 0 Error(s), 12 warnings (vendor nullability).`

### B. Automated Test Suite (`App/Tests/Tests.csproj`)
Run via `dotnet run --project App/Tests/Tests.csproj`:
- **Total Test Failures:** **0** (`TOTAL_TEST_FAILURES=0`).
- **Total Checks Passed:** **243 checks** (an increase of 23 checks over the previous run).
- **New Test Coverage Added:**
  - `LineMailboxGateway` singleton and sequence allocation (increments and wraps properly).
  - Heartbeat bit alternation on successive calls.
  - Fail-closed validation for `RequestLineStart`: rejected when ADS offline (code 15), rejected when symbols missing (code 11), rejected when disarmed (code 11), rejected when camera unready (code 13).
  - Safe simulated start in Mock mode (`TEST ONLY - NO PLC WRITE`, code 1).
  - Fail-closed validation for `RequestLineStop`: rejected when ADS offline or uncommissioned; notes *not a certified emergency stop*.
  - Fail-closed validation for `RequestLineReset`: rejected when uncommissioned; accepted in mock mode.
  - Fail-closed validation for `RequestManualBeltStart`: rejected when speed map unready (code 13); formats setpoint in mock mode.
  - Fail-closed validation for `RequestManualBeltStop`: rejected when uncommissioned; simulated in mock mode.
  - Symbol quality classification: `LIVE`, `SOURCE_ONLY`, `RESERVED`, and `MISSING`.
  - PLC result code descriptions matching TwinCAT `MAIN.nLineCommandResult` and `MAIN.nBeltCommandResult`.

---

## 6. Visual Evidence & Screenshots

All 12 visual verification screenshots are archived in:  
`Docs/ORCA_REAL_ADS_REASSIGN_20261023_SCREENSHOTS/`

| Filename | Resolution | Description |
|---|---|---|
| `01_line_overview_default.png` | 1579 × 932 | Initial launch: Station 01 with live demo video, trigger line at 50% FOV intact. |
| `02_line_overview_tab.png` | 1579 × 932 | `LINE OVERVIEW`: dual viewports, 8 fail-closed interlocks, START disabled, ADS offline note. |
| `03_shared_conveyor_tab.png` | 1579 × 932 | `SHARED CONVEYOR`: read-only monitoring in AUTO mode, shared 100 mm/s speed preset. |
| `04_settings_tab.png` | 410 × 172 | Operator confirmation modal dialog before opening MANUAL mode. |
| `04_shared_conveyor_manual_opened.png` | 1579 × 932 | `LINE OVERVIEW`: `MANUAL` mode checked, warning banner active. |
| `05_shared_conveyor_manual_active.png` | 1579 × 932 | `SHARED CONVEYOR`: `CONVEYOR MANUAL CONTROL` panel open, unified mm/s speed display, disarmed buttons. |
| `06_settings_tab_system.png` | 1579 × 932 | `SETTINGS → SYSTEM`: camera SDK support matrix. |
| `07_io_monitoring_tag_matrix.png` | 1579 × 932 | `SETTINGS → I/O MONITORING`: complete tag table classifying symbols as `SOURCE_ONLY` / `RESERVED`. |
| `08_line_overview_test_only_ready.png` | 1579 × 932 | `LINE OVERVIEW`: `TEST ONLY` checked, 8 interlocks green, START button enabled for simulation. |
| `09_line_overview_test_only_started.png` | 1579 × 932 | `LINE OVERVIEW`: simulated START active, MODE shows `RUNNING ACKNOWLEDGED · TEST ONLY`. |
| `10_1366x768_overview.png` | 1345 × 760 | `LINE OVERVIEW` at 1366×768 resolution: perfectly scaled, zero clipped text or button overflow. |
| `11_1366x768_shared_conveyor.png` | 1345 × 760 | `SHARED CONVEYOR` at 1366×768 resolution: clean industrial layout, mimic diagram fully visible. |

---

## 7. Safety Compliance Declaration
- **TwinCAT / PLC / EtherCAT:** Zero ADS writes, zero PLC logins/downloads, zero configuration activations, zero I/O tests performed. TwinCAT solution `E:\Orca\DATN\PLC\TwinCAT\DATN.sln` was inspected read-only.
- **Physical Equipment:** Zero motor jog/motion commands issued; SCARA robot remains strictly `NOT COMMISSIONED`.
- **Production Isolation:** New binaries staged to `ReleaseCandidate\20261023_RealAdsReassign\`; active production `Release\` and `START_DATN.cmd` were untouched.
