# Design Specification: Vision Pose Tracking & 4-Vertex Gantry Dispensing

**Document ID**: `SPEC-20261029-TRACKING-4PT-GANTRY`  
**Date**: 2026-10-29  
**Status**: DRAFT / SPECIFICATION ONLY (Read-Only Stage)  
**Target Path**: `docs/superpowers/specs/2026-10-29-tracking-four-point-gantry-design.md`  
**System Boundary**: Offline Simulation, WPF UI Telemetry, and C# TwinCAT FIFO Emulation (`E:\Orca\DATN`).  
**Safety Classification**: ZERO hardware writes, ZERO ADS motion commands, ZERO PLC online activation/download.

---

## 1. Goal

Design and formalize the end-to-end data contract and transformation pipeline to:
1. Track square workpieces ($50 \times 50\text{ mm}$ physical outline with internal $40 \times 40\text{ mm}$ dispensing pattern) in real-time camera frames across the conveyor midline.
2. Calculate the workpiece's planar pose (centroid position and orientation).
3. Compute and preserve the exact 4 dispensing vertices at the object's corners through the vision pipeline, trigger event, and tracking FIFO queue.
4. Prepare 15-word motion command payloads (`D100..D114`) conforming to the Mitsubishi QD75 / TwinCAT 3 motion publisher contract without initiating hardware motion.

---

## 2. Current Observed Facts & Audit Baseline

From the read-only audit of `App/Vision/Vision/PlateDetector.cs`, `App/Vision/Trigger/MidlineTracker.cs`, `App/Vision/Vision/BeltCoordinates.cs`, `App/Vision/TwinCAT/CameraPipelineSimulator.cs`, `App/Vision/TwinCAT/AdsFifoBenchBridge.cs`, `App/Vision/MainWindow.xaml.cs`, and `PLC/TwinCAT/DATN/PLC1/`:

1. **Plate Detection (`PlateDetector.cs:36-111`)**:
   - `PlateDetector.AnalyzeAll` locates contours, fits a `RotatedRect rr = Cv2.MinAreaRect(contour)`, and extracts `Point2f[] corners = rr.Points()`.
   - Angle is derived from `Corners[1]` and `Corners[0]` in image space, clamped modulo $90^\circ$ into $[-45^\circ, +45^\circ)$:
     `double theta = -imageAngle; while(theta >= 45) theta -= 90; while(theta < -45) theta += 90;` (`PlateDetector.cs:87-92`).
   - 4 internal glue points (`GluePx` in image pixels, `GlueMm` in optical mm) are synthesized using nominal pitch ($40\text{ mm}$) and local offset iteration: `[(-1,-1), (1,-1), (1,1), (-1,1)]` (`PlateDetector.cs:101-107`).
2. **Midline Triggering (`MidlineTracker.cs:31-112`)**:
   - Global cost-association pairs continuous tracks with detections.
   - Evaluates crossing of the virtual midline: fires a `TriggerEvent` once per track when `previous < 0 && projected >= 0 && (projected - previous >= 0.6) && p.Confidence >= 0.70` (`MidlineTracker.cs:95-96`).
3. **Data Loss at Conveyor Mapping (`BeltCoordinates.cs:35-39`, `MainWindow.xaml.cs:1063`)**:
   - `RecordTrigger` converts the optical detection using `BeltCoordinates.FromDetection(trigger.Pose, ...)`.
   - `BeltCoordinates.FromDetection` accepts and transforms ONLY scalar `(CenterPxX, CenterPxY, AngleDeg)`.
   - **The 4 detected points (`GluePx`, `GlueMm`, `OutlinePx`) are dropped completely.**
4. **Synthetic Corner Re-Creation (`CameraPipelineSimulator.cs:98-102`, `FB_CameraProductQueue.TcPOU:221-233`)**:
   - In both C# SIL simulation and TwinCAT PLC FB, 4 corners are re-synthesized downstream from the scalar center and orientation using nominal pitch ($40\times 40\text{ mm}$):
     `locals = [(+halfX, +halfY), (-halfX, +halfY), (-halfX, -halfY), (+halfX, -halfY)]`.
