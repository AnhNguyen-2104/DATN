# DATN WPF — ORCA IMPLEMENTATION RESULT: STATION INTERLOCK + CAMERA AT STATIONS
Task ID: DATN-WPF-STATION-INTERLOCK-20261021  
Execution date: 2026-10-22 (session start 2026-10-21 ~23:35 AML-IPC)  
Workspace verified: **`E:\Orca\DATN` (Only active workspace — no edits made to C: drive)**  
Branch: `backup/datn-full-source-20261021`  
Scope: WPF Station Interlock, Camera preparation per station, speed preset sync, fail-closed safety state machine, offline test suite and visual QA.

---

## 1. Files Changed & Restorable Backups

### Rollback / Backup Directory:
`Docs/WPF_BACKUP_20261021_STATION_INTERLOCK/`  
Every file was SHA-256 hashed and backed up before modification:
```
65ec710b5f1fbfcf8bf61a79a152eb905abbfb97a82b7c5b3b74c7fac3051781  Dashboard.cs
e074f21be9fb3423b8b0d489230ac79fbc4d3d20cdd0980fe832c09286713a32  MainWindow.xaml
50960a7d13f40fe8f98c26938b201b54da8335ca175a4d9c437c3436cfd57f5b  MainWindow.xaml.cs
96297b9ae45ca2bafa07191b17a1e10dc621533d0e9219ae4a6eef6fa02b7612  StationDashboards.cs
a9b1acbc196cf66aa829a82370b3822d61b0396a37f4509eeedf75460981d812  StationOverview.cs
2320b171ed12c997bacbca2ace6b80b060137659144b99511a2b9da3b7bf5005  Program.cs (Tests)
18255f1057dca7e9c2276a558ea3f5bee4ab9fd6555359ba6ee9cda6c471d7de  Tests.csproj
```

### Exact Files Modified / Created:
1. `App/Vision/Vision/LineInterlock.cs` *(NEW)*:
   - Pure C# interlock model and speed preset validator without WPF/ADS/OpenCV dependencies.
   - Contains: `InterlockState`, `InterlockCheckId`, `InterlockInputs`, `InterlockCheck`, `InterlockEvaluation`, `LineSpeedPreset`, and `LineInterlockModel`.
   - 8-point checklist: AdsLink, Estop, PlcQ, GantryFault, CameraFrame, SpeedSetpoint, AutoContract, ScaraExcluded.
   - Fail-closed rules in production; full offline mock lifecycle support when `MockMode == true`.
2. `App/Vision/MainWindow.xaml`:
   - Collapsed `VisionOperationTab` and `CameraSourceTab` from visible navigation (`Visibility="Collapsed"`).
   - Toolbar presents exactly the 6 operational tabs: `LINE OVERVIEW`, `STATION 01`, `STATION 02`, `SHARED CONVEYOR`, `SETTINGS`, `ALARMS & LOGS`.
   - **Station 01 (Gantry)**: Added Camera 01 IP connection strip (`S1CamIpBox`, `S1HostIpBox`, `CONNECT`, `DISCONNECT`, `DETECT ON`, `DETECT OFF`, status badge). Embedded live tracking overlay (`S1Overlay`).
   - **Station 02 (SCARA)**: Truthful `NOT INSTALLED / NOT COMMISSIONED` display; disabled Camera 02 IP inputs with explicit tooltips. Excluded from Gantry-only AUTO interlock.
   - **Line Overview**: Consolidated `SHARED CONVEYOR + MACHINE COMMANDS` panel:
     - Shared speed preset in mm/s (`OvPresetSpeedBox`) with range validation `[1.0 .. 150.0 mm/s]`.
     - Derived Hz info: `Hz: NOT CALCULATED - mm/s to Hz factor is not verified by measurement.`
     - Compact buttons: `START`, `STOP`, `RESET`, `TEST ONLY`, `PC VISION START`, `PC VISION STOP`, `MANUAL MODE`, `ADVANCED / TAGS`.
     - 8-indicator live interlock checklist with plain English primary blocking reason.
   - **Shared Conveyor**: Added synced speed preset panel (`CvPresetSpeedBox`, `APPLY`) reflecting the exact same source of truth.
   - **Settings (System)**: Added `OPEN ADVANCED CAMERA / VIDEO QA` for maintenance/offline video testing without polluting operator toolbar.
3. `App/Vision/MainWindow.xaml.cs`:
   - Added `TryConnectCameraIp` helper for station-level camera connections.
   - Handlers for `S1ConnectCamera_Click`, `S1DisconnectCamera_Click`, `S1VisionStart_Click`, `S1VisionStop_Click`, `S1Overlay_SizeChanged`.
   - Retargeted internal navigation away from the collapsed vision tabs to `GantryDetailTab`.
   - Added `--mock` CLI flag support for offline QA interlock verification.
