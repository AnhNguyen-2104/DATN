# Checkpoint: Vision Pose Tracking & 4-Vertex Gantry Dispensing Offline Contract

**Date:** 2026-10-29  
**Status:** OFFLINE VERIFIED / ZERO HARDWARE ACTUATION  
**System Boundary:** `E:\Orca\DATN` (C# SIL Simulation, WPF UI Telemetry, Fake ADS Mailbox)  
**Safety Classification:** Strictly zero hardware writes, zero ADS motion commands, zero live PLC downloads.  
**Design Spec:** `docs/superpowers/specs/2026-10-29-tracking-four-point-gantry-design.md`  
**Implementation Plan:** `docs/superpowers/plans/2026-10-29-tracking-four-point-gantry.md`  
**Tag Matrix Document:** `Docs/TAG_MATRIX.md` (Section 8)

---

## 1. Executive Summary

This checkpoint audits the completion of Tasks 1 through 5 of the Vision Pose Tracking & 4-Vertex Gantry Dispensing plan. The implementation formalizes the data contract and coordinate transformations required to track square workpieces ($50 \times 50\text{ mm}$ physical contour, $40 \times 40\text{ mm}$ dispensing pattern) across the conveyor midline, calculate planar poses, preserve the exact 4 optical dispensing vertices through the C# Software-in-the-Loop (SIL) simulation, and construct 15-word binary QD75 motion command payloads (`D100..D114`) conforming to the Mitsubishi QD75 buffer memory layout.

**Crucial Safety Boundary:** All implementation and verification in this phase were executed entirely offline in C# software simulation and automated test suites. Strictly **zero** writes were performed to physical PLC registers, ADS motion tags (`MAIN.bStartGlueCycle`, `MAIN.bTest_WriteM1000`), or hardware outputs. Hardware motion remains disarmed by the compile-time gate `MAIN.bLegacyMotionPublisherAllow = FALSE`.

---

## 2. Modified Files & Commit Trail

| Task | Target Files | Key Changes | Commits |
|---|---|---|---|
| **Task 1** | `App/Vision/Vision/BeltCoordinates.cs`<br>`App/Tests/Program.cs` | Defined immutable contract records `ConveyorVertex` and `TrackedWorkpiecePose`; implemented `MapVertices` with counter-clockwise Cartesian quadrant ordering and strict geometry validation. | `abe512f`<br>`3d7458d` |
| **Task 2** | `App/Vision/TwinCAT/CameraPipelineSimulator.cs`<br>`App/Tests/Program.cs` | Extended `CameraPipelineSimulator.Accept` to receive `measuredVertices`; preserved measured vertices in `Receipt.Corners`; added strict finite/canonical validation and yaw coupling in `BuildQd75Command`. | `abe512f`<br>`3d7458d` |
| **Task 3** | `App/Vision/Vision/VisionLabelOverlay.cs`<br>`App/Tests/Program.cs` | Reconciled UI callout lines P1..P4 to use `BeltCoordinates.MapVertices`, aligning visual corner labels with Cartesian Quadrants I..IV matching motion command indices. | `c1e1448` |
| **Task 4** | `App/Vision/MainWindow.xaml.cs`<br>`App/Tests/Program.cs` | Wired optical `GluePx` corner mapping in `RecordTrigger`; propagated measured vertices into `_offlineQueue.Accept`; updated sidebar telemetry and CSV logging; preserved camera transport mode in `SaveSlot`. | `48abfee`<br>`de6ddb4` |
| **Task 5** | `Docs/TAG_MATRIX.md`<br>`README.md`<br>`PROJECT_INDEX.md`<br>`Docs/CHECKPOINT_20261029_TRACKING_4PT_GANTRY.md` | Formalized offline contract documentation, tag matrix audit, system cross-references, zero ADS write affirmation, and remaining commissioning prerequisites. | Pending |

---

## 3. Canonical Coordinate Contract & Quadrant Order

All components (Vision detector, UI overlay, C# SIL simulator, and TwinCAT/QD75 motion publisher) adhere to standard Cartesian quadrant indexing:

$$\begin{aligned}
\text{Vertex 1 (Index 1)} &: (+halfX, +halfY) \quad \text{[Cartesian Quadrant I: Front-Left relative to conveyor travel]} \\
\text{Vertex 2 (Index 2)} &: (-halfX, +halfY) \quad \text{[Cartesian Quadrant II: Rear-Left relative to conveyor travel]} \\
\text{Vertex 3 (Index 3)} &: (-halfX, -halfY) \quad \text{[Cartesian Quadrant III: Rear-Right relative to conveyor travel]} \\
\text{Vertex 4 (Index 4)} &: (+halfX, -halfY) \quad \text{[Cartesian Quadrant IV: Front-Right relative to conveyor travel]}
\end{aligned}$$

Where nominal $halfX = 20.0\text{ mm}$ and $halfY = 20.0\text{ mm}$.

### Transformation Formulation:
Given workpiece center $(x_c, y_c)$ in conveyor or robot coordinates and total orientation angle $\alpha = \theta + \text{Yaw}$:

$$\begin{pmatrix} X_k \\ Y_k \end{pmatrix} = \begin{pmatrix} x_c \\ y_c \end{pmatrix} + \begin{pmatrix} \cos\alpha & -\sin\alpha \\ \sin\alpha & \cos\alpha \end{pmatrix} \begin{pmatrix} \Delta x_k \\ \Delta y_k \end{pmatrix}$$

Where $(\Delta x_k, \Delta y_k)$ are the local canonical vertex offsets for $k \in \{1, 2, 3, 4\}$.

### UI Overlay Reconciliation:
Prior to Task 3, `VisionLabelOverlay.cs` iterated raw `PlateResult.GluePx[0..3]` directly, displaying points in an optical layout inverted relative to the motion command publisher. With `BeltCoordinates.MapVertices`, callouts P1..P4 consistently correspond to Quadrants I..IV across UI graphics, sidebar diagnostics, and binary command buffers.

---

## 4. Measured GlueMm/GluePx Propagation Through C# SIL

### Contract Records:
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

### End-to-End Propagation Flow:
1. `PlateDetector.AnalyzeAll` detects contour and extracts `GluePx` / `GlueMm`.
2. `MidlineTracker.Update` tracks workpiece and triggers `TriggerEvent` containing full `PlateResult`.
3. `RecordTrigger` in `MainWindow.xaml.cs` calls `BeltCoordinates.MapVertices(trigger.Pose.GluePx, ...)`.
4. `_offlineQueue.Accept(..., measuredVertices)` receives the measured vertex array.
5. `CameraPipelineSimulator` validates finite coordinates, canonical indices 1..4, and non-degeneracy, then transforms vertices into Robot Base coordinates preserving optical variations (e.g. asymmetrical pitch $42 \times 38\text{ mm}$ tested and preserved without snapping to $40 \times 40\text{ mm}$).
6. If optical points are null, degenerate, or collinear, the pipeline safely catches the error and falls back to nominal geometric synthesis without crashing or corrupting the queue.

---

## 5. Mitsubishi QD75 Motion Command Payload (`D100..D114`)

The motion command generated by `CameraPipelineSimulator.BuildQd75Command` packs into exactly 15 16-bit words (30 bytes) matching the PLC-Q buffer register map:

| Register | Field Name | Data Type | Units / Scaling | Description |
|---|---|---|---|---|
| `D100..D101` | `Target_X` | 32-bit DINT | $0.1\ \mu\text{m}$ ($1\text{ mm} = 10,000$) | Axis 1 (X) target coordinate in robot frame |
| `D102..D103` | `Target_Y` | 32-bit DINT | $0.1\ \mu\text{m}$ ($1\text{ mm} = 10,000$) | Axis 2 (Y) target coordinate in robot frame |
| `D104..D105` | `Target_Z_Down` | 32-bit DINT | $0.1\ \mu\text{m}$ ($1\text{ mm} = 10,000$) | Axis 3 (Z) dispensing height |
| `D106..D107` | `Speed_Approach` | 32-bit DINT | $0.01\text{ mm/min}$ ($1\text{ mm/s} = 6,000$) | Rapid traverse speed to interception |
| `D108..D109` | `Speed_Sync` | 32-bit DINT | $0.01\text{ mm/min}$ ($1\text{ mm/s} = 6,000$) | Conveyor tracking synchronous speed |
| `D110..D111` | `DeltaX_Sync` | 32-bit DINT | $0.1\ \mu\text{m}$ ($1\text{ mm} = 10,000$) | Dynamic tracking distance lead |
| `D112..D113` | `Target_Z_Safe` | 32-bit DINT | $0.1\ \mu\text{m}$ ($1\text{ mm} = 10,000$) | Retract clearance height |
| `D114` | `Vertex_Index` | 16-bit INT | $1 \dots 4$ | Current dispensing corner index |

### Workspace & Dynamics Validation:
- Envelope boundaries: $X \in [0.0, 180.0]\text{ mm}$, $Y \in [0.0, 170.0]\text{ mm}$, $Z \in [0.0, 52.0]\text{ mm}$.
- Dynamic sync overtravel check: Rejects commands with `ErrorCode = 501` if $(TargetX + \Delta X_{sync}) > 180.0\text{ mm}$.
- Non-finite check: Any coordinate or parameter with `NaN` or $\pm\infty$ is rejected immediately.

---

## 6. Zero ADS Write Boundary & Strict Safety Isolation

| Subsystem / Symbol | Action in this Phase | Status / Enforcement Mechanism |
|---|---|---|
| `MAIN.bStartGlueCycle` | **NO WRITE** | Hard-coded zero-write policy; no ADS client references write access. |
| `MAIN.bTest_WriteM1000` | **NO WRITE** | Test motion register unreferenced by application code. |
| `MAIN.bLegacyMotionPublisherAllow` | **UNCHANGED** | Compile-time gate in TwinCAT `MAIN.TcPOU:258` strictly `FALSE`. |
| `MAIN.bAutoDispatchEnable` | **NO WRITE** | Auto-dispatch disabled. |
| `MAIN.bXYManualPermit` | **NO WRITE** | Manual jog permit disabled. |
| Crevis ST-1228 DI0 | **READ ONLY** | AUX feedback only; not treated as certified safety E-stop. |
| TwinCAT Port 851 ADS | **TELEMETRY ONLY** | Read-only polling via `FakeAdsMailboxTransport` in tests; live ADS disabled in offline simulation. |
| TwinCAT Solution | **NO DOWNLOAD** | No online changes, no activation of `DATN.tsproj` configuration. |
| Mitsubishi PLC-Q | **NO DOWNLOAD** | No online write or changes to GX Works2 ladder project. |

---

## 7. Automated Test Suite Results

The full automated test suite was executed offline against `App/Tests/Tests.csproj` and `App/Vision/PlateVision50.csproj`:

### Test Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo
dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```

**Result:**
- Total Failures: `TOTAL_TEST_FAILURES=0`
- Coverage includes:
  - Canonical vertex quadrant ordering (Quad I..IV at angles $0^\circ, 15^\circ, -30^\circ, 44^\circ$)
  - Geometric collinearity and coincidence rejection
  - Asymmetric pitch ($42 \times 38\text{ mm}$) optical measurement propagation
  - `CameraPipelineSimulator` measured vertex acceptance, yaw coupling, and FIFO ordering
  - Non-finite coordinate rejection (`NaN`, $\pm\infty$) and atomic FIFO rollback
  - QD75 15-word binary packing (`D100..D114`) and dynamic sync overtravel rejection
  - UI label overlay quadrant mapping contract
  - End-to-end trigger event wiring (`RecordTrigger` -> `_offlineQueue.Accept`)
  - Camera slot persistence normalization (`SaveSlot` mode preservation)

### WPF Application Build:
```cmd
dotnet build App/Vision/PlateVision50.csproj -c Debug -p:PylonRoot="C:\Program Files\Basler\pylon" --nologo
```

**Result:**
- Build Succeeded: `0 Warning(s), 0 Error(s)`
- Runtime target: `net8.0-windows`

---

## 8. Remaining Commissioning Prerequisites (Unverified External Conditions)

The following items are hardware and field commissioning prerequisites that **CANNOT** be verified in software simulation and remain unverified on the physical machine. Live gantry motion MUST NOT be authorized until all prerequisites are completed:

1. **Exposure Hardware Encoder Latch:**
   - Asynchronous camera capture over USB3/GigE introduces latency ($\Delta t \approx 30 \dots 100\text{ ms}$), causing positional drift ($\Delta X \approx 5\text{ mm}$ at $100\text{ mm/s}$).
   - **Prerequisite:** Connect camera optical strobe out (Flash Out) to Crevis ST-5114 high-speed latch input to latch `EncoderAtExposure_mm` in hardware. Assign this value to `TrackedWorkpiecePose.EncoderAtCrossingMm`.

2. **Physical Camera Offset Calibration:**
   - Coordinate frames currently use nominal estimates: $rCameraToWorkStart\_mm = 200.0\text{ mm}$ and $rCameraOffsetY\_mm = 85.0\text{ mm}$.
   - **Prerequisite:** Perform optical-to-robot coordinate calibration using a calibrated calibration plate and laser distance measurement on the physical frame.

3. **Crevis ST-5114 Encoder Scaling:**
   - Nominal factor is configured as $0.11911875\text{ mm/count}$ (360 PPR, x4 quadrature, 54.6 mm wheel diameter).
   - **Prerequisite:** Run a physical 1.000-meter belt travel test against a vernier scale to verify encoder scale factor.

4. **Mitsubishi PLC-Q Single-Writer & Valve Output Verification:**
   - In `PLC_Q_Session2_Logic.st`, output `Y20` was previously aliased to both Servo 1 Enable and Glue Valve.
   - Glue valve has been updated in source to `Y25` (physical pump trigger), keeping `Y20..Y23` reserved exclusively for Servo Enables and Brake in `MAIN`.
   - **Prerequisite:** Perform an online Read/Verify in GX Works2 to verify coil single-writer integrity, M1000/M1001 handshake timing, and M105 software abort handling.

5. **Certified Safety Interlock & Travel Limits:**
   - Crevis ST-1228 DI0 is software feedback, not certified functional safety.
   - **Prerequisite:** Inspect and verify hardwired safety relays, dual-channel E-stop circuits, and physical axis limit switches before setting `MAIN.bLegacyMotionPublisherAllow = TRUE`.