5. **Corner Indexing Inconsistency**:
   - `PlateDetector.cs:101` orders points as:
     - Point 0: `(-halfX, +halfY)` in optical mm (Top-Left in image)
     - Point 1: `(+halfX, +halfY)` in optical mm (Top-Right in image)
     - Point 2: `(+halfX, -halfY)` in optical mm (Bottom-Right in image)
     - Point 3: `(-halfX, -halfY)` in optical mm (Bottom-Left in image)
   - `CameraPipelineSimulator.cs:99` and `FB_CameraProductQueue.TcPOU:222-227` order vertices 1..4 as:
     - Vertex 1: `(+halfX, +halfY)` (Top-Right in Cartesian)
     - Vertex 2: `(-halfX, +halfY)` (Top-Left in Cartesian)
     - Vertex 3: `(-halfX, -halfY)` (Bottom-Left in Cartesian)
     - Vertex 4: `(+halfX, -halfY)` (Bottom-Right in Cartesian)
   - In WPF overlay (`VisionLabelOverlay.cs:198-202`), callouts P1..P4 display `PlateDetector.GluePx[0..3]`, meaning WPF visualizes points in an index order opposite to the PLC/Simulator vertex dispatch order.
6. **Encoder Synchronization**:
   - In `MainWindow.xaml.cs:1086`, `_offlineQueue.Accept(...)` passes `encoderAtTriggerMm = 0`.
   - Frames from asynchronous camera capture are not time-latched to physical Crevis ST-5114 encoder pulses.
7. **Safety Interlocks Active**:
   - `MAIN.bLegacyMotionPublisherAllow = FALSE` is a hard-coded compile-time gate in PLC code (`MAIN.TcPOU:258`).
   - `AdsFifoBenchBridge.SafetyCheck` enforces fail-closed isolation if any motion permit is active (`AdsFifoBenchBridge.cs:38-45`).
   - 15-word binary packing for QD75 `D100..D114` is implemented and verified in SIL unit tests (`App/Tests/Program.cs:1611-1682`).

---

## 3. Non-Goals

1. **NO Live PLC Code Downloads or Online Changes**: No activation of TwinCAT configurations or writing to running PLC projects.
2. **NO ADS Motion Writes**: Strictly zero writing to `MAIN.bStartGlueCycle`, `MAIN.bTest_WriteM1000`, `MAIN.bAutoDispatchEnable`, or `MAIN.bXYManualPermit`.
3. **NO Arbitrary Shape Segmentation**: The system is tailored for quadrilateral/square workpieces ($50\times 50\text{ mm}$ physical, $40\times 40\text{ mm}$ glue pattern). Generalized polygon or freeform contour dispensing is out of scope.
4. **NO Hardware Wiring Modifications**: Resolving physical wiring conflicts (e.g. Mitsubishi output `Y20` vs `Y25`) is an onsite electrical commissioning task, not a software automation task.

---

## 4. Canonical Coordinate Frames & Units

All calculations across Vision, Conveyor, Simulation, and Motion Publisher must adhere strictly to the following frame definitions:

| Frame Name | Origin | Axis Directions | Units | Notes |
|---|---|---|---|---|
| **Image Pixel Frame** | Top-left corner of sensor image | $+X_{px}$ Right, $+Y_{px}$ Down | Pixels (`double`) | Raw camera coordinates. |
| **Camera Optical Frame** | Calibrated optical center `(OriginPxX, OriginPxY)` | $+X_{mm}$ Right, $+Y_{mm}$ Up | Millimeters (`double`) | Inverted $Y$ via $(oy - py) \cdot mm/px$. |
| **Conveyor Belt Frame** | Optical center projected to belt surface | $+X_{belt}$ Forward along belt toward gantry, $+Y_{belt}$ Left of travel | Millimeters (`double`), Angle $\theta$ in Degrees ($[-45^\circ, +45^\circ)$ CCW) | Transformed from optical frame via `BeltDirection` (`XPlus`, `XMinus`, `YPlus`, `YMinus`). |
| **Robot Base Frame** | Gantry home position reference ($X=0, Y=0$) | $+X_{robot}$ Forward along conveyor travel, $+Y_{robot}$ Across conveyor | Millimeters (`double`) | Workspace envelope: $X \in [0.0, 180.0]\text{ mm}$, $Y \in [0.0, 170.0]\text{ mm}$. |
| **Mitsubishi QD75 Frame** | Gantry Axis 1 ($X$), Axis 2 ($Y$), Axis 3 ($Z$) | Identical orientation to Robot Base Frame | $0.1\ \mu\text{m}$ ($1\text{ mm} = 10,000$) for position, $0.01\text{ mm/min}$ for velocity | Stored in 32-bit little-endian DINT registers (`D100..D113`). |

