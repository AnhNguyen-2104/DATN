# GANTRY TELEMETRY & SAFE READ-ONLY SYNCHRONIZATION CHECKPOINT (2026-10-25)

## 1. Executive Summary
- **Workspace**: `E:\Orca\DATN`
- **Objective**: Synchronize Gantry robot variables from TwinCAT 3 (ADS port 851) to the WPF application (`PlateVision50.csproj`) in a strictly safe, read-only manner.
- **Safety Boundary**: Zero ADS writes to robot motion registers (`MAIN.bStartGlueCycle`, `MAIN.bTest_WriteM1000`, `MAIN.bTest_WriteD100`). No PLC program activation/download. `cQueueMotionCommissioned` remains compile-locked `FALSE`.
- **TDD Verification**: 20 new unit tests added to `App/Tests/Program.cs` verifying known source symbol registration and quality classification. Tested in red phase (failed as expected), then green phase (100% passed, 0 failures).
- **Build Status**: `App/Tests/Tests.csproj` (0 errors), `App/Vision/PlateVision50.csproj` (0 errors).

---

## 2. Verified TwinCAT Gantry Symbols & Data Types
All synchronized symbols are verified directly from `PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU` and `FB_QD75CommandPublisher.TcPOU`:

| Symbol Name | Data Type | Scope / Source | Direction | Semantics |
|---|---|---|---|---|
| `MAIN.bPlcQConnected` | `BOOL` | `MAIN.TcPOU:16` | R | MC Protocol TCP connection to Mitsubishi Q06UDEH (port 5010) active |
| `MAIN.bPlcQError` | `BOOL` | `MAIN.TcPOU:17` | R | MC Protocol TCP socket communication error flag |
| `MAIN.nPlcQErrorID` | `UDINT` | `MAIN.TcPOU:18` | R | MC Protocol socket raw error ID from Beckhoff client |
| `MAIN.bGlueCycleBusy` | `BOOL` | `MAIN.TcPOU:318` | R | Gantry 4-vertex cycle actively running in publisher (states 10..45) |
| `MAIN.bGlueCycleDone` | `BOOL` | `MAIN.TcPOU:319` | R | All 4 vertices completed and acknowledged |
| `MAIN.bGlueCycleError` | `BOOL` | `MAIN.TcPOU:320` | R | Gantry execution or communication error latched |
| `MAIN.nGlueCurVertex` | `INT` | `MAIN.TcPOU:322` | R | Current vertex index (1..4) |
| `MAIN.nGlueErrorCode` | `INT` | `MAIN.TcPOU:324` | R | Latched publisher error code (501=X boundary, 502=Y boundary, 503=Z, 900..930=timeout/protocol) |
| `MAIN.nGlueCommErrorID` | `UDINT` | `MAIN.TcPOU:323` | R | Raw error ID from MC read/write operations during glue cycle |
| `MAIN.bStopCommandConfirmed` | `BOOL` | `MAIN.TcPOU:321` | R | Verified that M1000=OFF and M105=ON were both acknowledged by PLC-Q via TCP |
| `MAIN.nAutoDispatchState` | `INT` | `MAIN.TcPOU:178` | R | Internal state machine of FIFO-to-robot dispatch sequencer |
| `MAIN.bLegacyMotionPublisherAllow` | `BOOL` | `MAIN.TcPOU:258` | R | Offline gate for QD75 dispatcher (locked FALSE by default) |
| `MAIN.bVisionFeedVerified` | `BOOL` | `MAIN.TcPOU:234` | R | Live camera-to-PLC tracking link verified |
| `MAIN.nQueueCount` | `INT` | `MAIN.TcPOU:264` | R | Current count of items in camera product FIFO (0..8) |
| `MAIN.nQueueHeadProductID` | `UDINT` | `MAIN.TcPOU:265` | R | Product ID of FIFO head item |
| `MAIN.nQueueAccepted` | `UDINT` | `MAIN.TcPOU:275` | R | Total products accepted into FIFO |
| `MAIN.nQueueMissed` | `UDINT` | `MAIN.TcPOU:277` | R | Total products missed |
| `MAIN.nQueueRealCompleted` | `UDINT` | `MAIN.TcPOU:185` | R | Accumulated count of products completed with 4 confirmed PLC ACKs |
| `MAIN.rQueueHeadX_mm` | `LREAL` | `MAIN.TcPOU:272` | R | Current predicted conveyor X position of head product (mm) |
| `MAIN.rQueueHeadY_mm` | `LREAL` | `MAIN.TcPOU:273` | R | Y position of head product (mm) |
| `MAIN.fbPublisher.rTargetX_mm` | `LREAL` | `FB_QD75CommandPublisher:54` | R | Calculated interception target X sent to Mitsubishi D100 (mm) |
| `MAIN.fbPublisher.rTargetY_mm` | `LREAL` | `FB_QD75CommandPublisher:55` | R | Target Y coordinate sent to Mitsubishi D102 (mm) |