4. `App/Vision/Dashboard.cs`:
   - Wired `_lineInterlock` model and shared `_presetSpeedMmS` into `RefreshDashboardAsync()`.
   - Implemented `BuildInterlockInputs()`, `RefreshInterlockUi()`, `ApplyPresetSpeed()`.
   - Implemented `SysLineStart_Click`, `SysLineStop_Click`, `SysLineReset_Click`, `LineMockMode_Changed`.
   - Synchronized LINE OVERVIEW and SHARED CONVEYOR preset boxes atomically.
5. `App/Vision/StationOverview.cs`:
   - Updated station vision and sensor edge handlers.
   - Integrated `UpdateS1CameraLabels()` into vision refresh.
   - Guaranteed `MANUAL MODE` entry immediately revokes test-only mock and disarms writes.
6. `App/Tests/Tests.csproj`:
   - Linked `<Compile Include="..\Vision\Vision\LineInterlock.cs" Link="LineInterlock.cs"/>`.
7. `App/Tests/Program.cs`:
   - Added 54 unit tests covering every branch: speed parsing, bounds, offline fail-closed, individual check failures, MockMode lifecycle, state transitions, reset gates, regression checks.

---

## 2. Build & Test Results

### Build Status:
- Solution: `App/PlateVision50.sln`
- Configuration: `Debug|x64`, `Release|x64`, `Release|Any CPU`
- Compiler: `0 Error(s)`
- Warnings: `11 Warning(s)` (all pre-existing CS8600/CS8603/CS8618 nullability warnings inside third-party `Dependencies\Devices\HikCameraService.cs` and `MyCamera.cs`).

### Automated Test Suite (`App/Tests/Tests.csproj`):
```
PASS single product crossing X+ emits once
PASS direction X- emits once
PASS direction Y+ emits once
PASS plate first detected after trigger line must not trigger
PASS 2 plates crossing 50% independently
PASS reset does not trigger on appearing at right half
PASS empty frame does not detect
PASS rotated geometry angle 0
PASS rotated geometry angle 15
PASS rotated geometry angle -30
PASS rotated geometry angle 35.5
PASS rotated geometry angle 44
PASS video -> simulated FIFO ACK 4
PASS video -> simulated FIFO ACK 5
PASS video -> simulated FIFO ACK 6
PASS recorded multi-product video crosses -> FIFO mock end-to-end
PASS legacy direct ADS pose write is fail-closed without PLC access
PASS PLC FIFO read-only ADS 4 glue points
LINE INTERLOCK / AUTO CONTRACT FAIL-CLOSED TESTS:
PASS interlock: speed 120 parses
PASS interlock: speed bounds accepted
PASS interlock: blank speed rejected
PASS interlock: non-numeric speed rejected
PASS interlock: speed 0 rejected
PASS interlock: speed 151 rejected
PASS interlock: NaN speed rejected
PASS interlock: Hz not calculated without verified calibration or zero factor
PASS interlock: Hz preview marked calculated not measured
PASS interlock: complete snapshot is READY
PASS interlock: exactly 8 checks in contract order
PASS interlock: ADS offline blocks start, stop and reset
PASS interlock: offline START refused
PASS interlock: offline STOP refused with physical E-STOP advice
PASS interlock: offline RESET refused
PASS interlock: E-stop not OK blocks start
PASS interlock: global stop blocks start
PASS interlock: PLC-Q disconnect blocks start
PASS interlock: gantry fault blocks start and reports cycle context
PASS interlock: busy gantry cycle alone does not block start
PASS interlock: stale camera frame blocks start
PASS interlock: missing camera blocks start
PASS interlock: out-of-range speed blocks start
PASS interlock: non-finite speed blocks start
PASS interlock: SCARA station not excluded blocks Gantry-only AUTO
PASS interlock: uncommissioned AUTO mailbox blocks production start
PASS interlock: mock mode makes contract READY for offline demo
PASS interlock: mock START accepted and names TEST ONLY no write
PASS interlock: START acknowledged -> RunningAcknowledged
PASS interlock: RESET refused while running
PASS interlock: STOP accepted when online and not a certified E-stop
PASS interlock: RESET accepted after STOP
PASS interlock: STOP acknowledged -> NotReady
PASS interlock: START accepted only from READY
PASS interlock: safety loss while running latches FAULT and allows reset
PASS interlock: mock all-false inputs are READY and startable
PASS interlock: mock reports all 8 checks OK with TEST ONLY SIMULATION details
PASS interlock: mock START accepted with no ADS and names TEST ONLY no write
PASS interlock: mock START acknowledged -> RunningAcknowledged
PASS interlock: mock STOP accepted from running without ADS
PASS interlock: mock RESET accepted after STOP -> NotReady
PASS interlock: production offline fail-closed regression
PASS interlock: mock off restores production Offline fail-closed rules
PASS interlock: mock off restores production NotReady on failed check
TOTAL_TEST_FAILURES=0
```

