# OFFLINE CAMERA TRACKING → FIFO → TWINCAT QD75 COORDINATE SIMULATION MANUAL
**Date:** 2026-10-25  
**Workspace:** `E:\Orca\DATN`  
**Classification:** Software-Only Preparation & Safe Offline Simulation Guide  
**Post-Safety Status:** Electrical/mechanical safety phase acknowledged by user; zero physical motion, zero PLC download/activation, zero ADS motion writes, strictly offline verified.

---

## 1. Executive Summary

Following completion of the electrical and mechanical safety review, this document establishes a **pure software-in-the-loop (SIL) simulation path** to exercise and verify the complete tracking-to-robot coordinate pipeline:
$$\text{Camera Midline Crossing} \longrightarrow \text{Conveyor Calibration} \longrightarrow \text{FIFO Queue} \longrightarrow \text{TwinCAT QD75 4-Vertex Dispatch} \longrightarrow \text{Mitsubishi D100..D114 Payload}$$

### Safety Boundary (Zero Risk Policy)
- **Zero TwinCAT Activation / Download**: No PLC project activation or online download.
- **Zero ADS / MC Writes to Motion Registers**: `MAIN.bStartGlueCycle`, `MAIN.bTest_WriteM1000`, `MAIN.bTest_WriteD100` remain completely untouched.
- **Zero PLC State Changes**: TwinCAT remains in its existing state (no Run/Stop commands issued).
- **Zero Servo / Axis Movement**: Gantry and rotary axes are not commanded.
- **Fail-Closed Production Posture**: When offline simulation mode is unchecked, the application defaults to strict fail-closed read-only behavior.
- **PLC Source Code**: Strictly untouched (`MAIN.TcPOU`, `FB_CameraProductQueue.TcPOU`, `FB_QD75CommandPublisher.TcPOU` not edited).

---

## 2. Pipeline Architecture & Coordinate Transformation Contract

The offline simulation path models the exact mathematical and protocol contracts defined in TwinCAT 3 POUs without requiring hardware:

```
[Camera Image (800x600 / 1440x1080)]
                 │
                 ▼
[PlateDetector: 50x50mm Contour Detection]
  - Center: (CenterPxX, CenterPxY), Angle: AngleDeg, Pitch: 40x40mm
                 │
                 ▼
[MidlineTracker: 50% FOV Midline Crossing]
  - Tracks continuous centroid trajectories; fires TriggerEvent once per workpiece
                 │
                 ▼
[BeltCoordinates: Image → Conveyor Transformation]
  - Orthographic projection: (X_mm, Y_mm, ThetaDeg) with conveyor offset & direction
                 │
                 ▼
[CameraPipelineSimulator: Camera → Robot Coordinate Transformation]
  - xc = -CameraToWorkStartMm + CameraFineXmm + xMm*cos(yaw) - yMm*sin(yaw)
  - yc = CameraOffsetYmm + xMm*sin(yaw) + yMm*cos(yaw)
  - 4 Corners: P_j = (xc + dx_j*cosθ - dy_j*sinθ, yc + dx_j*sinθ + dy_j*cosθ)
  - Dynamic tracking: P_j.X(encoder) = P_j.X(exposure) + (currentEncoder - exposureEncoder)
                 │
                 ▼
[TwinCatQd75Command: TwinCAT FB_QD75CommandPublisher Dispatch Contract]
  - Lead compensation: Target_X = Corner.X + (V_conv * t_fly)
  - Sync distance: DeltaX_Sync = V_conv * t_sync
  - Speeds: Speed_Approach = 500 mm/s * 6000, Speed_Sync = V_conv * 6000
  - Work heights: Target_Z_Down = 4.0mm (40,000), Target_Z_Safe = 0.0mm (0)
  - Boundary checking: X ∈ [0, 180] mm, Y ∈ [0, 170] mm, Target_X + DeltaX_Sync ≤ 180 mm
                 │
                 ▼
[15-Word Mitsubishi D100..D114 MC Binary Image]
  - D100..D101: Target_X (0.1 µm, little-endian DINT)
  - D102..D103: Target_Y (0.1 µm, little-endian DINT)
  - D104..D105: Target_Z_Down (0.1 µm, 40,000)
  - D106..D107: Speed_Approach (0.01 mm/min, 3,000,000)
  - D108..D109: Speed_Sync (0.01 mm/min, V_conv * 6000)
  - D110..D111: DeltaX_Sync (0.1 µm)
  - D112..D113: Target_Z_Safe (0.1 µm, 0)
  - D114: Vertex_Index (1..4)
```

---