---

## 5. Canonical Vertex Ordering Standard

To resolve index swapping across `PlateDetector`, `VisionLabelOverlay`, `CameraPipelineSimulator`, and TwinCAT `FB_CameraProductQueue`, all components MUST adopt the standard counter-clockwise Cartesian quadrant indexing:

$$\begin{aligned}
\text{Vertex 1 (Index 1)} &: (+halfX, +halfY) \quad \text{[Cartesian Quadrant I: Front-Left relative to conveyor travel]} \\
\text{Vertex 2 (Index 2)} &: (-halfX, +halfY) \quad \text{[Cartesian Quadrant II: Rear-Left relative to conveyor travel]} \\
\text{Vertex 3 (Index 3)} &: (-halfX, -halfY) \quad \text{[Cartesian Quadrant III: Rear-Right relative to conveyor travel]} \\
\text{Vertex 4 (Index 4)} &: (+halfX, -halfY) \quad \text{[Cartesian Quadrant IV: Front-Right relative to conveyor travel]}
\end{aligned}$$

Where:
- $halfX = \text{PitchX} / 2.0$ (nominal $20.0\text{ mm}$).
- $halfY = \text{PitchY} / 2.0$ (nominal $20.0\text{ mm}$).

### Rotation Transformation
Given workpiece center $(x_c, y_c)$ in conveyor or robot coordinates and total orientation angle $\alpha = \theta + \text{Yaw}$:

$$\begin{pmatrix} X_k \\ Y_k \end{pmatrix} = \begin{pmatrix} x_c \\ y_c \end{pmatrix} + \begin{pmatrix} \cos\alpha & -\sin\alpha \\ \sin\alpha & \cos\alpha \end{pmatrix} \begin{pmatrix} \Delta x_k \\ \Delta y_k \end{pmatrix}$$

Where $(\Delta x_k, \Delta y_k)$ are the local canonical vertex offsets for $k \in \{1, 2, 3, 4\}$.

---

## 6. Preserving Measured `GlueMm` Through the Offline Path

### 6.1. Contract Data Structures

To prevent discarding the 4 detected corner coordinates, the vision-to-FIFO pipeline shall introduce the following immutable records:

```csharp
namespace PlateVision50.Vision;

public sealed record ConveyorVertex(
    short Index,             // 1..4 (strictly canonical quadrant order)
    double X_ConveyorMm,     // mm along conveyor travel
    double Y_ConveyorMm      // mm across conveyor (left of travel)
);

public sealed record TrackedWorkpiecePose(
    int ProductId,                   // Monotonic track ID from MidlineTracker
    string SourceTrackKey,           // Unique key: $"{source}/{productId}"
    DateTime UtcAtCrossing,          // UTC crossing timestamp
    double EncoderAtCrossingMm,      // Conveyor encoder position at crossing instant
    double CenterX_mm,               // Conveyor frame center X (mm)
    double CenterY_mm,               // Conveyor frame center Y (mm)
    double Theta_deg,                // Angle clamped to [-45, +45) deg
    double MeasuredPitchX_mm,        // Optical measured pitch X (mm)
    double MeasuredPitchY_mm,        // Optical measured pitch Y (mm)
    double Confidence,               // Detection confidence [0.0, 1.0]
    ConveyorVertex[] Vertices,       // Exactly 4 canonical vertices in Conveyor frame
    bool IsOpticalMeasured           // true if derived directly from contour features
);
```

### 6.2. Mapping Logic in `BeltCoordinates`
Add an explicit mapping function in `BeltCoordinates.cs`:
```csharp
public static ConveyorVertex[] MapVertices(
    DetectionPoint[] opticalPoints,
    double thetaDeg,
    int width,
    int height,
    double mmPerPixel,
    double originX,
    double originY,
    double offsetX,
    double offsetY,
    BeltDirection direction);
```
- Maps each optical point $(px, py)$ to conveyor $(x_{conv}, y_{conv})$.
- Sorts and indexes points into canonical order $1 \dots 4$ based on their angular position relative to the workpiece centroid.

### 6.3. Extension to `CameraPipelineSimulator`
Extend `CameraPipelineSimulator.Accept` to receive explicit vertices:
```csharp
public Receipt Accept(
    string sourceTrackKey,
    double xMm,
    double yMm,
    double thetaDegrees,
    double encoderAtTriggerMm,
    ConveyorVertex[]? measuredVertices = null);
```
- If `measuredVertices` is provided and valid (length 4, finite coordinates):
  Transform the measured vertices directly from Conveyor frame to Robot frame using camera calibration offsets.