---

## 3. UI Visual QA & Screenshots

### Release Candidate Output:
`ReleaseCandidate/20261021_StationInterlock/`  
(The active `Release` directory and `START_DATN.cmd` were NOT overwritten).

### Archived Visual Evidence:
Stored under `Docs/ORCA_STATION_INTERLOCK_20261021_SCREENSHOTS/`:
1. `01_mock_ready.png`: `LINE OVERVIEW` in `TEST ONLY` mode. All 8 indicators teal (OK). State: `READY TO REQUEST · TEST ONLY`. `START` button enabled.
2. `02_mock_running_ack.png`: `START` invoked. PC vision tracking running, Track IDs #3 and #4 highlighted with corner glue points. State: `RUNNING ACKNOWLEDGED · TEST ONLY`. `START` locked, `STOP` enabled.
3. `03_mock_stop_requested.png`: `STOP` invoked. Coordinated stop requested, tracking halted, overlays cleared. State: `STOP REQUESTED · TEST ONLY`. `RESET` enabled.
4. `04_mock_reset_ready.png`: `RESET` invoked. State latched back to `READY TO REQUEST · TEST ONLY`.
5. `05_production_fail_closed.png`: Production mode with `TEST ONLY` off. `START` is immediately disabled/fail-closed. Exact failure reason displayed: `Emergency stop circuit not OK or button pressed` and `TwinCAT AUTO mailbox is not commissioned`.
6. `06_1366_overview.png`: Visual layout verified at 1366×768 resolution. No clipping, no overlapping labels, camera panels balanced 50/50.
7. `07_1366_station01.png`: `STATION 01 (GANTRY)` at 1366×768. Top camera strip with IP entry, CONNECT, DISCONNECT, DETECT ON/OFF. Live video viewport and telemetry cards intact.
8. `08_1366_shared_conveyor.png`: `SHARED CONVEYOR` tab at 1366×768. Synced speed preset in mm/s and telemetry display.
9. `09_pc_vision_tracking_demo.png`: Live demo video crossing the 50% FOV line with PC vision tracking active (`MIDLINE CROSS: #5 DEMO X=0.45 Y=-24.18 A=-39.81° | NO ADS WRITE`).

---

## 4. Pending Commissioning Blockers (Prior to Live Production Actuation)

Per `AGENTS.md` and `Docs/TWINCAT_LINE_MASTER_HMI_CONTRACT_20261021.md`, the following gates MUST be cleared on-site by engineering before live automatic motion can be armed:
1. **Physical Dual-Channel E-Stop Verification**:
   The `GVL_MD200_IO.bEStopFeedback` / `MAIN.bSystemEStopOK` bit read from ST-1228 slot 6 DI0 is an **auxiliary status feedback only**, NOT a safety-rated circuit. Hardware dual-channel safety relays must remove hazardous energy directly from MD200 and Gantry drives.
2. **Empirical Belt Speed Calibration**:
   The conversion `mm/s -> Hz` has **never been calibrated on AML-IPC**. WPF displays `Hz: NOT CALCULATED - mm/s to Hz factor is not verified by measurement.`. Engineering must perform at least 3 physical multi-point speed vs frequency measurements before enabling PLC speed conversion.
3. **TwinCAT Master AUTO Contract Activation**:
   The ADS handshake symbols (`MAIN.bHmiLineStartReq`, `MAIN.udiHmiLineCmdSeq`, `MAIN.bHmiHeartbeat`, `MAIN.nLineMasterState`) are declared in source but are **not deployed or activated on the running TwinCAT runtime**. `AutoContractCommissioned` remains compile-time `false` in WPF.
4. **PLC-Q / QD75 Gantry Handshake**:
   `MAIN.bPlcQConnected` and `M1000`/`M1001` handshake against Mitsubishi PLC-Q require on-site supervised verification with Gantry homed and physical limits confirmed.
5. **Station 02 (SCARA)**:
   Remains completely uncommissioned. It is safely excluded from Gantry-only AUTO interlocks.