## 3. How to Run the Offline Tests

### Method A: Automated CLI Test Suite (Recommended for CI/Verification)
Run the complete automated unit test suite from the terminal:
```bash
dotnet run --project App/Tests/Tests.csproj
```
**Expected Observable Result:**
```
PASS offline sim: empty snapshot has zero count and no head product
PASS offline sim: accepted trigger into virtual FIFO
PASS offline sim: approaching workpiece distance to work is 200mm and inside is false
PASS offline sim: head inside workspace is false when X < 0
PASS offline sim: QD75 command rejected with error 501 when workpiece outside workspace
PASS offline sim: in-workspace snapshot reports inside=true, HeadX=20mm, remaining=0mm
PASS offline sim: IsHeadInsideWorkspace is true at encoder 320mm
PASS offline sim: vertex 1 command is valid
PASS offline sim: vertex 1 Target_X is 520,000 (52.0 mm)
PASS offline sim: vertex 1 Target_Y is 1,050,000 (105.0 mm)
PASS offline sim: vertex 1 Target_Z_Down is 40,000 (4.0 mm)
PASS offline sim: vertex 1 Speed_Approach is 3,000,000 (500 mm/s)
PASS offline sim: vertex 1 Speed_Sync is 600,000 (100 mm/s)
PASS offline sim: vertex 1 DeltaX_Sync is 60,000 (6.0 mm)
PASS offline sim: vertex 1 Target_Z_Safe is 0
PASS offline sim: vertex 1 Vertex_Index is 1
PASS offline sim: Melsec payload is exactly 15 words (30 bytes)
PASS offline sim: D100..D101 Target_X unpacks correctly
PASS offline sim: D102..D103 Target_Y unpacks correctly
PASS offline sim: D104..D105 Target_Z_Down unpacks correctly
PASS offline sim: D106..D107 Speed_Approach unpacks correctly
PASS offline sim: D108..D109 Speed_Sync unpacks correctly
PASS offline sim: D110..D111 DeltaX_Sync unpacks correctly
PASS offline sim: D112..D113 Target_Z_Safe unpacks correctly
PASS offline sim: D114 Vertex_Index is 1
PASS offline sim: vertex 2, 3, 4 commands are all valid
PASS offline sim: overtravel beyond X=180mm rejected with error 501
PASS offline sim: zero speed rejected with error 501
PASS offline sim: invalid vertex index 5 rejected with error 501
PASS offline sim: CompleteHead empties queue and rejects command on empty queue
TOTAL_TEST_FAILURES=0
```

---

### Method B: Graphical User Interface (WPF Application)

#### 1. Launch the Application
```bash
dotnet run --project App/Vision/PlateVision50.csproj
```
Or open `OPEN_DATN_WPF.cmd`.

#### 2. Run Self-Test Benchmark
1. Navigate to the **SETTINGS / CALIBRATION** tab (or right panel).
2. Locate the box **OFFLINE PIPELINE BENCH · NO ROBOT MOTION**.
3. Click the button: **`RUN OFFLINE 4-POINT / ACK / ENCODER TEST`**.
4. **Observable Verification:**
   - Status text displays:
     ```
     PASS: Trigger → FIFO → 4 corners → TwinCAT QD75 contract (D100..D114 15 words) verified; PC memory only, 0 ADS writes.
     ```
   - Main status bar displays:
     ```
     OFFLINE TEST PASS: Trigger → FIFO → TwinCAT QD75 coordinate contract verified (0 ADS writes).
     ```
   - FIFO panel displays:
     - `Count`: 1
     - `Head`: 1
     - `Progress`: Target X/Y coordinates and calculated D100..D114 word values.
     - `Glue Points`: 4 coordinates ($P_1..P_4$) in millimeters.

#### 3. Run Simulated Tracking with Video Playback
1. In the **CAMERA / VIDEO** panel, click **OPEN VIDEO** and select a test video (e.g. `1.avi`).
2. In **OFFLINE PIPELINE BENCH**, check:
   - `[x] Enable simulated FIFO triggers during detection (no PLC commands)`
3. Observe status indicator:
   - `VIRTUAL FIFO ENABLED · no ADS write; each Track ID is counted once.`
   - `OFFLINE SIMULATION ACTIVE · ZERO ADS WRITES · Virtual FIFO ready`
4. Click **START**.
5. As phôi (workpieces) pass across the vertical/horizontal midline:
   - Center crossing is detected and logged in the trigger table as:
     `Delivery: VIRTUAL FIFO ACK #N`
     `Plc: SIMULATED 4 POINTS - NO PLC WRITE`
   - HUD table updates in real time with FIFO count, head product, and glue points.
   - Zero ADS packets are transmitted to TwinCAT or Mitsubishi PLC.