---

## 3. Explicit Boundaries & Unmapped Signals (Zero Guesswork)
1. **Actual Tool Center Point (TCP / Real-Time Axis Position)**:
   - Mitsubishi QD75 buffer memory `Md.20` (`U0\G800`, `U0\G900`, `U0\G1000` for Axes 1, 2, 3) is **NOT** read into D registers or transmitted via MC Protocol to TwinCAT in `PLC_Q_Session2_Logic.st`.
   - TwinCAT does **not** possess live robot TCP feedback.
   - UI widgets `S1X`, `S1Y`, `S1Z`, `S1Rz` and `Ov1X`, `Ov1Y`, `Ov1Z`, `Ov1Rz` remain explicitly displayed as `--` (`UNAVAILABLE`) with the note `TCP: NOT AVAILABLE IN PLC IMAGE`.
   - Target vertex coordinates (`MAIN.fbPublisher.rTargetX_mm`, `rTargetY_mm`) are exposed in the Tag Matrix table under group `GANTRY`.

2. **Homed Status (`S1Homed`, `Ov1Homed`)**:
   - Mitsubishi homing is executed in Mitsubishi ladder `MAIN` via `M100/M101` and is strictly forbidden from tracking use.
   - No homing status symbol exists in TwinCAT. Displayed as `UNMAPPED` / `NO DATA`.

3. **Robot Drive Ready (`S1Ready`, `S1Servo`)**:
   - Previously, WPF displayed `MAIN.bServo_Ready` under Station 01 Gantry.
   - Audit revealed `MAIN.bServo_Ready` belongs to the **Ezi-SERVO2 rotary stepper drive** (Degree), **not** the Mitsubishi QD75 gantry servo drives.
   - QD75 drive ready is `X0` in Mitsubishi PLC-Q and is not exposed as a top-level TwinCAT symbol.
   - WPF now clarifies `S1Ready` as `UNMAPPED (QD75)` and `S1Servo` as `ROTARY OK` / `ROTARY OFF` (EziServo).

4. **Operating Mode (`S1Mode`, `Ov1Mode`)**:
   - `MAIN.bLegacyMotionPublisherAllow` is an offline safety gate, not an operating mode selector.
   - WPF now displays `MONITOR` (if allowed) or `PUBLISHER LOCKED` (when disarmed), preventing misleading operator assumptions.

---

## 4. Modified Code Files
- `App/Vision/Vision/LineMailboxProtocol.cs`:
  - Added 10 verified Gantry symbols to `KnownSourceSymbols` HashSet (lines 142-151) so quality classification marks them as `SOURCE_ONLY` / `LIVE` instead of `MISSING`.
- `App/Vision/Dashboard.cs`:
  - Extended `LineStatus` record with Gantry telemetry fields: `PlcQError`, `PlcQErrorId`, `GantryCommErrorId`, `StopCommandConfirmed`, `AutoDispatchState`, `QueueRealCompleted`, `QueueHeadX_mm`, `QueueHeadY_mm`, `GantryTargetX_mm`, `GantryTargetY_mm`.
  - Updated `LineAds.Poll()` with safe `Optional<T>` reads for all new symbols.
  - Extended `UpdateCellViews()` and `PopulateOfflineTagMatrix()` with the complete set of Gantry tags under group `GANTRY`.
  - Enhanced overview status strings in `RefreshDashboardAsync()` to report socket errors and stop confirmation.
- `App/Vision/StationDashboards.cs`:
  - Added `MAIN.bStopCommandConfirmed` and `MAIN.bPlcQError` to `_s1Sensors` signal list.
  - Relabeled EziServo signal to `EziServo rotary ready` to avoid confusion with Gantry QD75 ready.
  - Decoupled `S1Ready` to `UNMAPPED (QD75)` and clarified `S1Mode` as `PUBLISHER LOCKED`.
- `App/Vision/StationOverview.cs`:
  - Updated sensor edge detection to log transitions for `PLC-Q MC Error` and `Gantry Stop Confirmed`.
  - Updated `OverviewGantrySensors` text block to display MC Error and Stop ACK.
- `App/Tests/Program.cs`:
  - Added unit tests for Gantry symbols registration, quality classification, safe readback via `FakeAdsMailboxTransport`, and unmapped assertions for actual TCP / homing.
- `Docs/TAG_MATRIX.md`:
  - Updated Section 3 (GANTRY / CAMERA 1) with all verified symbols and limitations.

---

## 5. Verification Commands & Results
1. `dotnet run --project App/Tests/Tests.csproj`
   - Result: `TOTAL_TEST_FAILURES=0` (all tests passing).
2. `dotnet build App/Vision/PlateVision50.csproj -c Release`
   - Result: `Build succeeded. 0 Error(s), 13 Warning(s)` (warnings inherited from vendor camera wrappers).
