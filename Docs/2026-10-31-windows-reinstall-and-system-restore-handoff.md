# Windows Reinstallation & DATN System Restoration Handoff
**Document Date:** 2026-10-31  
**Project Workspace:** `E:\Orca\DATN` (Maintenance & Development Root)  
**Host Machine:** AML-IPC (Windows 10 Pro 64-bit, Version 10.0.19044 x64)  
**Document Purpose:** Complete, durable operational guide to reinstall Windows on AML-IPC and restore the DATN industrial vision, TwinCAT ADS telemetry, and dual-station line control system to full working order without data loss or safety regressions.

---

## 1. System Overview & Project Topography

### 1.1 Repository Path
- **Active Working Copy:** `E:\Orca\DATN`
- **Historical Workspaces (Reference Only):**
  - `C:\Users\aml\orca\DATN` (historical C: drive path prior to disk consolidation)
  - `D:\DATN_ARCHIVE_20261020` (cold storage archive containing 6.48 GB historical C# builds, 12 TwinCAT stage ZIPs, and legacy desktop projects)

### 1.2 Main Solution & Projects
The active Visual Studio solution and application structure is located under `App/`:

| Path | Type | Purpose | Target Framework / Platform |
| :--- | :--- | :--- | :--- |
| `App/DATN_WPF.sln` | Visual Studio Solution | Main C# WPF vision and dashboard solution | Visual Studio 2022 / VS Code |
| `App/Vision/PlateVision50.csproj` | C# WPF Project (WinExe) | Primary industrial SCADA application (`PlateVision50_V2.exe` / `.dll`) | `net8.0-windows` (x64), `UseWPF=true` |
| `App/Tests/Tests.csproj` | C# Console Test Project (Exe) | Headless regression test suite (`App/Tests/Program.cs`) | `net8.0-windows` (x64), `UseWPF=true` |
| `Dependencies/BaslerCamera/NVKProject.DotNet.BaslerCamera.csproj` | C# Class Library | Basler camera wrapper module | `net8.0-windows` (x64) |
| `Dependencies/Devices/` | C# Linked Source Tree | Shared native Hikrobot MVS & unified camera device implementations (`MvsNativeLoader.cs`, `HikDirectIpCamera.cs`, `GvcpDiscovery.cs`, `UnifiedCameraService.cs`, `MyCamera.cs`, etc.) | Direct project link into `PlateVision50.csproj` & `Tests.csproj` |

### 1.3 Lower Control Layer & PLC Solutions
| Path | Type | Role | Maintenance Boundary |
| :--- | :--- | :--- | :--- |
| `PLC/TwinCAT/DATN_TwinCat.sln` | Beckhoff TwinCAT Solution | TwinCAT 3 master PLC project (`DATN/DATN.tsproj`) | ADS Port 851 scalar telemetry. Stage copy for maintenance; not proof of live controller. |
| `PLC/Mitsubishi/DATN_PLC.gxw` | MELSOFT GX Works2 Project | Mitsubishi Q-Series PLC-Q motion controller project | QD75 motion buffer (`D100..D114`) and `M1000/M1001` handshake reference. |

---

## 2. Git Source Control & Exact Latest Commits to Preserve

### 2.1 Git Remotes and Tracking Branch
- **Remote Name:** `origin`
- **Remote Fetch/Push URL:** `https://github.com/AnhNguyen-2104/DATN.git`
- **Active Branch:** `backup/datn-full-source-20261021`

### 2.2 Exact Commits to Preserve (Verified on 2026-10-31)
Ensure the following sequence of latest commits is preserved in git history upon restoration:

1. **`bc3558e7d7f3998e4ade237d44f909351e0926fb`** (HEAD, 2026-10-31)  
   `fix(camera): enable CONNECT without scan for manual Hikrobot IP and explicit NIC`  
   *Enables manual typed-IP connect on Station 01 and Station 02 for Hikrobot cameras without requiring full MVS subnet scan; enforces explicit NIC isolation.*

2. **`0a8e2d598906d249cb60e8509970928f7e427367`** (2026-10-31)  
   `feat(camera): dual-Hikrobot isolated IP/NIC camera routing and fail-closed validation`  
   *Defaults both Station 01 and Station 02 to Hikrobot; implements per-slot NIC resolution with fail-closed validation; prevents cross-station fallback.*

3. **`1d9920c4c0c6b79d372b4e3a6b919afe34021a93`** (2026-10-29)  
   `docs: add tracking 4-point gantry checkpoint and tag matrix contract updates`

4. **`de6ddb486abfb70579317dce0270b2dbf5350e7e`** (2026-10-29)  
   `fix(ui): preserve model.Mode in MainWindow.SaveSlot to prevent overwriting USB camera slots`

5. **`48abfeeff3babdead981238ee3559b7d1b2a4c00`** (2026-10-29)  
   `feat(ui): wire measured ConveyorVertex array to offline simulation queue`

6. **`c1e144856d38a40f8949b3ce2807cd4b58eeaba7`** (2026-10-29)  
   `fix(overlay): reconcile UI P1..P4 overlay to canonical quadrant vertices`

7. **`3d7458d3cfdc26335d17a5d5b2ddb5a1887f5de4`** (2026-10-29)  
   `test(sim): add yaw coupling regression coverage for measured vertices in CameraPipelineSimulator`

8. **`abe512f8afc1c2d0ba7626d250bb45f111ea3037`** (2026-10-29)  
   `feat(sim): accept measured ConveyorVertex array with strict validation in CameraPipelineSimulator`

9. **`1dc8f9d6f8dae736e359f3bd17b184e0e8dc558a`** (2026-10-29)  
   `fix(vision): address re-review findings on fail-closed pose, rotation geometry, and collinearity`

10. **`adbbec8917f02fc38a6b6d1f414aabded7f6d4d4`** (2026-10-29)  
    `fix(vision): address review findings in MapVertices and CreateTrackedPose`

11. **`c0e9b6ee9e0c2cfdff4afe6d7511bc92379d27fe`** (2026-10-29)  
    `feat(vision): add ConveyorVertex, TrackedWorkpiecePose and canonical MapVertices`

### 2.3 Uncommitted Working Tree Files (CRITICAL BACKUP REQUIREMENT)
In addition to git commits, the local working tree contains modified and untracked files that MUST be backed up before reinstalling Windows:
- **Modified Working Files:** `AGENTS.md`, `App/Vision/Dashboard.cs`, `App/Vision/StationOverview.cs`, `App/Vision/Vision/HikDirectIpCamera.cs`, `App/Vision/Vision/LineMailboxProtocol.cs`, `Dependencies/Devices/HikCameraService.cs`, `Dependencies/Devices/MyCamera.cs`, `Dependencies/Devices/UnifiedCameraService.cs`, `Dependencies/BaslerCamera/PylonNativeLoader.cs`, `PLC/Mitsubishi/...`, `PLC/TwinCAT/...`
- **Untracked Core Dependencies:**
  - `Dependencies/Devices/MvsNativeLoader.cs` (Hikrobot MVS x64 dynamic loader)
  - Diagnostic scripts and JSON benchmarks under `Docs/` (`Docs/START_MVS_CAMERA_RUNTIME_20261028.cmd`, `Docs/CAMERA_GIGE_REAL_CONNECTION_RESULT_20261028.md`, `Docs/FIX_ETHERNET3_GIGE_20261028.ps1`, etc.)
  - Superpowers specs and plans under `Docs/superpowers/`

---

## 3. Post-Reinstall Build and Test Commands

After reinstalling Windows and installing the required SDKs, open a standard Command Prompt (`cmd.exe`) in `E:\Orca\DATN` and execute the following commands in order:

### 3.1 Build & Run Regression Test Suite
```cmd
:: Build the headless test suite
dotnet build App/Tests/Tests.csproj -c Debug --nologo

:: Execute tests offline (Zero hardware, zero ADS)
dotnet run --project App/Tests/Tests.csproj -c Debug --no-build
```
*Acceptance Verification:* The console output MUST finish with:
```
TOTAL_TEST_FAILURES=0
```
and exit with return code `0`.

### 3.2 Build Main WPF Vision Application
```cmd
:: Build Debug configuration
dotnet build App/Vision/PlateVision50.csproj -c Debug --nologo

:: Build Release configuration
dotnet build App/Vision/PlateVision50.csproj -c Release --nologo

:: Build complete WPF Solution
dotnet build App/DATN_WPF.sln -c Debug --nologo
```
*Acceptance Verification:* Build outputs 0 Errors (11 compiler warnings from legacy Hikrobot SDK wrapper interop are expected).

---

## 4. Required .NET SDK, Runtime & Package Dependencies

### 4.1 .NET Environment
- **SDK:** Microsoft .NET 8.0 SDK (x64) (v8.0.400 or later) with Windows Desktop development workload.
- **Runtime:** Microsoft .NET 8.0 Desktop Runtime (x64) (`Microsoft.WindowsDesktop.App 8.0.x`).
- **Pre-packaged Local Runtime:** The repository includes a portable, self-contained .NET 8 runtime under `Runtime\DotNet8\dotnet.exe` used by the production batch launcher.

### 4.2 NuGet Packages
All required packages are resolved automatically via NuGet restore:
- `OpenCvSharp4` (Version `4.13.0.20260214`) — Computer vision image processing primitives.
- `OpenCvSharp4.Windows` (Version `4.13.0.20260214`) — Native x64 OpenCV binaries.
- `OpenCvSharp4.WpfExtensions` (Version `4.13.0.20260214`) — Direct `WriteableBitmap` rendering extensions for WPF.
- `Beckhoff.TwinCAT.Ads` (Version `6.2.521`) — TwinCAT ADS router client for Port 851 scalar communication.

---

## 5. Camera Vendor Architecture: Basler Note vs Actual Hikrobot Hardware

### 5.1 Project Repository Note regarding Basler Pylon
The project retains code references to Basler Pylon:
- `Dependencies/BaslerCamera/NVKProject.DotNet.BaslerCamera.csproj`
- `Dependencies/BaslerCamera/PylonNativeLoader.cs`
- References search for `%PYLON_ROOT%`, `C:\Program Files\Basler\pylon`, or fallback `libs\Basler.Pylon.dll`.

### 5.2 Physical Workstation Hardware Reality (CRITICAL)
**The physical AML-IPC workstation has ZERO Basler cameras connected.**  
Actual physical production hardware consists of **TWO Hikrobot GigE Vision cameras**:

1. **Station 01 (Conveyor / Gantry Station):**
   - **Physical Model:** Hikrobot `MV-CS016-10GC`
   - **Serial Number:** `DA7403922`
   - **Hardware MAC:** `34:BD:20:6E:8D:AB`
   - **Interface:** Intel I350 Gigabit Network Connection (`Ethernet 2`)
   - **Vendor Setting:** `HIKROBOT`

2. **Station 02 (Conveyor / SCARA Station):**
   - **Physical Model:** Hikrobot `MV-CE050-31GC`
   - **Serial Number:** `J95682667`
   - **Hardware MAC:** `34:BD:20:05:B6:E4` (DeviceUserID: `CAM_COLOR`)
   - **Interface:** Intel I350 Gigabit Network Connection (`Ethernet 3`)
   - **Vendor Setting:** `HIKROBOT`

*Operator Rule:* Do NOT spend time troubleshooting missing Basler cameras. The Basler wrapper is maintained solely for legacy compilation compatibility. Both stations in production must be configured and operated as **Hikrobot** GigE cameras.

---

## 6. Hikrobot MVS Native SDK & `MvCameraControl.dll` Dependency

### 6.1 Native MVS Runtime Requirement
Hikrobot GigE Vision communication relies on the native 64-bit C library `MvCameraControl.dll`.  
Hikrobot MVS (Machine Vision Software) must be installed on Windows.

### 6.2 Native Loader Mechanism (`Dependencies/Devices/MvsNativeLoader.cs`)
The application discovers and stages the native runtime automatically via `MvsNativeLoader.Ensure()`:
1. Probes candidate directory paths in priority order:
   - `C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64\`
   - `C:\Program Files\Common Files\MVS\Runtime\Win64_x64\`
   - `%MVS_RUNTIME_PATH%` / `%MVCAM_COMMON_RUNENV%\Win64_x64` / `%MVS_ROOT%\Runtime\Win64_x64`
   - `C:\Program Files (x86)\MVS\Development\Bin\win64\`
   - `C:\Program Files\MVS\Development\Bin\win64\`
2. Validates that `MvCameraControl.dll` is an authentic 64-bit binary (preventing 32-bit `BadImageFormatException` crashes).
3. Prepends the resolved directory to the process `PATH` environment variable.
4. Invokes `NativeLibrary.SetDllImportResolver` on the calling assembly to bind P/Invoke entry points directly.
5. If missing, the application fails closed safely, recording diagnostic details in `MvsNativeLoader.LastError`.

---

## 7. Manual Per-Station Camera Setup Fields & Network Topology

### 7.1 WPF UI Input Fields (MainWindow.xaml)
The manual camera configuration controls in the WPF dashboard are:

| Station | UI Field Name | Field Label | Purpose | Permitted Vendor |
| :--- | :--- | :--- | :--- | :--- |
| **Station 01** | `S1CamIpBox` | `IP` | Target IPv4 address of Camera 1 | `HIKROBOT` (or `BASLER` via scan) |
| **Station 01** | `S1HostIpBox` | `NIC` | Local host IPv4 address of `Ethernet 2` | Bound to Station 01 subnet |
| **Station 01** | `S1VendorBox` | `VENDOR` | Vendor selection dropdown (`SelectedIndex="0"`) | Default: `HIKROBOT` |
| **Station 02** | `S2CamIpBox` | `IP` | Target IPv4 address of Camera 2 | `HIKROBOT` (or `BASLER` via scan) |
| **Station 02** | `S2HostIpBox` | `NIC` | Local host IPv4 address of `Ethernet 3` | Bound to Station 02 subnet |
| **Station 02** | `S2VendorBox` | `VENDOR` | Vendor selection dropdown (`SelectedIndex="0"`) | Default: `HIKROBOT` |
| **Diagnostic Tab** | `CameraIpBox` / `HostIpBox` | `IP camera` / `IP PC` | Legacy manual connect test boxes | Dedicated bench testing |

### 7.2 Ground Truth Network Configuration (Do NOT Invent IPs)
The workstation uses physically isolated Intel I350 NICs. Operators must inspect Windows Network Connections (`ncpa.cpl`) to verify actual IPs. The verified baseline network parameters are:

- **Station 01 Subnet (`Ethernet 2`):**
  - **Adapter Alias:** `Ethernet 2` (Intel I350 MAC: `78-D0-04-2B-02-3A`)
  - **Host NIC IP (`S1HostIpBox`):** `169.254.113.10` (or baseline alias `169.254.113.20`), Subnet Mask `255.255.255.0` (`/24`)
  - **Camera 1 IP (`S1CamIpBox`):** `169.254.113.1`
- **Station 02 Subnet (`Ethernet 3`):**
  - **Adapter Alias:** `Ethernet 3` (Intel I350 MAC: `78-D0-04-2B-02-3B`)
  - **Host NIC IP (`S2HostIpBox`):** `192.168.50.10`, Subnet Mask `255.255.255.0` (`/24`)
  - **Camera 2 IP (`S2CamIpBox`):** `192.168.50.2`

### 7.3 Fail-Closed IP/NIC Rules
- **No Cross-Station Leaks:** `StationCameraController.ResolveCameraNic` rejects empty or invalid host NIC addresses and will NEVER fall back across stations.
- **Unicast GVCP Probe:** Manual typed-IP connect sends GVCP discovery UDP datagrams exclusively as unicast to the target camera IP on port 3956; it does NOT broadcast to `255.255.255.255`.
- **Slot Ownership Collision:** `CameraOwnershipRegistry` enforces single ownership per camera IP/serial across the entire application process.

---

## 8. Runtime Launch Commands & Executable Paths

The repository provides several launch scripts in the root directory:

| Script | Executable Target | Description & Operational Mode |
| :--- | :--- | :--- |
| `START_DATN.cmd` | `Release\PlateVision50_V2.dll` | **Production Launcher.** Starts line control using embedded `Runtime\DotNet8\dotnet.exe`. Enforces single-instance named Mutex (`DATN_AML_LINE_CONTROL_UI`). |
| `START_DATN_STATION_PREVIEW.cmd` | `ReleaseCandidate_COMPACT_SCADA_20261021\PlateVision50_V2.exe` | **Station Dashboard Preview.** Launches compact industrial SCADA candidate. Defaults to safe, disarmed mode. |
| `START_DATN_PREVIEW.cmd` | Forwards to `START_DATN_STATION_PREVIEW.cmd` | Convenience alias forwarder. |
| `START_DATN_OVERVIEW_MODE_PREVIEW.cmd` | `ReleaseCandidate_OVERVIEW_MODE_20261021\PlateVision50_V2.exe` | Single-screen overview candidate preview. Checks for active UI instances before running. |
| `OPEN_DATN_WPF.cmd` | `App\DATN_WPF.sln` | Opens main C# solution in Visual Studio. |
| `OPEN_DATN_TwinCat.cmd` | `PLC\TwinCAT\DATN_TwinCat.sln` | Opens TwinCAT 3 master project in Beckhoff `TcXaeShell.exe`. |
| `OPEN_DATN_PLC.cmd` | `PLC\Mitsubishi\DATN_PLC.gxw` | Opens Mitsubishi PLC project in MELSOFT GX Works2 (`GD2.exe`). |
| `Docs/START_MVS_CAMERA_RUNTIME_20261028.cmd` | Hikrobot MVS GUI | Launches vendor MVS tool using process-local MVS runtime paths. |
| `Docs/START_HIK_IP_CONFIG_RUNTIME_20261028.cmd` | Hikrobot IP Config Tool | Utility to inspect or modify static IPs in camera non-volatile memory. |

---

## 9. Read-Only Boundaries & Offline Simulation Contracts

### 9.1 TwinCAT ADS Port 851 Read-Only Telemetry
- The live connection to TwinCAT ADS Port 851 reads **primitive scalar variables only** (`KnownSourceSymbols` in `LineMailboxProtocol.cs`, 57 symbols total: `bSystemEStopOK`, `rConveyorSpeed`, `rMD200_SetFrequency_Hz`, `nQueueCount`, `MAIN.bPlcQConnected`, etc.).
- **Zero Structs / Multi-Word Arrays:** No composite structs or multi-byte arrays are written across ADS.
- **Direct ADS Pose Writes Disabled:** `AdsPreview.WritePose` explicitly throws `NotSupportedException`. Direct coordinate writing over ADS is disarmed.

### 9.2 Four-Vertex Planar Tracking (Purely Offline SIL)
- The 4-point workpiece pose estimation contract (`BeltCoordinates.MapVertices`, `TrackedWorkpiecePose`, `CameraPipelineSimulator`) runs **strictly in offline Software-in-the-Loop (SIL) simulation**:
  - Vertices are ordered counter-clockwise in Cartesian quadrants: $V_1 (+halfX, +halfY, \text{Quad I})$, $V_2 (-halfX, +halfY, \text{Quad II})$, $V_3 (-halfX, -halfY, \text{Quad III})$, $V_4 (+halfX, -halfY, \text{Quad IV})$.
  - Mathematical Shoelace collinearity/degeneracy validation fails closed ($\text{area} < 1.0\text{ mm}^2$).
  - Virtual queue simulation transforms vertices with yaw coupling into 15-word binary QD75 motion command packets (`D100..D114`).
  - **Zero Motion Writes:** In-memory queue only; zero writes to physical Mitsubishi buffer memory.

### 9.3 Video Simulation Mode (`--demo`)
- Launching with `--demo` opens a local AVI video file (`C:\Users\aml\Desktop\DEMO\1.avi`).
- This mode is **image-only** for vision algorithm validation and does NOT synchronize with physical conveyor encoder pulses.

### 9.4 Station 02 (SCARA) Reserved State
- Station 02 SCARA logic is **uncommissioned and reserved**. The dashboard displays truthful `RESERVED` / `OFFLINE` indicators and never asserts fake online telemetry.

---

## 10. Strict Safety Rules: No ADS / PLC / Motion / Servo / Glue / Safety Actions

When restoring or working on this repository after a Windows reinstallation, the following boundaries MUST be strictly observed:

1. **NO TwinCAT PLC Download or Activation:**  
   Do NOT download, activate, or restart TwinCAT PLC runtimes merely to verify repository health. The files in `PLC/TwinCAT/` are source copies for maintenance.
2. **NO Mitsubishi MC Protocol Writes:**  
   Do NOT send write packets to Mitsubishi PLC-Q over Ethernet MC Protocol.
3. **NO ADS Motion Commands:**  
   `MAIN.bStartGlueCycle` and `MAIN.bTest_WriteM1000` must remain `FALSE`. The compile-time gate `bLegacyMotionPublisherAllow := FALSE` must remain uncommissioned.
4. **NO Actuator Energization:**  
   Do NOT run conveyor MD200 inverter, do NOT energize servo motors, and do NOT trigger glue dispenser pneumatic valves.
5. **Auxiliary E-Stop Telemetry Boundary:**  
   The E-stop status displayed in the software (`MAIN.bSystemEStopOK`, driven by Crevis ST-1228 Slot 6 DI0) is **auxiliary diagnostic telemetry only**. It is NOT a safety-rated circuit and must never be relied upon as a primary emergency stop.
6. **Physical Safety Isolation:**  
   Certified safety relays and physical E-stop pushbuttons must remain independently wired to drop power to drives and inverters.

---

## 11. Remaining Hardware, PLC, Calibration & Safety Prerequisites

The following steps CANNOT be resolved in C# source code and must be completed by a qualified commissioning engineer on the physical machine:

1. **Physical Cabling & Intel I350 NIC Configuration:**
   - Connect Camera 1 to `Ethernet 2` (`78-D0-04-2B-02-3A`).
   - Connect Camera 2 to `Ethernet 3` (`78-D0-04-2B-02-3B`).
   - Configure static IPv4 addresses in Windows. Remove ghost IP aliases (e.g. `169.254.29.19`).
   - In Intel NIC Advanced Adapter properties, configure:
     - **Jumbo Packet:** `9014 Bytes`
     - **Receive Descriptors (Buffers):** `2048`
2. **Optical Calibration & Lens Adjustment:**
   - Under production ring illumination, focus and lock aperture set-screws on both C-mount lenses.
   - Run a certified 100 mm gauge block down the conveyor to calibrate true `FixedMmPerPixel` (replacing the nominal $0.20\text{ mm/px}$ estimate).
3. **Physical Survey of Camera Offsets:**
   - Calibrate laser distance from Camera 1 optical center to conveyor workpiece baseline (`rCameraToWorkStart_mm`, nominal $200.0\text{ mm}$).
   - Calibrate lateral offset (`rCameraOffsetY_mm`, nominal $85.0\text{ mm}$).
4. **Hardware Encoder Latching (Crevis ST-5114):**
   - Connect Camera 1 flash strobe output (Line 2 opto-isolated) to Crevis ST-5114 high-speed latch input.
   - Verify hardware pulse latching associates exact encoder position with optical exposure time at conveyor speeds up to $150\text{ mm/s}$.
5. **Mitsubishi PLC-Q / QD75 Ladder Program Verification:**
   - Open `PLC/Mitsubishi/DATN_PLC.gxw` in GX Works2.
   - Perform online ladder comparison against the live Q-Series CPU.
   - Verify single-writer coil ownership: confirm Y20 (Servo 1 enable/brake) and Y25 (Glue pump trigger valve) have exactly one controlling rung.
   - Confirm QD75 buffer memory `D100..D114` mapping matches the 15-word binary structure contract.
   - Test `M1000` (Start) / `M1001` (Acknowledge) handshake logic.
6. **TwinCAT 3 Runtime Commissioning Gates:**
   - Once physical calibration and safety are verified, open `PLC/TwinCAT/DATN_TwinCat.sln` in Beckhoff TcXaeShell.
   - In `PLC1/POUs/MAIN.TcPOU`, adjust the commissioning constants:
     ```structured-text
     cQueueMotionCommissioned : BOOL := TRUE;
     cLineAutoCommissioned : BOOL := TRUE;
     bLegacyMotionPublisherAllow : BOOL := TRUE;
     ```
   - Rebuild, generate new boot project (`Build -> Generate Boot Project`), and activate onto the runtime.
7. **Certified Physical Emergency Stop Validation:**
   - Conduct physical trip tests of all mechanical E-stop pushbuttons to ensure positive-guided safety relays reliably cut 24V/220V power to MD200 inverter and QD75 servo drives.

---

## 12. Windows Reinstallation Backup Checklist

Before wiping the operating system drive or reinstalling Windows, complete this backup checklist:

### Checklist Item 1: Complete Source Code Working Tree
- [ ] Archive the entire directory `E:\Orca\DATN` (or copy to external storage).
- [ ] Ensure all **uncommitted modified files** (20 files, including `AGENTS.md`, `Dashboard.cs`, `HikDirectIpCamera.cs`, etc.) are preserved.
- [ ] Ensure all **untracked files** (38 files, notably `Dependencies/Devices/MvsNativeLoader.cs`, `Docs/`, `Docs/superpowers/`) are preserved.

### Checklist Item 2: Git Repository Metadata & Remotes
- [ ] Verify `git status` and record the current HEAD SHA: `bc3558e7d7f3998e4ade237d44f909351e0926fb`.
- [ ] Confirm remote URL: `git remote get-url origin` (`https://github.com/AnhNguyen-2104/DATN.git`).
- [ ] Confirm tracking branch: `backup/datn-full-source-20261021`.

### Checklist Item 3: Camera IP / NIC Configuration & App Settings
- [ ] Backup camera slots settings file:
  `%LOCALAPPDATA%\PlateVision50_V2\camera_slots.json`
- [ ] Backup MVS configuration and device parameters:
  `Docs/MVS_LocalConfig_backup_20261028_190859.ini` and `%APPDATA%\MVS\`
- [ ] Backup camera hardware binding dumps:
  `Docs/CAMERA_GIGE_BINDINGS_20261028_141638.json` and `Docs/CAMERA_NIC_BASELINE_20261028_132716.json`.

### Checklist Item 4: SDK Installers & Vendor Software
Preserve setup installers and license files for:
- [ ] **Hikrobot MVS x64 Installer** (Machine Vision Software v3.x or v4.x x64).
- [ ] **Basler Pylon SDK Installer** (Pylon 8.x or 7.x x64).
- [ ] **Beckhoff TwinCAT 3 XAE** (e.g. TwinCAT 3.1 Build 4024.x) + TwinCAT ADS runtime installer.
- [ ] **Beckhoff TwinCAT Target Licenses** (TwinCAT System ID, license files under `C:\TwinCAT\3.1\Target\License\`).
- [ ] **Mitsubishi MELSOFT GX Works2** installer and serial key.
- [ ] **Microsoft .NET 8.0 SDK (x64)** installer (`dotnet-sdk-8.0.xxx-win-x64.exe`).
- [ ] **Microsoft .NET 8.0 Desktop Runtime (x64)** installer (`windowsdesktop-runtime-8.0.xxx-win-x64.exe`).

### Checklist Item 5: Runtime Folders & Archive Partitions
- [ ] Verify standalone runtime folder: `E:\Orca\DATN\Runtime\DotNet8\`
- [ ] Verify published releases: `E:\Orca\DATN\Release\` and `E:\Orca\DATN\ReleaseCandidate*`
- [ ] Verify cold-storage archive partition: `D:\DATN_ARCHIVE_20261020\` (contains legacy projects and 12 TwinCAT backup stages; do not format drive `D:` during Windows install).

### Checklist Item 6: Windows NIC Configuration & Driver Parameters
Record network adapter parameters from Windows:
- [ ] `Ethernet 2` (Intel I350 #1, MAC `78-D0-04-2B-02-3A`):
  - IPv4: `169.254.113.10` (or `.20`), Subnet Mask: `255.255.255.0`
  - Jumbo Packet: `9014 Bytes`
  - Receive Buffers: `2048`
- [ ] `Ethernet 3` (Intel I350 #2, MAC `78-D0-04-2B-02-3B`):
  - IPv4: `192.168.50.10`, Subnet Mask: `255.255.255.0`
  - Jumbo Packet: `9014 Bytes`
  - Receive Buffers: `2048`
- [ ] `Ethernet 4` (TwinCAT EtherCAT RT-Ethernet adapter):
  - Beckhoff TwinCAT RT-Ethernet driver binding enabled.
- [ ] Primary LAN (`Ethernet`):
  - Office / Factory LAN IP for ADS routing (baseline `192.168.10.x`).

---

## 13. System Restoration Step-by-Step Procedure

1. **Install Windows 10 Pro x64** on AML-IPC.
2. **Install Chipset & Intel Network Drivers** (specifically for Intel I350 dual-port controller).
3. **Configure Static Network Adapters** according to Section 12, Checklist Item 6.
4. **Install Application SDKs:**
   - Install .NET 8.0 SDK (x64) and .NET 8.0 Desktop Runtime (x64).
   - Install Hikrobot MVS x64 (confirm `C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64\MvCameraControl.dll` exists).
   - Install Beckhoff TwinCAT 3 XAE (restore Beckhoff target license if necessary).
   - Install MELSOFT GX Works2.
5. **Restore Repository:**
   - Copy or restore repository tree to `E:\Orca\DATN`.
   - Restore `%LOCALAPPDATA%\PlateVision50_V2\camera_slots.json`.
6. **Execute Offline Build & Test Verification:**
   - Run `dotnet build App/Tests/Tests.csproj -c Debug --nologo`.
   - Run `dotnet run --project App/Tests/Tests.csproj -c Debug --no-build` (verify `TOTAL_TEST_FAILURES=0`).
   - Run `dotnet build App/Vision/PlateVision50.csproj -c Release --nologo`.
7. **Perform Bench Validation (Zero Actuation):**
   - Execute `START_DATN_STATION_PREVIEW.cmd` or `START_DATN.cmd`.
   - Test camera streams via manual IP connection in GUI (`S1CamIpBox: 169.254.113.1`, `S1HostIpBox: 169.254.113.10`; `S2CamIpBox: 192.168.50.2`, `S2HostIpBox: 192.168.50.10`).
   - Confirm ADS Port 851 reads telemetry in read-only mode.
   - Do NOT send robot commands or download PLC logic until on-site physical commissioning prerequisites (Section 11) are satisfied.
