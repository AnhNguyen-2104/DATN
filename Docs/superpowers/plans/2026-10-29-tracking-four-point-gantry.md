# Vision Pose Tracking & 4-Vertex Gantry Dispensing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement an end-to-end planar workpiece pose tracking and 4-vertex gantry dispensing coordinate pipeline that preserves optical corner measurements through the virtual trigger and FIFO simulation queue into 15-word QD75 motion command payloads without initiating hardware motion or ADS writes.

**Architecture:** Transform optical detections into canonical Cartesian quadrant-ordered conveyor vertices ($P1 \dots P4$) via a pure geometric contract in `BeltCoordinates`. Propagate measured vertices through `TrackedWorkpiecePose` into the in-memory `CameraPipelineSimulator` for dynamic conveyor tracking and QD75 binary packing (`D100..D114`). Reconcile UI label overlays to display canonical quadrant ordering while maintaining strict zero-hardware safety boundaries.

**Tech Stack:** C# 12, .NET 8 WPF (`net8.0-windows`), OpenCvSharp4, Beckhoff TwinCAT ADS (read-only telemetry interface), custom regression test runner in `App/Tests/Program.cs`.

**Spec:** `docs/superpowers/specs/2026-10-29-tracking-four-point-gantry-design.md`

## Global Constraints

- **Platform Target**: .NET 8 x64 (`net8.0-windows`).
- **Safety Classification**: ZERO hardware writes, ZERO ADS motion commands, ZERO PLC online activation/download.
- **Symbol Isolation**: Strictly zero writes to `MAIN.bStartGlueCycle`, `MAIN.bTest_WriteM1000`, `MAIN.bAutoDispatchEnable`, or `MAIN.bXYManualPermit`.
- **PLC Source Invariance**: No modifications to `PLC/TwinCAT/` or `PLC/Mitsubishi/` source files.
- **Fail-Closed Validation**: Every coordinate, speed, angle, and distance MUST be checked with `double.IsFinite(v)`. Reject `NaN` and $\pm\infty$ with error code 501.
- **Workspace Envelope**: Gantry envelope bounds $X \in [0.0, 180.0]\text{ mm}$, $Y \in [0.0, 170.0]\text{ mm}$, $Z \in [0.0, 52.0]\text{ mm}$.
- **QD75 Motion Word Contract**: Exactly 15 16-bit words (`D100..D114`) little-endian.
- **Canonical Ordering**: Vertex 1 $(+halfX, +halfY)$ [Quad I], Vertex 2 $(-halfX, +halfY)$ [Quad II], Vertex 3 $(-halfX, -halfY)$ [Quad III], Vertex 4 $(+halfX, -halfY)$ [Quad IV].

## Review Focus

1. **Non-finite coordinates in measured vertices**: Passing `NaN` or $\pm\infty$ in `ConveyorVertex` coordinates to `CameraPipelineSimulator.Accept` must reject immediately with `ArgumentException` without corrupting queue state. (Owned by Task 2, Step 1)
2. **Vertex count mismatch**: Passing an array whose length is not exactly 4 (e.g. 3 or 5 vertices) to `CameraPipelineSimulator.Accept` must reject immediately with `ArgumentException`. (Owned by Task 2, Step 1)
3. **Collinear or degenerate optical points in `MapVertices`**: If optical detection produces identical points or collapsed contours, `MapVertices` must handle zero-division safely and deterministically assign indices 1..4 or reject with `ArgumentException`. (Owned by Task 1, Step 1)
4. **Asymmetrical pitch exceeding physical tolerance**: If measured pitch deviates by $> 2.0\text{ mm}$ from nominal $40.0\text{ mm}$ ($|PitchX - 40.0| > 2.0$ or $|PitchY - 40.0| > 2.0$), `TrackedWorkpiecePose` validation must set `IsOpticalMeasured = false`. (Owned by Task 1, Step 1)
5. **Dynamic sync lead pushes vertex overtravel at maximum belt speed**: A corner inside the static workspace at trigger ($X = 175.0\text{ mm}$) that exceeds $X_{max} = 180.0\text{ mm}$ once $V_{conv} \cdot t_{sync}$ lead is applied must produce `IsValid = false` and `ErrorCode = 501` in `BuildQd75Command`. (Owned by Task 2, Step 1)

---

### Task 1: Core Canonical Vertex Mapping & Tracked Pose Contracts

**Files:**
- Modify: `App/Vision/Vision/BeltCoordinates.cs:1-40`
- Test: `App/Tests/Program.cs`

**Interfaces:**
- Consumes:
  - `PlateVision50.Vision.DetectionPoint(double X, double Y)` from `App/Vision/Vision/PlateDetector.cs:8`
  - `PlateVision50.Vision.PlateResult` from `App/Vision/Vision/PlateDetector.cs:9-14`
  - `PlateVision50.Vision.VisionOptions` from `App/Vision/Vision/PlateDetector.cs:16-28`
  - `PlateVision50.Trigger.BeltDirection` from `App/Vision/Trigger/MidlineTracker.cs:8`