- If `measuredVertices` is null (backward compatibility):
  Synthesize vertices using nominal `PitchXmm, PitchYmm` as currently implemented.

---

## 7. Compatibility Boundary with Existing TwinCAT FIFO

The current deployed TwinCAT 3 function block `FB_CameraProductQueue.TcPOU` accepts only 3 scalar values from the camera:
- `rCenterX_mm` (`MAIN.rCameraCenterX`)
- `rCenterY_mm` (`MAIN.rCameraCenterY`)
- `rAngle_deg` (`MAIN.rCameraAngleDeg`)

### Dual-Path Compatibility Interface:
1. **Offline SIL Path (In-Memory)**:
   - Uses `TrackedWorkpiecePose` and `CameraPipelineSimulator`.
   - Propagates all 4 optical measured vertices end-to-end.
   - Evaluates workspace boundaries against actual measured corners.
2. **Online ADS Bench Path (Hardware TwinCAT Port 851)**:
   - `AdsFifoBenchBridge.EnqueueBenchAsync` continues to send scalar `(CenterX, CenterY, AngleDeg)`.
   - TwinCAT internal logic (`FB_CameraProductQueue.TcPOU:221-233`) generates corners using its internal `rLength_mm, rWidth_mm`.
   - **Zero ADS schema breaking changes**: No ADS variables are added, renamed, or modified.

---

## 8. Exposure Encoder Synchronization (Prerequisite)

### Current Limitation:
Camera acquisition runs asynchronously over USB3/GigE. Conveyor movement during capture latency ($\Delta t \approx 30 \dots 100\text{ ms}$) causes an uncompensated drift:
$$\Delta X_{drift} = V_{conv} \cdot \Delta t \quad (\text{at } 100\text{ mm/s}, 50\text{ ms} = 5.0\text{ mm})$$

### Prerequisite Specification (Commissioning Prerequisite):
1. **Hardware Latch**: Camera optical exposure strobe (Flash Out) must be wired to a fast digital input on the Crevis ST-5114 encoder counter module to latch `EncoderAtExposure_mm` in hardware.
2. **Software Attribution**:
   - In offline simulation: `encoderAtTriggerMm` is explicitly provided as a known simulated trajectory input.
   - In production: `TrackedWorkpiecePose.EncoderAtCrossingMm` must be assigned from the latched hardware counter, NOT from the arrival time of the frame in C#.

---

## 9. Validation & Fail-Closed Rules

The pipeline MUST reject malformed or unsafe inputs at every boundary without throwing unhandled exceptions:

1. **Finite Value Gate**:
   - Every coordinate (`X`, `Y`, `Z`), velocity, angle, and encoder distance MUST satisfy `double.IsFinite(v)`. Any `NaN` or `$\pm\infty$` causes immediate rejection (`ErrorCode = 501`).
2. **Workpiece Geometry Envelope**:
   - Aspect ratio: $w/h \le 1.30$.
   - Fill ratio: $0.64 \le \text{fill} \le 1.12$.
   - Pitch tolerance: $|MeasuredPitchX - 40.0\text{ mm}| \le 2.0\text{ mm}$ and $|MeasuredPitchY - 40.0\text{ mm}| \le 2.0\text{ mm}$.
   - Detection confidence: $\ge 0.70$.
3. **Duplicate Track & Capacity Rejection**:
   - Rejection if `sourceTrackKey` already exists in `_acceptedKeys`.
   - Rejection if queue count $\ge 8$ (`FIFO FULL 8/8`, `ErrorCode = 115`).
4. **On-the-Fly Dynamic Workspace Envelope**:
   - For every vertex $k \in \{1..4\}$ at conveyor velocity $V_{conv}$:
     $$TargetX_k = Vertex_k.X_{robot} + (Encoder_{now} - Encoder_{exposure}) + V_{conv} \cdot t_{fly}$$
     $$\Delta X_{sync} = V_{conv} \cdot t_{sync}$$
     $$TargetY_k = Vertex_k.Y_{robot}$$
   - Strict Boundary Constraints:
     $$0.0\text{ mm} \le TargetX_k \quad \text{AND} \quad (TargetX_k + \Delta X_{sync}) \le 180.0\text{ mm}$$
     $$0.0\text{ mm} \le TargetY_k \le 170.0\text{ mm}$$
     $$0.0\text{ mm} \le Z_{safe} \le Z_{down} \le 52.0\text{ mm}$$
   - Any boundary violation sets `IsValid = false` and `ErrorCode = 501` without queuing motion.