---

## 4. Live Execution Authorization Status & Remaining Blockers

The following table explicitly distinguishes what has been verified in software vs. what remains **machine-authorized only** and strictly blocked from automatic motion:

| Component | Status | Location / Condition | Description / Reason for Block |
|---|:---:|---|---|
| **Software Coordinate Transform** | **VERIFIED** | `CameraPipelineSimulator.cs:53-62` | Mathematically identical to `FB_CameraProductQueue.TcPOU:197-233` |
| **QD75 D100..D114 Command Contract** | **VERIFIED** | `CameraPipelineSimulator.cs:175-230` | 15 words, $0.1\ \mu\text{m}$ & $0.01\text{ mm/min}$ scaling, error 501 boundary check |
| **WPF Offline Telemetry UI** | **VERIFIED** | `MainWindow.xaml.cs:161-184, 1073-1081` | Real-time display of simulated FIFO head and corners without ADS connection |
| **TwinCAT Queue Motion Gate** | **BLOCKED** | `MAIN.TcPOU:361` | `cQueueMotionCommissioned : BOOL := FALSE;` locks `nAutoDispatchState := 100` |
| **TwinCAT Auto Commissioned Gate** | **BLOCKED** | `MAIN.TcPOU:363` | `cLineAutoCommissioned : BOOL := FALSE;` rejects HMI line start with error 11 |
| **Motion Publisher Gate** | **BLOCKED** | `MAIN.TcPOU:258` | `bLegacyMotionPublisherAllow : BOOL := FALSE;` disarms `fbPublisher` |
| **Vision Feed Verified Flag** | **BLOCKED** | `MAIN.TcPOU:234` | `bVisionFeedVerified : BOOL := FALSE;` prevents automatic dispatch |
| **Camera-to-Robot Distance** | **NOT COMMISSIONED** | `MAIN.TcPOU:236` | `rCameraToWorkStart_mm : LREAL := 0.0;` requires physical tape/gage measurement |
| **Hardware Exposure Latch** | **NOT COMMISSIONED** | Crevis ST-5114 & Camera | No physical shutter trigger signal coupled to encoder capture register |
| **Mitsubishi PLC-Q Output Wiring** | **BLOCKED** | Cabinet Wiring vs. ST File | Physical outputs `Y20..Y22` are Servo Enablers; `Y23` is Brake; `Y25` is Glue Pump. Historical ST code mistakenly had `Y20` as Glue Valve. |
| **Hardware Safety Relay / E-Stop** | **BLOCKED** | Cabinet E-Stop Circuit | Software M105 cancellation is non-safety TCP; dual-channel certified safety relay circuit required before live motion. |

---

## 5. Summary of Files Changed

1. **`App/Vision/TwinCAT/CameraPipelineSimulator.cs`**:
   - Added `TwinCatQd75Command` record with `ToMelsecWords()` method to format 15 contiguous 16-bit words for Mitsubishi Q-Series D100..D114 registers.
   - Added `IsHeadInsideWorkspace(double currentEncoderMm, ...)` to perform strict boundary validation against TwinCAT stroke limits ($X \in [0, 180]$, $Y \in [0, 170]$).
   - Added `GetSnapshot(double currentEncoderMm, double speedMmS)` returning a `FifoSnapshot` to drive WPF HUD widgets offline.
   - Added `BuildQd75Command(...)` calculating lead drift, sync distance, speeds, and Z working heights matching TwinCAT `FB_QD75CommandPublisher`.

2. **`App/Vision/MainWindow.xaml.cs`**:
   - Enhanced `TestOfflinePipeline_Click` to execute a comprehensive end-to-end benchmark covering corner conversion, encoder propagation, workspace entry, 4-vertex QD75 commands, boundary rejection, and queue completion.
   - Updated `OfflinePipelineChanged`, `RecordTrigger`, and `OnAdsTick` fallback to update `FifoCount`, `FifoProduct`, `FifoProgress`, and `FifoGluePoints` directly from the virtual FIFO when offline simulation is enabled, eliminating "PLC not available" during offline demonstrations while maintaining fail-closed posture when simulation is disabled.

3. **`App/Tests/Program.cs`**:
   - Added 32 new unit tests under Section 16 verifying the trigger $\to$ FIFO $\to$ TwinCAT QD75 coordinate contract, snapshot generation, Melsec 15-word binary packing, 4-vertex sequencing, and out-of-boundary rejection.
   - 100% PASS (0 failures).