- Produces:
  - `public sealed record ConveyorVertex(short Index, double X_ConveyorMm, double Y_ConveyorMm);`
  - `public sealed record TrackedWorkpiecePose(int ProductId, string SourceTrackKey, DateTime UtcAtCrossing, double EncoderAtCrossingMm, double CenterX_mm, double CenterY_mm, double Theta_deg, double MeasuredPitchX_mm, double MeasuredPitchY_mm, double Confidence, ConveyorVertex[] Vertices, bool IsOpticalMeasured);`
  - `public static ConveyorVertex[] BeltCoordinates.MapVertices(DetectionPoint[] opticalPoints, double thetaDeg, int width, int height, double mmPerPixel, double originX, double originY, double offsetX, double offsetY, BeltDirection direction)`
  - `public static TrackedWorkpiecePose BeltCoordinates.CreateTrackedPose(int productId, string source, PlateResult detection, VisionOptions calibration, int frameWidth, int frameHeight, BeltDirection direction, double encoderAtCrossingMm = 0.0, DateTime? utcCrossing = null)`

- [ ] **Step 1: Write the failing tests in `App/Tests/Program.cs`**

Add tests under section `17. Vision Pose Tracking & 4-Vertex Gantry Planar Contract Verification`:

```csharp
// 17. Vision Pose Tracking & 4-Vertex Gantry Planar Contract Verification
// Test canonical vertex ordering across 0, 15, -30, 44 deg
var testOpt = new VisionOptions { PlateSizeMm = 50, HolePitchMm = 40, FixedMmPerPixel = 0.1, OriginPxX = 320, OriginPxY = 240, OffsetXmm = 0, OffsetYmm = 0 };
double mmPerPx = 0.1;
int fw = 640, fh = 480;

// Case 1: unrotated square 40x40mm pitch centered at (320, 240)
// Optical offsets: TL=(-200px, -200px), TR=(+200px, -200px), BR=(+200px, +200px), BL=(-200px, +200px)
var pts0 = new DetectionPoint[] {
    new(120, 40),  // TL in image: x=120, y=40 (py < oy => v = +20mm, u = -20mm)
    new(520, 40),  // TR in image: x=520, y=40 (py < oy => v = +20mm, u = +20mm)
    new(520, 440), // BR in image: x=520, y=440 (py > oy => v = -20mm, u = +20mm)
    new(120, 440)  // BL in image: x=120, y=440 (py > oy => v = -20mm, u = -20mm)
};
var vOrder0 = BeltCoordinates.MapVertices(pts0, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
Check("vertex ordering: MapVertices returns exactly 4 vertices", vOrder0.Length == 4);
Check("vertex ordering: v1 is Quad I (+20, +20)", vOrder0[0].Index == 1 && Math.Abs(vOrder0[0].X_ConveyorMm - 20.0) < 1e-4 && Math.Abs(vOrder0[0].Y_ConveyorMm - 20.0) < 1e-4);
Check("vertex ordering: v2 is Quad II (-20, +20)", vOrder0[1].Index == 2 && Math.Abs(vOrder0[1].X_ConveyorMm - (-20.0)) < 1e-4 && Math.Abs(vOrder0[1].Y_ConveyorMm - 20.0) < 1e-4);
Check("vertex ordering: v3 is Quad III (-20, -20)", vOrder0[2].Index == 3 && Math.Abs(vOrder0[2].X_ConveyorMm - (-20.0)) < 1e-4 && Math.Abs(vOrder0[2].Y_ConveyorMm - (-20.0)) < 1e-4);
Check("vertex ordering: v4 is Quad IV (+20, -20)", vOrder0[3].Index == 4 && Math.Abs(vOrder0[3].X_ConveyorMm - 20.0) < 1e-4 && Math.Abs(vOrder0[3].Y_ConveyorMm - (-20.0)) < 1e-4);

// Test rotated case theta = 15 deg
var vOrder15 = BeltCoordinates.MapVertices(pts0, 15.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
Check("vertex ordering: 15 deg retains canonical indices 1..4",
    vOrder15[0].Index == 1 && vOrder15[1].Index == 2 && vOrder15[2].Index == 3 && vOrder15[3].Index == 4);

// Review Focus 3: Degenerate or collinear points handling
bool caughtDegenerate = false;
try {
    var degeneratePts = new DetectionPoint[] { new(320, 240), new(320, 240), new(320, 240), new(320, 240) };
    BeltCoordinates.MapVertices(degeneratePts, 0.0, fw, fh, mmPerPx, 320, 240, 0, 0, BeltDirection.XPlus);
} catch (ArgumentException) { caughtDegenerate = true; }
Check("review focus 3: MapVertices rejects degenerate coincident points", caughtDegenerate);

// Review Focus 4: Out-of-tolerance pitch in CreateTrackedPose
var outOfTolResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95,
    new DetectionPoint[] { new(100, 40), new(540, 40), new(540, 440), new(100, 440) }, // 44mm pitch X (> 42mm tolerance)
    [], [], "OK");
var outOfTolPose = BeltCoordinates.CreateTrackedPose(1, "CAM1", outOfTolResult, testOpt, fw, fh, BeltDirection.XPlus, 0.0);
Check("review focus 4: TrackedPose marks IsOpticalMeasured=false when pitch exceeds tolerance", !outOfTolPose.IsOpticalMeasured);
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo
```
Expected: FAIL with compilation errors `The name 'ConveyorVertex' does not exist in the current context` and `'BeltCoordinates' does not contain a definition for 'MapVertices'`.