---

## 10. Live ADS & PLC Commissioning Gates (Prerequisites)

The following prerequisites are strictly marked as unverified external conditions. They MUST NOT be assumed true:

| Parameter / Gate | Current Verified State | Commissioning Prerequisite |
|---|---|---|
| `MAIN.bLegacyMotionPublisherAllow` | `FALSE` (`MAIN.TcPOU:258`) | Must remain `FALSE` until electrical safety, axis limits, and emergency stops are signed off. |
| `MAIN.bQueueSimMode` | `FALSE` (`MAIN.TcPOU:260`) | Must remain `FALSE` during normal production; used only during dry-run SIL bench testing. |
| Camera-to-Gantry Distance `rCameraToWorkStart_mm` | Stored as nominal $200.0\text{ mm}$ | Must be physically measured and laser-surveyed on the real frame. |
| Camera Lateral Offset `rCameraOffsetY_mm` | Stored as nominal $85.0\text{ mm}$ | Must be calibrated using a reference target plate. |
| Mitsubishi Output `Y20` vs `Y25` | `Y20` assigned in `PLC_Q_Session2_Logic.st` | `Y20` is Servo Enable/Brake; `Y25` is Glue Pump Trigger. Logic MUST be corrected and audited in GX Works2 prior to live testing. |
| Crevis ST-5114 Factor | Nominal $0.005\text{ mm/pulse}$ | Must be verified against a physical 1-meter linear vernier scale run. |

---

## 11. Required Tests & Verification Suite

All future implementation of this design must be verified against an expanded automated test suite in `App/Tests/Program.cs`:

1. **Vertex Ordering Test**:
   - Verify that `PlateDetector`, `BeltCoordinates`, and `CameraPipelineSimulator` produce identical vertex ordering for orientations $0^\circ, 15^\circ, -30^\circ, 44^\circ$.
2. **Measured Point Propagation Test**:
   - Verify that introducing a deliberate $42 \times 38\text{ mm}$ asymmetrical pitch contour propagates the exact measured coordinates through `TriggerEvent` into `Receipt.Corners`, rather than defaulting to $40 \times 40\text{ mm}$.
3. **Workspace Overtravel Tests**:
   - Verify that any vertex exceeding $X=180.0\text{ mm}$ or $Y=170.0\text{ mm}$ (including dynamic sync lead) produces `IsValid = false` and `ErrorCode = 501`.
4. **Duplicate & Non-Finite Rejection Tests**:
   - Verify that `NaN`, $\pm\infty$, and duplicate track IDs are rejected cleanly.
5. **Zero-Hardware Invariance**:
   - Assert that `gantryTransport.Writes` contains zero writes to `bStartGlueCycle`, `bTest_WriteM1000`, or motion registers.

---

## 12. Architectural Summary

```
[ Camera Frame ]
       │
       ▼
[ PlateDetector.AnalyzeAll ] ───► Extract RotatedRect, Angle ([-45, 45)°), and 4 Optical Points
       │
       ▼
[ MidlineTracker.Update ] ──────► Cross Midline ──► Emit TriggerEvent with PlateResult
       │
       ▼
[ BeltCoordinates.MapVertices ] ─► Map (X, Y, θ) + 4 Optical Points to Canonical Conveyor Order:
                                   P1: (+halfX, +halfY), P2: (-halfX, +halfY)
                                   P3: (-halfX, -halfY), P4: (+halfX, -halfY)
       │
       ▼
[ TrackedWorkpiecePose ] ────────► Preserves Both Centroid Pose & 4 Measured Vertices
       │
       ├──────────────────────────────────────────────┐
       ▼                                              ▼
[ Offline SIL Path ]                        [ Live ADS Bench Path ]
(CameraPipelineSimulator)                   (AdsFifoBenchBridge)
  - Accepts 4 measured vertices               - Sends (CenterX, CenterY, Angle) to PLC
  - Dynamic encoder propagation               - PLC synthesizes nominal corners
  - Builds 15-word QD75 command (D100..D114)   - Zero breaking ADS schema changes
  - Zero ADS writes                           - Gated by bLegacyMotionPublisherAllow=FALSE
```
