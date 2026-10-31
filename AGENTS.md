# Repository Guidelines

## Project Overview

DATN is a Windows industrial-line vision/HMI project. The maintained application is a .NET 8 x64 WPF dashboard for two camera stations, plate detection, track IDs, conveyor coordinates, TwinCAT ADS telemetry, and guarded line-command workflows. TwinCAT is the lower control layer; Mitsubishi PLC-Q/Gantry logic is a source-level integration boundary. Station 02 SCARA/Camera 2 remains reserved and uncommissioned.

The active checkout is this repository at `E:\Orca\DATN`. Treat older `Desktop\DATN`, `C:\Users\aml\orca\DATN`, archived releases, and historical solution names as reference only; prefer paths that exist in the current tree.

## Architecture & Data Flow

- `App/Vision/PlateVision50.csproj` builds `PlateVision50_V2`, the WPF entry point (`App/Vision/App.xaml` → `MainWindow.xaml`). `App/Vision/App.xaml.cs` enforces one process with `Local\\DATN_AML_LINE_CONTROL_UI`.
- `MainWindow` is split across partial-class files: `MainWindow.xaml.cs` owns capture/tracking flow; `Dashboard.cs`, `StationOverview.cs`, and `StationDashboards.cs` own dashboard/telemetry views. A 75 ms dispatcher loop ingests video/camera frames; a separate ADS polling loop updates diagnostics and FIFO state.
- Camera flow: `StationCameraController`/`CameraSlotModel` scan and own devices; Basler uses the pylon wrapper, Hikrobot uses MVS/GVCP paths. `CameraOwnershipRegistry` prevents slot collisions. Camera 1 is the production vision source; Camera 2 is display/reserved only.
- Vision flow: `PlateDetector.AnalyzeAll` → `MidlineTracker` per source → one `TriggerEvent` per crossing → `BeltCoordinates` pixel-to-conveyor mapping → WPF overlays/audit CSV. Preserve Track ID identity, trigger-line reset behavior, and video-only/no-encoder semantics.
- PLC flow: `AdsFifoBenchBridge`/`LineMailboxProtocol` read ADS port 851 telemetry and mediate any guarded mailbox request. `LineInterlock` evaluates ADS freshness, auxiliary E-stop state, PLC-Q, gantry fault, camera freshness, speed, commissioning, and SCARA exclusion before a command. `CameraPipelineSimulator` is in-memory only.
- TwinCAT source (`PLC/TwinCAT/`) owns encoder/conveyor state, the 8-slot camera FIFO, and QD75 publisher. Mitsubishi source (`PLC/Mitsubishi/`) owns the PLC-Q motion/dispense side. The offline coordinate contract is D100..D114 with M1000/M1001/M105 handshake; do not infer live TCP/homed telemetry that is not mapped.

## Key Directories

- `App/Vision/` — maintained WPF application, XAML, vision, trigger, ADS, and station modules.
- `App/Tests/` — standalone executable regression suite; links selected production source files directly.
- `App/DATN_WPF.sln` — current WPF solution containing the application and Basler wrapper.
- `Dependencies/BaslerCamera/` — Basler pylon wrapper; `Dependencies/Devices/` — shared Hikrobot/unified camera source linked by the app.
- `PLC/TwinCAT/` — TwinCAT 3 source (`DATN_TwinCat.sln`, `DATN/DATN.tsproj`, `PLC1/POUs/`). Source only; not proof of the running controller.
- `PLC/Mitsubishi/` — GX Works2 project/source and PLC-Q Structured Text references.
- `Docs/` — tag/safety contracts, checkpoints, read-only audits, camera diagnostics, build evidence, and commissioning notes.
- `ReleaseCandidate/` — versioned local candidate outputs. `Release/` and `Runtime/` are active/local runtime paths and must not be overwritten casually.

## Development Commands

Run from the repository root in a Windows/.NET 8 environment:

```cmd
dotnet build App/DATN_WPF.sln -c Debug -p:PylonRoot="C:\Program Files\Basler\pylon" --nologo
dotnet build App/Vision/PlateVision50.csproj -c Release -p:PylonRoot="C:\Program Files\Basler\pylon" --nologo
dotnet build App/Tests/Tests.csproj -c Debug --nologo
dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```

- Use `OPEN_DATN_WPF.cmd` for Visual Studio, `OPEN_DATN_TwinCat.cmd` for the TwinCAT source project, and `OPEN_DATN_PLC.cmd` for GX Works2.
- `START_DATN.cmd` launches the existing active `Release/PlateVision50_V2.dll` with `Runtime/DotNet8/dotnet.exe`; it is not a build command. Preview launchers may point at historical candidate folders, so inspect their target before using them.
- Build/test offline into a new versioned candidate directory when packaging. Do not overwrite active `Release/`; do not activate, download, force, Run/Stop, or write TwinCAT/ADS/MC hardware to verify a change.

## Code Conventions & Common Patterns

