# ORCA IMPLEMENTATION RESULT: DUAL STATION USB/GIGE CAMERAS + COMPACT INDUSTRIAL UI
**Task ID:** DATN-WPF-DUAL-TRANSPORT-20261022  
**Date:** 2026-10-22  
**Authoritative Workspace:** `E:\Orca\DATN` (Only active workspace — no modifications to C: drive)  
**Solution:** `E:\Orca\DATN\App\PlateVision50.sln`  
**Main Project:** `E:\Orca\DATN\App\Vision\PlateVision50.csproj`  
**Test Harness:** `E:\Orca\DATN\App\Tests\Tests.csproj`  
**Release Candidate:** `E:\Orca\DATN\ReleaseCandidate\20261022_DualTransport_MinimalUI\`  

---

## 0. Executive Summary & Verification Highlights
All requirements specified in `Docs\ORCA_DUAL_STATION_USB_GIGE_MINIMAL_UI_20261022.md` have been implemented, tested offline, and visually verified on AML-IPC:
1. **Dual Flexible Camera Slots:** Both Station 01 (Gantry) and Station 02 (SCARA) support vendor-compatible USB3 Vision and GigE/IP cameras using the identical compact connection toolbar (`CAM` | `TYPE [AUTO/USB/IP]` | `DEVICE [Model - Serial - Vendor - Transport]` | `IP` [visible only in IP mode] | `SCAN` | `CONNECT` | `DISCONNECT` | `STATUS`).
2. **Two Concurrent Independent Streams:** Each station owns its own camera controller (`StationCameraController`), lifecycle, and frame dispatch. Station 01 drives `Station1Preview` + Overview `Ov1CamImage`; Station 02 drives `Station2Preview` + Overview `Ov2CamImage`.
3. **Mutual Exclusion & Unplug Isolation:** A thread-safe `CameraOwnershipRegistry` prevents opening the same physical camera serial on both stations simultaneously (rejects competing claims with `"In use by Station 0X"`). Unplugging or disconnecting one station sets that slot to `LOST` / `OFFLINE` while the other station continues streaming uninterrupted.
4. **Minimal Industrial UI:** Removed verbose explanatory paragraphs, duplicate section headings, and non-essential subtitles across all 6 operational tabs (`LINE OVERVIEW`, `STATION 01`, `STATION 02`, `SHARED CONVEYOR`, `SETTINGS`, `ALARMS & LOGS`). Retained all critical safety warnings (`NOT COMMISSIONED`, `PHYSICAL E-STOP ONLY`, `ADS READ-ONLY`, `FAIL-CLOSED`).
5. **Clear SDK Matrix in Settings:** The supported SDK variants are clearly declared in `SETTINGS → SYSTEM`:
   - Basler USB3 Vision: **SUPPORTED** (pylon 8.1 x64 / USB3 GenTL)
   - Basler GigE Vision: **SUPPORTED** (pylon 8.1 x64 / GigE GenTL)
   - Hikrobot GigE Vision: **SUPPORTED** (MVS GigE native direct / GVCP)
   - Hikrobot USB Vision: **NOT SUPPORTED** (MVS USB kernel driver not installed)
   - Generic USB / UVC Webcams: **UNSUPPORTED** (no direct capture backend; rejects generic UVC with clear reason)
6. **Safety Invariants Maintained:** 100% offline verification; no PLC/TwinCAT modifications, no ADS writes, no physical camera driver installs, no servo motion, and SCARA robot remains strictly `NOT COMMISSIONED`.

---

## 1. Source Backups & Checkpoint
Before making any changes, all affected WPF source files were backed up with SHA-256 hashes to `Docs/WPF_BACKUP_DUAL_CAMERA_20261022/`:

| Source File | SHA-256 Baseline |
|---|---|
| `App/Vision/MainWindow.xaml` | `3d1f7b014ee6884bb1880fce8bc03c5f35ccf8e4c853deae3c07596de9cd85a3` |
| `App/Vision/MainWindow.xaml.cs` | `ffca56c11caa0a6017ea09e22935930c86d424dae0d95eb0dc98f67e631bf806` |
| `App/Vision/Dashboard.cs` | `a14f1a4bce32d667617cb9f435cac16bf32c050fbae0409f0462c3f4f1ed1316` |
| `App/Vision/StationDashboards.cs` | `96297b9ae45ca2bafa07191b17a1e10dc621533d0e9219ae4a6eef6fa02b7612` |
| `App/Vision/StationOverview.cs` | `6c5ffe63d752cb9eaded12979c7e16c4a574004e1ea07b2286cb5eb81e564910` |
| `App/Vision/PlateVision50.csproj` | `05bbda94b3694723a038bfbff09ee1cd3e47ba2bc4c5240716854787a2deb8fe` |
| `App/Vision/Vision/LineInterlock.cs` | `625c64c3f5a51a2faac825144dc9ccc07966acc27007566a68e34faea402b081` |
| `App/Tests/Program.cs` | `e3b5d41238be0bba6f81afc516e8b6cbd829e2502cf751ce85037e7ef72629c6` |
| `App/Tests/Tests.csproj` | `d39233460be900fcff15fcb579b099084b216511dce7a44e8e86e55ec9f29678` |

---

## 2. Architecture of Two Flexible Camera Slots

### A. New Modules Created
1. **`App/Vision/Vision/CameraSlotModel.cs`**:
   - `CameraTransportMode`: `Auto`, `Usb`, `Ip`.
   - `CameraSlotState`: `Offline`, `Scanning`, `Connecting`, `Connected`, `Live`, `Lost`, `Error`.
   - `CameraDevice`: Record storing vendor, transport, model, serial, address, backend index, uppercase normalized `Key` (`VENDOR|SERIAL`), and display label. Flags `IsGenericUvc` for webcam/UVC devices.
   - `CameraOwnershipRegistry`: Thread-safe slot claim table with `TryClaim`, `Release(slot)`, `OwnerOf(key)`, and `Reset()`. Prevents cross-station camera hijacking.
   - `CameraTransportPolicy`: Enforces SDK support rules (Basler USB/GigE, Hikrobot GigE/USB), validates IPv4 / RFC-1123 hostnames, rejects generic UVC with explicit reason, and rejects dispatch of USB cameras in IP mode and vice versa.
   - `CameraSlotModel`: Manages per-slot device filtering, selection, connection validation, truthful state transitions (`MarkScanning`, `MarkConnecting`, `MarkConnected`, `MarkFrame`, `MarkLost`, `MarkError`, `MarkOffline`), and timeout-enforced `IsLive(now, timeout)`.
2. **`App/Vision/Vision/StationCameraController.cs`**:
   - Owns camera SDK lifecycle for one station.
   - Enumerates physical devices via `UnifiedCameraService` without creating cross-station side effects.
   - Dispatches connection exclusively through vendor SDK (`HikDirectIpCamera` for native Hikrobot direct-IP; `UnifiedCameraService` for Basler USB3, Basler GigE, or Hikrobot enumerated devices).
   - Clones latest frames with zero UI thread freezing.
   - Persists per-station transport mode, IP, and selected device key to `%LOCALAPPDATA%\PlateVision50_V2\camera_slots.json` without credentials.

### B. UI Integration & Visual Layout
- **Station 01 & Station 02 Connection Bars:**
  - Placed directly above the live viewport in a compact single/two-row toolbar.
  - Controls:
    - Mode selector: ComboBox `AUTO` / `USB` / `IP`.
    - Device picker: ComboBox bound to filtered scanned devices.
    - IP row: `IP [TextBox]` + `NIC [TextBox]` — automatically displayed when in IP mode and collapsed when in USB mode.
    - Buttons: `SCAN`, `CONNECT`, `DISCONNECT`.
    - Status badge: `OFFLINE` (gray), `SCANNING` (amber), `CONNECTING` (amber), `CONNECTED` (amber), `LIVE` (teal), `LOST` (red), `ERROR` (red).
- **Line Overview Viewports:**
  - Viewports share width 50/50:
    - Left: `CAM 1 · GANTRY` | `OFFLINE` / `LIVE` / `VIDEO` + `[ ADS LIVE ]` badge.
    - Right: `CAM 2 · SCARA` | `OFFLINE` / `LIVE` / `NO USB CAMERA`.
    - Inner placeholder for Camera 02 clearly states: `NO SIGNAL · CAM 2 OFFLINE · SCARA not commissioned`.
  - Independent telemetry under each camera:
    - Cam 01: `TRACK ID`, `GANTRY / PLC-Q`, `PRODUCT X / Y`, `SERVO / MODE`.
    - Cam 02: `TRACK ID (--)`, `TRACKING (UNAVAILABLE)`, `PRODUCT X / Y (-- / -- mm)`, `SCARA ROBOT (NOT COMM.)`.

---

## 3. UI Simplification Results Across Every Tab

| Tab | Prior State | Simplified State |
|---|---|---|
| **Global Header** | Long subtitle `"GANTRY DISPENSING · SCARA PICK-PLACE · CONVEYOR TRACKING"` + verbose banner. | Subtitle removed; compact banner: `ADS READ-ONLY · NO MOTION` + `Theme` checkbox. |
| **LINE OVERVIEW** | Long title `"SHARED CONVEYOR + MACHINE COMMANDS"`, verbose `"MASTER: TWINCAT 3 · COMMAND CONTRACT PENDING"`, long footer text `"VISION COMMANDS: PC ONLY..."`. | Concise title `CONVEYOR + COMMANDS` \| `CONTRACT PENDING`. Compact labels: `RUN`, `ACTUAL (mm/s)`, `POSITION (mm)`, `MODE`. Buttons: `START`, `STOP`, `RESET`, `[ ] TEST ONLY`, `VISION START`, `VISION STOP`, `[ ] MANUAL`, `TAGS`. 8 concise interlock dots + single primary reason. |
| **STATION 01** | Redundant sub-headings (`GANTRY · 4-POINT GLUE DISPENSING`), explanatory paragraphs under queue, long text under video. | Clean heading `STATION 01`. Compact side panels (`ROBOT TELEMETRY`, `PRODUCT TRACKING`, `CONFIG`). Explanatory notes moved to ToolTips. |
| **STATION 02** | Hardcoded disabled IP strip, multiple repeated notices (`NOT INSTALLED / NOT COMMISSIONED`), verbose robot essay. | Identical dynamic camera toolbar to Station 01 (`CAM 2` \| `[ USB ▾ ]` \| `DEVICE` \| `SCAN` \| `CONNECT` \| `DISCONNECT`). Clear truth: `SCARA not commissioned`. |
| **SHARED CONVEYOR** | Paragraphs explaining single encoder source, motor shaft speed disclaimers, and redundant tracking models. | Clean heading `CONVEYOR · ENCODER 01` \| `MONITOR`. Compact speed preset `SPEED (mm/s) · SHARED [ 100 ] APPLY`. Disclaimers shortened to concise industrial labels. |
| **SETTINGS** | Explanatory text under every header. | Clean header `SETTINGS` \| `ACCESS: OPERATOR / MAINTENANCE`. Added dedicated `CAMERA SDK & TRANSPORT MATRIX` table declaring Basler and Hikrobot support. |
| **ALARMS & LOGS** | Verbose headers. | Concise tabular history. |

---

## 4. Build, Test and Release Candidate Verification

### A. Solution Builds
Both Debug and Release builds of `App/PlateVision50.sln` succeed offline with **0 Errors**:
- `dotnet build App/PlateVision50.sln -c Debug`: `Build succeeded. 0 Error(s), 11 vendor nullability warnings.`
- `dotnet build App/PlateVision50.sln -c Release`: `Build succeeded. 0 Error(s), 11 vendor nullability warnings.`

### B. Automated Test Suite (`App/Tests/Tests.csproj`)
Run via `dotnet run --project App/Tests/Tests.csproj`:
- **Total Test Failures:** **0** (`TOTAL_TEST_FAILURES=0`).
- **Comprehensive test coverage added:**
  - Device normalization: null-safety, uppercase key formatting, display formatting.
  - UVC detection: generic UVC / webcam detection and rejection with capture backend explanation.
  - SDK policy: Basler USB/GigE and Hikrobot USB/GigE dispatch; wrong transport rejection; unknown vendor rejection.
  - Address validation: IPv4 dotted quads, hostnames, rejection of empty, spaces, ports, URLs, and out-of-range octets.
  - Slot model: device scanning, deduplication, sorting (USB then GigE then model), Auto/Usb/Ip filtering.
  - USB mode: no IP required, claims camera on connect, ignores invalid IP text.
  - IP mode: requires valid address, validates before connect.
  - Ownership: multi-slot claim table, double-claim rejection (`"In use by Station 01"`), slot release isolation, slot reset.
  - State machine: `Scanning`, `Connecting`, `Connected`, `Live`, `Lost`, `Error`, `Offline`.
  - Truthful freshness: `Connected` without frames is NOT `Live`; `IsLive` timeout enforcement; reconnect with old frame shows `NO FRAME`.
  - Concurrency: Slot 1 and Slot 2 validate and connect different cameras concurrently.
  - Unplug isolation: Slot 2 going `LOST` does not affect Slot 1 `LIVE` status.
  - Disconnect isolation: releasing Slot 2 keeps Slot 1 claimed and running.
  - Interlock safety: Cam 2 alone does not satisfy Cam 1 interlock; camera connection never triggers auto-start or machine motion.
  - All existing interlock (8 checks, fail-closed, speed bounds, mock mode) and vision tests pass with 0 regressions.

### C. Release Candidate Directory
The output binaries are staged at:
`ReleaseCandidate\20261022_DualTransport_MinimalUI\`  
(Preserved active `Release\` directory and `START_DATN.cmd`).

---

## 5. Visual QA Evidence & Screenshots

All visual test screenshots are archived under:  
`Docs/ORCA_DUAL_CAMERA_20261022_SCREENSHOTS/`

| Filename | Resolution | Description |
|---|---|---|
| `01_line_overview_wide.png` | 1579 × 932 | LINE OVERVIEW: dual 50/50 viewports (Cam 1 & Cam 2), compact command strip, 8 interlock dots, primary failure reason. |
| `02_station01_auto.png` | 1579 × 932 | STATION 01: AUTO mode camera toolbar, device picker, offline viewport, side telemetry panels. |
| `02_station01_ip_mode.png` | 1579 × 932 | STATION 01: IP mode showing dynamically displayed IP and NIC entry fields. |
| `03_station02_auto.png` | 1579 × 932 | STATION 02: AUTO mode camera toolbar identical to Station 01. |
| `03_station02_usb_mode.png` | 1579 × 932 | STATION 02: USB mode scanned (reports `NO USB CAMERA`), no IP field required or visible. |
| `04_shared_conveyor.png` | 1579 × 932 | SHARED CONVEYOR: shared speed setpoint `[ 100 ] mm/s`, conveyor telemetry, mimic diagram. |
| `05_settings_sdk_matrix.png` | 1579 × 932 | SETTINGS: `SYSTEM` tab displaying the complete `CAMERA SDK & TRANSPORT MATRIX` table. |
| `06_alarms_and_logs.png` | 1579 × 932 | ALARMS & LOGS: read-only alarm log and tracking audit history. |
| `07_mock_ready_test_only.png` | 1579 × 932 | LINE OVERVIEW: `TEST ONLY` mode checked, all 8 interlocks green, START button enabled for offline simulation. |
| `11_1366x768_overview.png` | 1345 × 760 | LINE OVERVIEW at 1366×768 resolution: perfectly scaled, zero clipped buttons or text. |
| `12_1366x768_station01_ip.png` | 1345 × 760 | STATION 01 at 1366×768 resolution: compact toolbar, IP row visible. |
| `13_1366x768_station02_usb.png` | 1345 × 760 | STATION 02 at 1366×768 resolution: compact toolbar, USB mode active, truthful `SCARA not commissioned`. |
| `14_demo_overview.png` | 1579 × 932 | LINE OVERVIEW with demo video playback active on Cam 01 (plates crossing midline) while Cam 02 is offline. |

---

## 6. Live Commissioning Prerequisites & Open Items
1. **Physical Camera Hardware:**
   - Basler USB3 camera requires physical connection to AML-IPC USB3 port and driver confirmation in Basler Pylon Viewer.
   - Hikrobot GigE camera requires physical Ethernet connection to dedicated NIC with matching subnet (e.g. `169.254.113.1` / `169.254.113.10`).
2. **Speed Setpoint Calibration:**
   - The UI correctly displays `Hz: NOT CALIBRATED` until measured physical calibration data is collected and entered.
3. **TwinCAT AUTO Mailbox:**
   - Production line START remains fail-closed until the TwinCAT Master implements and acknowledges `MAIN.bHmiLineStartReq`.
4. **SCARA Robot:**
   - Station 02 remains strictly `NOT COMMISSIONED`. Camera 02 acquisition operates in image-only mode and cannot actuate the robot.
5. **Machine Safety:**
   - Dual-channel physical emergency stop circuit must be verified on-site before any equipment motion is authorized.