- [ ] **Step 3: Implement `ConveyorVertex`, `TrackedWorkpiecePose`, and `BeltCoordinates` methods in `App/Vision/Vision/BeltCoordinates.cs`**

Define the immutable records in `PlateVision50.Vision`:
```csharp
public sealed record ConveyorVertex(
    short Index,
    double X_ConveyorMm,
    double Y_ConveyorMm);

public sealed record TrackedWorkpiecePose(
    int ProductId,
    string SourceTrackKey,
    DateTime UtcAtCrossing,
    double EncoderAtCrossingMm,
    double CenterX_mm,
    double CenterY_mm,
    double Theta_deg,
    double MeasuredPitchX_mm,
    double MeasuredPitchY_mm,
    double Confidence,
    ConveyorVertex[] Vertices,
    bool IsOpticalMeasured);
```

Add methods to `BeltCoordinates`:
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
    BeltDirection direction)
{
    if (opticalPoints == null || opticalPoints.Length != 4)
        throw new ArgumentException("opticalPoints must contain exactly 4 points");
    if (!double.IsFinite(mmPerPixel) || mmPerPixel <= 0)
        throw new ArgumentException("mmPerPixel > 0 required");
    if (!opticalPoints.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
        throw new ArgumentException("Optical coordinates must be finite");

    var mapped = new (double X, double Y)[4];
    for (int i = 0; i < 4; i++)
    {
        var coord = Map(opticalPoints[i].X, opticalPoints[i].Y, thetaDeg, width, height, mmPerPixel, originX, originY, offsetX, offsetY, direction);
        mapped[i] = (coord.Xmm, coord.Ymm);
    }

    double cx = mapped.Average(p => p.X);
    double cy = mapped.Average(p => p.Y);

    double rad = thetaDeg * Math.PI / 180.0;
    double cos = Math.Cos(rad), sin = Math.Sin(rad);

    var assigned = new List<ConveyorVertex>();
    foreach (var p in mapped)
    {
        double dx = p.X - cx;
        double dy = p.Y - cy;
        // Unrotate by -thetaDeg into the workpiece frame
        double lx = dx * cos + dy * sin;
        double ly = -dx * sin + dy * cos;

        if (Math.Abs(lx) < 1e-4 && Math.Abs(ly) < 1e-4)
            throw new ArgumentException("Degenerate collinear or coincident points detected");

        short idx = (lx >= 0, ly >= 0) switch
        {
            (true, true) => 1,   // Quad I: Front-Left (+halfX, +halfY)
            (false, true) => 2,  // Quad II: Rear-Left (-halfX, +halfY)
            (false, false) => 3, // Quad III: Rear-Right (-halfX, -halfY)
            (true, false) => 4   // Quad IV: Front-Right (+halfX, -halfY)
        };
        assigned.Add(new ConveyorVertex(idx, p.X, p.Y));
    }

    var distinctIndices = assigned.Select(v => v.Index).Distinct().Count();
    if (distinctIndices != 4)
    {
        // Fallback: assign strictly 1..4 by polar angle
        var sortedByAngle = mapped
            .Select(p => {
                double dx = p.X - cx, dy = p.Y - cy;
                double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
                double angle = Math.Atan2(ly, lx);
                return (Point: p, Angle: angle);
            })
            .OrderBy(item => item.Angle)
            .ToArray();
        // Atan2 order from -PI to PI: Quad III -> Quad IV -> Quad I -> Quad II
        // Map explicitly to 3, 4, 1, 2
        short[] angleOrder = [3, 4, 1, 2];
        return sortedByAngle
            .Select((item, i) => new ConveyorVertex(angleOrder[i], item.Point.X, item.Point.Y))
            .OrderBy(v => v.Index)
            .ToArray();
    }

    return assigned.OrderBy(v => v.Index).ToArray();
}

public static TrackedWorkpiecePose CreateTrackedPose(
    int productId,
    string source,
    PlateResult detection,
    VisionOptions calibration,
    int frameWidth,
    int frameHeight,
    BeltDirection direction,
    double encoderAtCrossingMm = 0.0,
    DateTime? utcCrossing = null)
{
    var centerCoord = FromDetection(detection, calibration, frameWidth, frameHeight, direction);
    var vertices = MapVertices(detection.GluePx, detection.AngleDeg, frameWidth, frameHeight,
        detection.MmPerPixel, calibration.OriginPxX, calibration.OriginPxY,
        calibration.OffsetXmm, calibration.OffsetYmm, direction);

    static double Dist(ConveyorVertex a, ConveyorVertex b) =>
        Math.Sqrt(Math.Pow(a.X_ConveyorMm - b.X_ConveyorMm, 2) + Math.Pow(a.Y_ConveyorMm - b.Y_ConveyorMm, 2));

    double pitchX = (Dist(vertices[0], vertices[1]) + Dist(vertices[3], vertices[2])) / 2.0;
    double pitchY = (Dist(vertices[1], vertices[2]) + Dist(vertices[0], vertices[3])) / 2.0;

    bool validPitch = Math.Abs(pitchX - 40.0) <= 2.0 && Math.Abs(pitchY - 40.0) <= 2.0;
    bool isMeasured = detection.Found && validPitch && detection.Confidence >= 0.70;

    return new TrackedWorkpiecePose(
        productId,
        $"{source}/{productId}",
        utcCrossing ?? DateTime.UtcNow,
        encoderAtCrossingMm,
        centerCoord.Xmm,
        centerCoord.Ymm,
        centerCoord.ThetaDeg,
        pitchX,
        pitchY,
        detection.Confidence,
        vertices,
        isMeasured);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo && dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
Expected: PASS with `TOTAL_TEST_FAILURES=0`.

- [ ] **Step 5: Commit**

```bash
git add App/Vision/Vision/BeltCoordinates.cs App/Tests/Program.cs
git commit -m "feat(vision): add ConveyorVertex, TrackedWorkpiecePose and canonical MapVertices"
```

---

### Task 2: Simulator Measured Vertex Acceptance & Workspace Envelope

**Files:**
- Modify: `App/Vision/TwinCAT/CameraPipelineSimulator.cs:52-115`
- Test: `App/Tests/Program.cs`

**Interfaces:**
- Consumes:
  - `PlateVision50.Vision.ConveyorVertex` from Task 1 (`App/Vision/Vision/BeltCoordinates.cs`)
- Produces:
  - `public Receipt Accept(string sourceTrackKey, double xMm, double yMm, double thetaDegrees, double encoderAtTriggerMm, ConveyorVertex[]? measuredVertices = null)`
  - Direct transformation of `measuredVertices` to Robot frame:
    $X_{robot} = -CameraToWorkStartMm + CameraFineXmm + v.X_{ConveyorMm} \cdot \cos(yaw) - v.Y_{ConveyorMm} \cdot \sin(yaw)$
    $Y_{robot} = CameraOffsetYmm + v.X_{ConveyorMm} \cdot \sin(yaw) + v.Y_{ConveyorMm} \cdot \cos(yaw)$
  - Backward-compatible synthesis fallback when `measuredVertices == null`.
  - Non-finite coordinate rejection and array length validation.

- [ ] **Step 1: Write the failing tests in `App/Tests/Program.cs`**

Add tests under section `17`:

```csharp
// Test simulator acceptance with measured vertices (asymmetric 42x38 mm pitch)
var simAsym = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);
var measuredAsym = new ConveyorVertex[] {
    new(1, 21.0, 19.0),   // Quad I: halfX=21, halfY=19 => 42x38mm
    new(2, -21.0, 19.0),  // Quad II
    new(3, -21.0, -19.0), // Quad III
    new(4, 21.0, -19.0)   // Quad IV
};
var ackAsym = simAsym.Accept("ASYM_TEST/1", 0.0, 0.0, 0.0, 100.0, measuredAsym);
Check("sim measured: accepted receipt has 4 corners", ackAsym.Corners.Length == 4);
// With cameraToWorkStart=200, FineX=0, OffsetY=85, yaw=0:
// V1 RobotX = -200 + 21 = -179mm, RobotY = 85 + 19 = 104mm
Check("sim measured: V1 RobotX is -179.0mm", Math.Abs(ackAsym.Corners[0].X - (-179.0)) < 1e-4);
Check("sim measured: V1 RobotY is 104.0mm", Math.Abs(ackAsym.Corners[0].Y - 104.0) < 1e-4);
Check("sim measured: V2 RobotX is -221.0mm", Math.Abs(ackAsym.Corners[1].X - (-221.0)) < 1e-4);

// Review Focus 1: Non-finite coordinate rejection
bool caughtNan = false;
try {
    var nanVertices = new ConveyorVertex[] {
        new(1, double.NaN, 19.0), new(2, -21.0, 19.0), new(3, -21.0, -19.0), new(4, 21.0, -19.0)
    };
    simAsym.Accept("NAN_TEST/1", 0.0, 0.0, 0.0, 100.0, nanVertices);
} catch (ArgumentException) { caughtNan = true; }
Check("review focus 1: Accept rejects NaN measured vertex coordinates", caughtNan);

// Review Focus 2: Vertex count mismatch rejection
bool caughtLengthMismatch = false;
try {
    var badLen = new ConveyorVertex[] { new(1, 21.0, 19.0), new(2, -21.0, 19.0), new(3, -21.0, -19.0) };
    simAsym.Accept("LEN_TEST/1", 0.0, 0.0, 0.0, 100.0, badLen);
} catch (ArgumentException) { caughtLengthMismatch = true; }
Check("review focus 2: Accept rejects vertex arrays of length != 4", caughtLengthMismatch);

// Review Focus 5: Dynamic sync lead pushes vertex overtravel
// At encoder = 345mm, corner X = -179 + 345 - 100 = 66mm (in bounds)
// At encoder = 455mm, corner X = -179 + 455 - 100 = 176mm (inside 180mm static envelope)
// Lead = 100 mm/s * 0.12s = 12mm; DeltaXSync = 100 mm/s * 0.06s = 6mm.
// TargetX + DeltaXSync = 176 + 12 + 6 = 194mm > 180mm => MUST reject with 501
var overtravelCmd = simAsym.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 455.0);
Check("review focus 5: BuildQd75Command rejects dynamic sync lead overtravel with error 501",
    !overtravelCmd.IsValid && overtravelCmd.ErrorCode == 501);
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo
```
Expected: FAIL with compilation error: `No overload for method 'Accept' takes 6 arguments`.

- [ ] **Step 3: Update `CameraPipelineSimulator.Accept` in `App/Vision/TwinCAT/CameraPipelineSimulator.cs`**

Modify `Accept` method signature and body:
```csharp
public Receipt Accept(
    string sourceTrackKey,
    double xMm,
    double yMm,
    double thetaDegrees,
    double encoderAtTriggerMm,
    ConveyorVertex[]? measuredVertices = null)
{
    if(string.IsNullOrWhiteSpace(sourceTrackKey)||
       !new[]{xMm,yMm,thetaDegrees,encoderAtTriggerMm}.All(double.IsFinite) ||
       Math.Abs(xMm)>1000 || Math.Abs(yMm)>1000 || Math.Abs(thetaDegrees)>45)
        throw new ArgumentException("Invalid input to simulation queue");
    if(_acceptedKeys.Contains(sourceTrackKey))
        throw new InvalidOperationException("Duplicate Track ID: already accepted");
    if(_queue.Count>=8)throw new InvalidOperationException("FIFO full 8/8");

    double yaw=CameraYawDegrees*Math.PI/180.0;
    double cosYaw=Math.Cos(yaw), sinYaw=Math.Sin(yaw);
    double xc=-CameraToWorkStartMm+CameraFineXmm+xMm*cosYaw-yMm*sinYaw;
    double yc=CameraOffsetYmm+xMm*sinYaw+yMm*cosYaw;

    FifoGlue[] corners;
    if (measuredVertices != null)
    {
        if (measuredVertices.Length != 4)
            throw new ArgumentException("measuredVertices must contain exactly 4 vertices");
        if (measuredVertices.Any(v => !double.IsFinite(v.X_ConveyorMm) || !double.IsFinite(v.Y_ConveyorMm)))
            throw new ArgumentException("measuredVertices coordinates must be finite");
        var sorted = measuredVertices.OrderBy(v => v.Index).ToArray();
        for (short i = 1; i <= 4; i++)
        {
            if (sorted[i - 1].Index != i)
                throw new ArgumentException("measuredVertices must contain canonical indices 1..4");
        }
        corners = sorted.Select(v => new FifoGlue(
            -CameraToWorkStartMm + CameraFineXmm + v.X_ConveyorMm * cosYaw - v.Y_ConveyorMm * sinYaw,
            CameraOffsetYmm + v.X_ConveyorMm * sinYaw + v.Y_ConveyorMm * cosYaw)).ToArray();
    }
    else
    {
        double radians=(thetaDegrees+CameraYawDegrees)*Math.PI/180.0;
        double c=Math.Cos(radians), s=Math.Sin(radians);
        double halfX=PitchXmm/2.0,halfY=PitchYmm/2.0;
        var locals=new[]{(halfX,halfY),(-halfX,halfY),(-halfX,-halfY),(halfX,-halfY)};
        corners=locals.Select(p=>new FifoGlue(
            xc+p.Item1*c-p.Item2*s,
            yc+p.Item1*s+p.Item2*c)).ToArray();
    }

    uint id=checked(_accepted+1);
    var receipt=new Receipt(id,_queue.Count+1,xc,yc,corners,encoderAtTriggerMm,sourceTrackKey);
    _queue.Enqueue(new Item(receipt,encoderAtTriggerMm));
    _acceptedKeys.Add(sourceTrackKey);
    _accepted=id;
    return receipt;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo && dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
Expected: PASS with `TOTAL_TEST_FAILURES=0`.

- [ ] **Step 5: Commit**

```bash
git add App/Vision/TwinCAT/CameraPipelineSimulator.cs App/Tests/Program.cs
git commit -m "feat(sim): accept measured ConveyorVertex array with strict validation in CameraPipelineSimulator"
```

---

### Task 3: UI Label Overlay Canonical Quadrant Reconciliation

**Files:**
- Modify: `App/Vision/Vision/VisionLabelOverlay.cs:196-206`
- Test: `App/Tests/Program.cs`

**Interfaces:**
- Consumes:
  - `BeltCoordinates.MapVertices` from Task 1
- Produces:
  - UI callout lines $P1 \dots P4$ rendered strictly in canonical Quadrants I..IV order matching QD75 motion command publisher indices.

- [ ] **Step 1: Write the failing test in `App/Tests/Program.cs`**

Add assertion in `App/Tests/Program.cs` verifying UI overlay mapping contract:

```csharp
// UI Overlay mapping contract: verify MapVertices yields P1..P4 corresponding to Quad I..IV
var dummyPlate = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, pts0, [], [], "OK");
var overlayVerts = BeltCoordinates.MapVertices(dummyPlate.GluePx, dummyPlate.AngleDeg, fw, fh, dummyPlate.MmPerPixel, 320, 240, 0, 0, BeltDirection.XPlus);
Check("ui overlay contract: P1 corresponds to Quad I (Index 1)", overlayVerts[0].Index == 1);
Check("ui overlay contract: P4 corresponds to Quad IV (Index 4)", overlayVerts[3].Index == 4);
```

- [ ] **Step 2: Run test to verify existing baseline**

Run:
```cmd
dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
Expected: PASS (verifies Task 1 contract ready for Task 3).

- [ ] **Step 3: Update `VisionLabelOverlay.cs` lines 196-206**

In `App/Vision/Vision/VisionLabelOverlay.cs`:
Replace raw index iteration `for(int i=0;i<Math.Min(4,p.GluePx.Length);i++)` with `BeltCoordinates.MapVertices`:

```csharp
            if (p.GluePx != null && p.GluePx.Length == 4)
            {
                var vertices = BeltCoordinates.MapVertices(p.GluePx, p.AngleDeg, imageWidth, imageHeight,
                    p.MmPerPixel, options.OriginPxX, options.OriginPxY, options.OffsetXmm, options.OffsetYmm, belt);
                foreach (var v in vertices)
                {
                    display.Children.Add(new TextBlock {
                        Text=$"P{v.Index}  X={val(v.X_ConveyorMm),8}  Y={val(v.Y_ConveyorMm),8}",
                        FontFamily=new FontFamily("Consolas"),FontSize=11.8,
                        Foreground=Brush("#BAEAF7"),Margin=new Thickness(0,1,0,0)
                    });
                }
            }
            else
            {
                for(int i=0;i<Math.Min(4,p.GluePx?.Length ?? 0);i++)
                {
                    var gp=p.GluePx![i];
                    var point=BeltCoordinates.Map(gp.X,gp.Y,p.AngleDeg,imageWidth,imageHeight,
                        p.MmPerPixel,options.OriginPxX,options.OriginPxY,options.OffsetXmm,options.OffsetYmm,belt);
                    display.Children.Add(new TextBlock {
                        Text=$"P{i+1}  X={val(point.Xmm),8}  Y={val(point.Ymm),8}",
                        FontFamily=new FontFamily("Consolas"),FontSize=11.8,
                        Foreground=Brush("#BAEAF7"),Margin=new Thickness(0,1,0,0)
                    });
                }
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo && dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
Expected: PASS with `TOTAL_TEST_FAILURES=0`.

- [ ] **Step 5: Commit**

```bash
git add App/Vision/Vision/VisionLabelOverlay.cs App/Tests/Program.cs
git commit -m "fix(overlay): reconcile UI P1..P4 overlay to canonical quadrant vertices"
```

---

### Task 4: Main Application Trigger Wiring & End-to-End SIL Simulation

**Files:**
- Modify: `App/Vision/MainWindow.xaml.cs:1060-1110`
- Test: `App/Tests/Program.cs`

**Interfaces:**
- Consumes:
  - `BeltCoordinates.MapVertices` and `BeltCoordinates.CreateTrackedPose` from Task 1
  - `CameraPipelineSimulator.Accept` with `measuredVertices` from Task 2
- Produces:
  - Propagated measured vertices from `TriggerEvent` into `_offlineQueue`
  - Canonical corner presentation in `FifoGluePoints.Text` and `PipelineMockStatus.Text`

- [ ] **Step 1: Write the failing end-to-end SIL simulation test in `App/Tests/Program.cs`**

Add test simulating full trigger pipeline:

```csharp
// End-to-end SIL simulation: TriggerEvent -> MapVertices -> Simulator -> BuildQd75Command
var simE2E = new CameraPipelineSimulator(cameraToWorkStartMm: 200, cameraFineXmm: 0, cameraOffsetYmm: 85, cameraYawDegrees: 0, pitchXmm: 40, pitchYmm: 40);
var dummyResult = new PlateResult(true, 320, 240, 0, 0, 0, 0.1, 0.95, pts0, [], [], "OK");
var trigEvent = new TriggerEvent(42, "OFFLINE_CAM", dummyResult, 100, DateTime.UtcNow);

var e2ePose = BeltCoordinates.CreateTrackedPose(trigEvent.ProductId, trigEvent.Source, trigEvent.Pose, testOpt, fw, fh, BeltDirection.XPlus, 50.0);
Check("e2e: TrackedPose created with 4 canonical vertices", e2ePose.Vertices.Length == 4 && e2ePose.IsOpticalMeasured);

var e2eAck = simE2E.Accept(e2ePose.SourceTrackKey, e2ePose.CenterX_mm, e2ePose.CenterY_mm, e2ePose.Theta_deg, e2ePose.EncoderAtCrossingMm, e2ePose.Vertices);
Check("e2e: simulator accepted tracked pose with measured vertices", e2eAck.ProductId == 1 && e2eAck.Corners.Length == 4);

// Advance virtual encoder to 270mm (travel = 220mm, HeadX = -200 + 20 + 220 = 40mm inside workspace)
var e2eCmd = simE2E.BuildQd75Command(vertexIndex: 1, conveyorSpeedMmS: 100.0, currentEncoderMm: 270.0);
Check("e2e: QD75 command generated successfully from measured vertex", e2eCmd.IsValid && e2eCmd.ErrorCode == 0);
var e2eWords = e2eCmd.ToMelsecWords();
Check("e2e: 15-word binary payload generated", e2eWords.Length == 15 && e2eWords[14] == 1);
```

- [ ] **Step 2: Run test to verify it passes**

Run:
```cmd
dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
Expected: PASS (or FAIL before Step 3 if tests were run against unlinked types).

- [ ] **Step 3: Update `RecordTrigger` in `App/Vision/MainWindow.xaml.cs`**

In `App/Vision/MainWindow.xaml.cs:1060-1110`:
```csharp
    private void RecordTrigger(TriggerEvent trigger,int width,int height)
    {
        if(!_trackingEnabled)return;
        var pose=BeltCoordinates.FromDetection(trigger.Pose,_options,width,height,_beltDirection);
        ConveyorVertex[]? measuredVertices = null;
        if (trigger.Pose.GluePx != null && trigger.Pose.GluePx.Length == 4)
        {
            try {
                measuredVertices = BeltCoordinates.MapVertices(trigger.Pose.GluePx, trigger.Pose.AngleDeg,
                    width, height, trigger.Pose.MmPerPixel, _options.OriginPxX, _options.OriginPxY,
                    _options.OffsetXmm, _options.OffsetYmm, _beltDirection);
            } catch { measuredVertices = null; }
        }

        _triggersTotal++;
        TriggerCounter.Text=_triggersTotal.ToString();
        string line=FormattableString.Invariant($"#{trigger.ProductId}  {trigger.Source}  X={pose.Xmm:F2}  Y={pose.Ymm:F2}  A={pose.ThetaDeg:F2}°");
        string rowKey=trigger.Source+"/"+trigger.ProductId;
        var row=new TriggerAuditRow {
            Timestamp=DateTime.Now, Source=trigger.Source,TrackId=trigger.ProductId,
            PreviousPx=trigger.PreviousProjectedPx,CurrentPx=trigger.CurrentProjectedPx,
            MidlinePx=trigger.MidlinePx,Xmm=pose.Xmm,Ymm=pose.Ymm,Theta=pose.ThetaDeg,
            Delivery=_benchArmed?"TRIGGER → FIFO BENCH":"TRIGGER → NOT SENT",
            Plc=_benchArmed?"WAITING FOR SAFETY / ACK":"ADS READ ONLY"
        };
        _auditRows.Insert(0,row);
        _auditByTrack[rowKey]=row;
        if(_auditRows.Count>250)_auditRows.RemoveAt(_auditRows.Count-1);
        AppendPipelineCsv(row,"MIDLINE_TRIGGER",_benchArmed?"BENCH REQUESTED":"NO ADS WRITE");
        _events.Insert(0,DateTime.Now.ToString("HH:mm:ss")+" | "+line);
        if(_events.Count>4)_events.RemoveAt(_events.Count-1);
        TriggerLog.Text=string.Join("\n",_events);
        if(_offlineSimulationEnabled)
        {
            try {
                // This is in-memory PLC contract simulation, NEVER an ADS transaction.
                var virtualAck=_offlineQueue.Accept(rowKey,pose.Xmm,pose.Ymm,pose.ThetaDeg,0,measuredVertices);
                row.Delivery="VIRTUAL FIFO ACK #"+virtualAck.ProductId;
                row.Plc=measuredVertices != null ? "MEASURED 4 VERTICES - NO PLC WRITE" : "SIMULATED 4 POINTS - NO PLC WRITE";
                PipelineMockStatus.Text=$"VIRTUAL ACK #{virtualAck.ProductId} | FIFO={virtualAck.QueueCount}/8 | 4 vertices: "+
                    string.Join(" ; ",virtualAck.Corners.Select((p,i)=>$"P{i+1}=({p.X:F1},{p.Y:F1})"));
                AppendPipelineCsv(row,"SIMULATED_FIFO_ACK",PipelineMockStatus.Text);
                var snap=_offlineQueue.GetSnapshot();
                FifoCount.Text=snap.Count.ToString();
                FifoProduct.Text=snap.HeadProduct>0?snap.HeadProduct.ToString():"—";
                FifoProgress.Text=$"VIRTUAL FIFO (PC MEMORY · ZERO ADS)\nHead: X={snap.HeadXmm:F1}, Y={snap.HeadYmm:F1} mm | window={(snap.Inside?"INSIDE":"WAIT")}\nAccepted={snap.Accepted}, Count={snap.Count}/8";
                FifoGluePoints.Text=snap.Count>0?string.Join("\n",snap.GluePoints.Select((p,i)=>$"P{i+1}  X={p.X,8:F2}  Y={p.Y,8:F2} mm")):"No simulated product";
            }
            catch(Exception ex)
            {
                row.Delivery="SIMULATION REJECTED";
                row.Plc=ex.Message;
                PipelineMockStatus.Text="SIM REJECTED: "+ex.Message;
                AppendPipelineCsv(row,"SIMULATED_FIFO_REFUSED",ex.Message);
            }
        }
```

- [ ] **Step 4: Run test and build to verify it passes**

Run:
```cmd
dotnet build App/Vision/PlateVision50.csproj -c Debug -p:PylonRoot="C:\Program Files\Basler\pylon" --nologo
dotnet build App/Tests/Tests.csproj -c Debug --nologo && dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
Expected: PASS with 0 build errors and `TOTAL_TEST_FAILURES=0`.

- [ ] **Step 5: Commit**

```bash
git add App/Vision/MainWindow.xaml.cs App/Tests/Program.cs
git commit -m "feat(ui): wire measured ConveyorVertex array to offline simulation queue"
```

---

### Task 5: Documentation, Tag Matrix Audit Checkpoint, and Full Offline QA

**Files:**
- Create: `Docs/CHECKPOINT_20261029_TRACKING_4PT_GANTRY.md`
- Modify: `Docs/TAG_MATRIX.md:79-95`
- Modify: `README.md:20-40`
- Modify: `PROJECT_INDEX.md:50-60`

**Interfaces:**
- Consumes: Completed and verified test results from Tasks 1–4
- Produces: Audit trail, tag matrix documentation, zero-hardware verification checkpoint

- [ ] **Step 1: Update `Docs/TAG_MATRIX.md`**

Add section 7 documenting the 4-vertex planar tracking and QD75 handoff contract:
```markdown
## 7. VISION POSE TRACKING & 4-VERTEX GANTRY OFFLINE CONTRACT (2026-10-29)
- Spec reference: `docs/superpowers/specs/2026-10-29-tracking-four-point-gantry-design.md`
- Contract records: `ConveyorVertex` (Index 1..4), `TrackedWorkpiecePose`.
- Canonical Quadrants: V1 (+halfX, +halfY), V2 (-halfX, +halfY), V3 (-halfX, -halfY), V4 (+halfX, -halfY).
- Offline SIL Simulation: `CameraPipelineSimulator.Accept` receives measured vertices and preserves them into `Receipt.Corners`.
- Motion command: 15-word binary packing (`D100..D114`) conforming to Mitsubishi QD75 buffer memory layout.
- Safety Boundary: Zero ADS writes to `MAIN.bStartGlueCycle` or `MAIN.bTest_WriteM1000`. Hardware motion strictly disarmed by compile-time gate `MAIN.bLegacyMotionPublisherAllow = FALSE`.
```

- [ ] **Step 2: Update `README.md` and `PROJECT_INDEX.md`**

Add cross-references to the new design spec, implementation plan, and checkpoint under the testing and architecture sections.

- [ ] **Step 3: Create `Docs/CHECKPOINT_20261029_TRACKING_4PT_GANTRY.md`**

Create the audit checkpoint documenting:
- Spec and plan paths
- Modified files and linked tests
- Automated test results (`App/Tests/Program.cs`)
- Zero-hardware affirmation (0 writes to `bStartGlueCycle`, 0 PLC downloads)
- Commissioning prerequisites remaining unverified (ST-5114 hardware latch, laser calibration, Y20/Y25 GX Works2 verification).

- [ ] **Step 4: Run full offline verification suite**

Run:
```cmd
dotnet build App/Tests/Tests.csproj -c Debug --nologo
dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
dotnet build App/Vision/PlateVision50.csproj -c Debug -p:PylonRoot="C:\Program Files\Basler\pylon" --nologo
```
Expected: PASS with 0 errors and `TOTAL_TEST_FAILURES=0`.

- [ ] **Step 5: Commit**

```bash
git add Docs/CHECKPOINT_20261029_TRACKING_4PT_GANTRY.md Docs/TAG_MATRIX.md README.md PROJECT_INDEX.md
git commit -m "docs: add tracking 4-point gantry checkpoint and tag matrix contract updates"
```