- C# uses nullable reference types and implicit usings; target x64 and `net8.0-windows`. Follow existing compact style and file-scoped namespaces. Keep WPF event handlers thin and put reusable math/protocol logic in the `Vision`, `Trigger`, or `TwinCAT` modules.
- Use partial `MainWindow` files by responsibility rather than growing one UI file. UI state is explicit in fields/models; refresh views after source, calibration, direction, or safety-state changes.
- Prefer fail-closed guards: reject stale/missing ADS or camera data, nonfinite coordinates, invalid speed/calibration, duplicate/full FIFO entries, and uncommissioned command paths. Never treat software ADS feedback as certified safety.
- Keep hardware access behind transport/controller abstractions (`IAdsMailboxTransport`, camera controllers) so tests can use fake transports and the simulator. Live mailbox writes require the existing commissioning, ACK, sequence, pulse-release, and cancellation rules.
- Async camera/ADS work uses `Task.Run` around blocking SDK/ADS calls and cancellation tokens for queued operations; do not block the WPF dispatcher. Disarm pending bench work when a source, direction, calibration, or window changes.
- Preserve explicit UI labels such as `READ ONLY`, `TEST ONLY`, `NO ADS WRITE`, `UNAVAILABLE`, and `RESERVED`; never add fake SCARA state, PLC tags, camera measurements, or live robot telemetry.
- Before changing source/configuration, make a restorable backup and record the changed paths/checkpoint under `Docs/`.

## Important Files

- `App/Vision/PlateVision50.csproj` — maintained app target, NuGet packages, linked device sources, and Basler project reference.
- `App/Vision/MainWindow.xaml.cs` — capture loop, detection/tracking, overlays, audit logs, offline simulation, and guarded bench UI.
- `App/Vision/Vision/PlateDetector.cs`, `MidlineTracker.cs`, `BeltCoordinates.cs`, `VisionLabelOverlay.cs` — core vision/identity/coordinate/overlay behavior.
- `App/Vision/Vision/LineMailboxProtocol.cs`, `LineInterlock.cs`, `TwinCAT/AdsFifoBenchBridge.cs` — ADS transport, fail-closed interlocks, mailbox sequencing, and ACK handling.
- `App/Tests/Program.cs` and `App/Tests/Tests.csproj` — executable regression checks and linked production sources.
- `PLC/TwinCAT/DATN/DATN.tsproj`, `PLC/TwinCAT/DATN/PLC1/POUs/` — TwinCAT source and POUs; read `Docs/TAG_MATRIX.md` before changing tag contracts.
- `PLC/Mitsubishi/Sources_20261019/Source/PLC_Q_Session2_Logic.st` — PLC-Q reference logic and single-writer constraints.
- `README.md`, `PROJECT_INDEX.md`, `Docs/TAG_MATRIX.md`, `Docs/PLC_README_ESTOP.md` — project map, tag contract, and safety constraints. Some historical sections contain stale paths; current on-disk names win.

## Runtime/Tooling Preferences

- Use the installed Windows .NET SDK with the `dotnet` CLI and the repository's SDK-style projects. The app/test targets are `.NET 8` Windows WPF and x64.
- Basler builds need the local pylon SDK. The project fallback mentions `C:\Program Files\Basler\pylon 8`; on this workstation pass the actual installed root with `-p:PylonRoot="C:\Program Files\Basler\pylon"` when required. Hikrobot runtime access depends on the local MVS SDK/native `MvCameraControl.dll`.
- TwinCAT editing/building uses Beckhoff TcXaeShell; Mitsubishi editing uses GX Works2. These are source/configuration tools, not safe substitutes for commissioning.
- No repository CI, standard test SDK, xUnit/NUnit/MSTest, or coverage tool is configured. Do not use `dotnet test` as the primary verification command; use the executable runner below.

## Testing & QA

- `App/Tests/Program.cs` is a custom `Check(name, value)` executable runner. It returns nonzero on failure and prints `TOTAL_TEST_FAILURES=...`; it covers tracking, geometry, overlays, camera-slot isolation, GVCP parsing, interlocks, FIFO simulation, protocol packing, and source-level PLC invariants.
- Primary offline QA:
  ```cmd
  dotnet build App/Tests/Tests.csproj -c Debug --nologo
  dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
  ```
- The suite may reference local demo video files under `C:\Users\aml\Desktop\DEMO`; missing media can make media-specific checks environment-dependent. Prefer synthetic frames, fake ADS transports, and `CameraPipelineSimulator` for deterministic checks.
- Read-only PowerShell audits include `Docs/CHECK_MANUAL_BELT_READONLY_20261024.ps1`, `Docs/TWINCAT_CSHARP_SYNC_20261022/READ_ONLY_RUN_CHECK.ps1`, and `Docs/TWINCAT_FIXED_K_OFFLINE_20261023/FIXED_K_REGRESSION.ps1`. Camera diagnostics are not ordinary unit tests and may inspect local network state.
- QA must remain offline and disarmed: no ADS/MC writes, PLC activation/download, TwinCAT Run/Stop, servo/robot start, E-stop manipulation, physical camera reconfiguration, or IO/equipment actuation. The ST-1228 DI0 feedback is auxiliary—not a certified E-stop; physical safety chains remain independent.
