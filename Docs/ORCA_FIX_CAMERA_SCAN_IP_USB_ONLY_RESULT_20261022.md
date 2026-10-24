# ORCA IMPLEMENTATION RESULT: CAMERA SCAN FIX — TWO MODES (IP + USB ONLY) & GVCP DISCOVERY
**Task ID:** DATN-CAMERA-SCAN-CONNECT-FIX-20261022  
**Date:** 2026-10-22  
**Authoritative Workspace:** `E:\Orca\DATN` (Only active workspace — no modifications to C: drive)  
**Solution:** `E:\Orca\DATN\App\PlateVision50.sln`  
**Main Project:** `E:\Orca\DATN\App\Vision\PlateVision50.csproj`  
**Test Harness:** `E:\Orca\DATN\App\Tests\Tests.csproj`  
**Release Candidate:** `E:\Orca\DATN\ReleaseCandidate\20261022_CameraScan_IpUsbOnly\`  
**Backup Manifest:** `Docs\WPF_BACKUP_CAMERA_SCAN_20261022\SHA256_MANIFEST.txt`  

---

## 0. Executive Summary & Verification Highlights
All requirements specified in `Docs\ORCA_FIX_CAMERA_SCAN_IP_USB_ONLY_20261022.md` have been fully implemented, offline-tested, and visually verified on the AML-IPC workstation:
1. **Removed `AUTO` Mode Completely:** `CameraTransportMode` now contains exclusively `Usb` and `Ip`. Both Station 01 and Station 02 UI toolbars present only `IP` and `USB`. Station 01 defaults to `IP` and Station 02 defaults to `USB`. Legacy `"Auto"` persisted preferences are migrated automatically to station defaults (`Ip` for 1, `Usb` for 2).
2. **Real Hardware Identification on AML (169.254.113.1):** Non-invasive GVCP discovery over Ethernet 2 (`169.254.113.10`) confirmed the responder at `169.254.113.1` is a genuine **Hikrobot MV-CS016-10GC** camera (Serial: `DA7403922`, MAC: `34:BD:20:6E:8D:AB`, Firmware: `V4.0.44 250410 1500180`). It is now discovered automatically upon clicking `SCAN` in IP mode and populated into the DEVICE dropdown.
3. **USB3 Vision Scan Truthfulness:** On AML-IPC, Windows `Get-PnpDevice -PresentOnly` confirms no physical USB3 camera is currently connected. Clicking `SCAN` in USB mode truthfully reports `NO USB CAMERA` with an actionable operator hint (`Check USB3 cable/port; Windows and pylon Viewer report no connected device`) without creating fake devices or reporting false errors.
4. **No Silent Swallowing of SDK Errors:** Replaced the previous `catch { }` block in `StationCameraController` with structured `CameraScanIssue` diagnostics. Any exception from Basler pylon or Hikrobot MVS is captured with vendor, transport, exception type, and message, displayed on UI as `HIK SDK ERROR` or `BASLER SDK ERROR` with an expandable details panel, and logged to `%LOCALAPPDATA%\PlateVision50_V2\logs\camera_scan.log`.
5. **Clean Industrial UI at 1366×768 & 1579×932:** The toolbar is compact:
   $$\text{CAM } x \mid [\text{IP } \triangledown] \mid \text{IP } [169.254.113.1] \mid \text{NIC } [] \mid \text{VENDOR } [\text{HIKROBOT } \triangledown] \mid \text{DEVICE } [\text{ComboBox}] \mid \text{SCAN} \mid \text{CONNECT} \mid \text{DISCONNECT} \mid \text{OFFLINE}$$
   When switching to `USB`, the IP/NIC/Vendor row collapses completely.
6. **Zero Regressions & Safety Invariants:** 220 automated unit checks passed (`TOTAL_TEST_FAILURES=0`). Zero PLC/TwinCAT/ADS writes, zero robot motion, and SCARA robot remains strictly `NOT COMMISSIONED`.

---

## 1. Baseline Backup & SHA-256 Manifest
Before code modifications, all affected files were backed up to `Docs/WPF_BACKUP_CAMERA_SCAN_20261022/`:

| File Path | SHA-256 Baseline |
|---|---|
| `App\Vision\MainWindow.xaml` | `455ecb348aff5ccdc3e9057d3740f338317c89db9979f322afdacbaf354059bd` |
| `App\Vision\MainWindow.xaml.cs` | `2a0da475f222473c11ed0ba99805239ec2cf3c114c6bde19882002a2167db8dd` |
| `App\Vision\Vision\StationCameraController.cs` | `10c51d61c1a360db14841ad23a1fff38e8badc98b47c250c946bc5195c068640` |
| `App\Vision\Vision\CameraSlotModel.cs` | `8a387c5e59e2f2ccbf9ca27cfe04a6f2fb403a32c0bf43a0fea0c7c6547945a2` |
| `App\Vision\Vision\GvcpDiscovery.cs` | `6496f3b6a885b8e4ef15af4c02e492e920b4740ad672612867e92144e01b19d9` |
| `App\Tests\Program.cs` | `28f15b251c4325360567cce639c18d411f077608154b01044456b193106cb1b4` |
| `Dependencies\Devices\UnifiedCameraService.cs` | `86618e285a3a1b4bb603c79253ea2060adf01da8c1bd6a21a53e83d329f34ab5` |
| `Dependencies\Devices\HikCameraService.cs` | `a030c698d229e686aad06ce2509dea5339a85daae0240457949584b6368bae50` |
| `Dependencies\BaslerCamera\BaslerCameraService.cs` | `9431aaf926a1da70278670d56a0a8cd1c71c064cf40f7adb52fedc0220ec7d87` |

---

## 2. Root Cause Analysis of Previous Scan Failures
1. **Silent Exception Swallowing in `AddTransport`:**  
   In `StationCameraController.cs`, a blanket `try { ... } catch { }` was swallowing all exceptions from vendor SDK enumeration. If an SDK threw an exception or was missing a native dependency, the scan silently completed with zero devices, falsely displaying `NO CAMERA` rather than reporting an SDK issue.
2. **Missing GVCP Discovery in Station Scans:**  
   Previously, non-invasive UDP GVCP discovery (`GvcpDiscovery.ScanAsync`) was only called in a legacy hidden utility panel (`ScanCameras_Click`) and was completely omitted from `StationCameraController.Scan`. Because `MyCamera.MV_CC_EnumDevices_NET` broadcasts on the Windows primary routing interface rather than multi-homed camera subnets (such as Ethernet 2 at `169.254.113.10`), the Hikrobot camera at `169.254.113.1` was never discovered by the station scan.
3. **Ambiguity of `AUTO` Mode:**  
   `AUTO` mode attempted to merge USB and IP devices into a single list, obscuring whether the user intended to scan the USB bus or the GigE network. Operators could not tell if the scan failed due to a missing USB camera or an unreachable IP camera.

---

## 3. Engineering Fixes Implemented

### A. Two Modes and Only Two Modes (`Usb` and `Ip`)
* **`App/Vision/Vision/CameraSlotModel.cs`:**
  - `CameraTransportMode` enum reduced to:
    ```csharp
    public enum CameraTransportMode { Usb, Ip }
    ```
  - `CameraSlotDefaults`:
    - `DefaultMode(1)` returns `CameraTransportMode.Ip`.
    - `DefaultMode(2)` returns `CameraTransportMode.Usb`.
    - `MigrateMode(saved, slot)`: parses `"Usb"` and `"Ip"` case-insensitively; migrates legacy `"Auto"`, null, or invalid strings to station default.
  - Incompatible selection clearing: when operator changes `Mode`, `CameraSlotModel.Mode` setter clears incompatible `Selected` devices and increments `ScanGeneration` to invalidate in-flight scans.
* **`App/Vision/MainWindow.xaml` & `MainWindow.xaml.cs`:**
  - `S1ModeBox` and `S2ModeBox` contain exclusively `<ComboBoxItem Content="IP"/>` and `<ComboBoxItem Content="USB"/>`.
  - Mode index mapping: `0 => Ip`, `1 => Usb`.

### B. GVCP Protocol Parser & Direct IP Integration
* **`App/Vision/Vision/GvcpDiscovery.cs`:**
  - Implemented full GigE Vision Discovery ACK (`0x0003`) parser:
    - MAC address: offset 10..15 (payload 8..13)
    - IP address: payload 36..39 (falls back to remote endpoint address if 0.0.0.0)
    - Manufacturer: payload 72..103 (ASCII, null-terminated)
    - Model: payload 104..135 (ASCII, null-terminated)
    - Firmware version: payload 136..167 (ASCII, null-terminated)
    - Serial number: payload 216..231 (ASCII, null-terminated)
  - `GvcpDeviceInfo.ToCameraDevice()`: constructs a `CameraDevice` with `Vendor = "Hikrobot"`, `Transport = "GigE"`, and `BackendIndex = -2` for native direct IP streaming.
  - Guarded against false positives: packets shorter than 72 bytes, header-only packets, or zero-identity packets return `null` and do not produce phantom devices.
* **`StationCameraController.Scan`:**
  - In `Ip` mode:
    1. Queries Hikrobot GigE SDK enumeration.
    2. Queries Basler GigE SDK enumeration.
    3. Runs `GvcpDiscovery.DiscoverAsync` preferring Ethernet 2 (`169.254.113.10`).
    4. Merges and deduplicates devices by `Key` (`VENDOR|SERIAL`).
    5. Surfaces any SDK or GVCP exceptions in `CameraScanIssue` rather than swallowing.

### C. Error Surfacing & Industrial Logging
* **`CameraScanIssue` & `CameraScanResult`:**
  - Records `Vendor`, `Transport`, `ErrorType`, `Message`, and `Details`.
  - Exposes standard labels: `HIK SDK ERROR`, `BASLER SDK ERROR`, `GVCP ERROR`.
* **Logging:**
  - Logs scan issues with UTC timestamp to `%LOCALAPPDATA%\PlateVision50_V2\logs\camera_scan.log` (limited PII, zero image data).
* **UI Status & Details:**
  - When errors occur and 0 devices are found, `SlotStateText` shows `HIK SDK ERROR` / `BASLER SDK ERROR`.
  - An expandable `DETAILS` panel in the toolbar allows operators to inspect full exception messages.

---

## 4. Hardware Verification on AML-IPC Workstation

### A. IP Camera Scan (169.254.113.1)
* **Local Interface:** Ethernet 2 (`169.254.113.10`, UP 1 Gbps).
* **GVCP Discovery Result:**
  - Responder IP: `169.254.113.1:3956`
  - MAC Address: `34:BD:20:6E:8D:AB`
  - Manufacturer: `Hikrobot`
  - Model Name: `MV-CS016-10GC`
  - Serial Number: `DA7403922`
  - Firmware Version: `V4.0.44 250410 1500180`
* **UI Behavior:**
  - Clicking `SCAN` in IP mode populates `MV-CS016-10GC · DA7403922 · Hikrobot · GigE` into the device dropdown and sets the IP box to `169.254.113.1`.
  - `CONNECT` button is enabled.

### B. USB Camera Scan
* **Hardware State:** No physical USB3 Vision camera connected to AML-IPC.
* **UI Behavior:**
  - Clicking `SCAN` in USB mode sets status to `NO USB CAMERA`.
  - Empty viewport text displays: `NO USB CAMERA · check the USB3 cable/port; Windows and pylon Viewer report no connected device`.
  - `CONNECT` button remains safely disabled.

---

## 5. Automated Test Suite Execution

Run via: `dotnet run --project App/Tests/Tests.csproj`
* **Total Failures:** `0` (`TOTAL_TEST_FAILURES=0`)
* **Checks Added:** 47 new contract, GVCP parsing, mode migration, and error surfacing checks.
* **Checks Removed:** 11 obsolete `Auto` mode checks.
* **Total Passed Checks:** 220 checks passed.

Key test areas validated:
1. `CameraTransportMode` contains exclusively `Usb` and `Ip`.
2. `CameraSlotDefaults.DefaultMode` returns `Ip` for Station 1, `Usb` for Station 2.
3. `CameraSlotDefaults.MigrateMode` cleanly migrates legacy `"Auto"`, null, or invalid strings.
4. Mode change immediately clears incompatible `Selected` devices.
5. GVCP parser correctly decodes the captured hardware packet: `Hikrobot`, `MV-CS016-10GC`, `DA7403922`, `34:BD:20:6E:8D:AB`.
6. Malformed, truncated, or zero-identity UDP packets do not produce fake devices.
7. `CameraScanResult` retains devices from partial SDK enumeration while reporting `HasIssues = true`.
8. Direct IP connects are restricted to Hikrobot/Hikvision; Basler direct IP is rejected with `SCAN REQUIRED`.
9. Exclusive dual-station camera ownership, disconnect isolation, and fail-closed interlock safety remain 100% intact.

---

## 6. Visual Evidence & Screenshots

All screenshots are stored under `Docs/ORCA_CAMERA_SCAN_IP_USB_ONLY_SCREENSHOTS/`:

| Filename | Resolution | Description |
|---|---|---|
| `01_initial_overview.png` | 1579 × 932 | App launch: Station 01 default IP, Station 02 default USB, dual 50/50 viewports. |
| `02_station01_scanned_gige.png` | 1579 × 932 | Station 01 IP mode post-scan: Hikrobot camera discovered, IP populated. |
| `03_station01_ip_scan_identified.png` | 1579 × 932 | Station 01 detailed tab: clean formatted device in dropdown, CONNECT enabled. |
| `04_station02_usb_scan_no_camera.png` | 1579 × 932 | Station 02 USB mode: truthful `NO USB CAMERA`, IP row collapsed. |
| `05_station02_ip_scan_identified.png` | 1579 × 932 | Station 02 switched to IP mode: IP row appears, `SCAN` discovers `MV-CS016-10GC`. |
| `06_line_overview_dual_cameras.png` | 1579 × 932 | Line Overview: dual viewports, compact commands strip, 8 interlock dots. |
| `07_1366x768_overview.png` | 1345 × 760 | Line Overview at 1366×768 resolution: zero clipped controls or text overflow. |
| `08_1366x768_station01_ip.png` | 1345 × 760 | Station 01 at 1366×768 resolution: compact toolbar, IP row visible. |
| `09_1366x768_station02_usb.png` | 1345 × 760 | Station 02 at 1366×768 resolution: USB mode, IP row hidden, `SCARA not commissioned`. |

---

## 7. Safety Declaration
* **TwinCAT / PLC / ADS:** Zero ADS writes or PLC downloads executed. ADS connection remains in read-only monitoring mode.
* **Robot & Conveyor Motion:** Zero motion commands issued; SCARA robot remains strictly `NOT COMMISSIONED`.
* **Network & Drivers:** Zero changes to Windows network adapters, IP settings, firewalls, or camera SDK driver installations.
* **Production Integrity:** Staged to `ReleaseCandidate\20261022_CameraScan_IpUsbOnly\`; the active `Release\` directory and `START_DATN.cmd` were untouched.
