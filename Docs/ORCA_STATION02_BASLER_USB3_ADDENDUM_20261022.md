# Addendum: STATION 02 Camera 02 must support Basler USB3 Vision
User follow-up: 2026-10-22. Task workspace E:\Orca\DATN ONLY. Read alongside Docs\ORCA_STATION_INTERLOCK_WPF_TASK_20261021.md.

## Verified on AML (read-only)
- Intel USB 3.0 xHCI and USB 3.0 Root Hub are present.
- Basler pylon 8.1.0.16743, Basler USB3 Vision driver plnu3v, pylon .NET 8 runtime and native PylonUsb_v10_TL.dll are installed.
- No physical Basler USB camera is detected by Windows Get-PnpDevice -PresentOnly now. Actual camera cannot be tested until plugged into AML.
- Dependencies\BaslerCamera\BaslerCameraService.cs already supports CameraTransportType.Usb and enumerates Basler.Pylon.DeviceType.Usb and BaslerGenTlU3vDeviceClass. UnifiedCameraService exists.
- MainWindow.xaml.cs ScanCameras_Click discovers USB and GigE devices. But Station 02 MainWindow.xaml still hard-disables S2CamIpBox, S2CamConnectButton and S2CamDisconnectButton, with no connected handlers or Station02 frame display. Station01 IP connection specifically uses HikDirectIpCamera. Do not pass Basler USB serial to an IP connect method.
- Existing _sessions list mostly feeds one Preview rendering path; give Station02 its own session and frame routing.

## UPDATED OPERATOR REQUIREMENT, supersedes Station02 IP-only spec
Station01 stays Hikrobot GigE + IP CONNECT, separate and unchanged.
Station02 will use BASLER USB3 Vision:
1. Physically connect Basler camera to AML USB3 port.
2. In Station 02 show compact SCAN USB button (search Basler USB only), list device MODEL, SERIAL NUMBER and transport USB.
3. Operator selects exact serial, then CONNECT; show live Camera02 frames both in Station02 large panel and LINE OVERVIEW Camera02. Provide DISCONNECT affecting only Camera02.
4. A USB camera has NO IPv4; never require an IP field for this USB workflow. Hide or disable S2CamIpBox for transport USB; optional GigE support may remain as separate selector.
5. Keep SCAN USB enabled even when 0 devices found; show NO BASLER USB CAMERA DETECTED; do not make fake LIVE claims; connect enabled only for selected discovered device; disconnected/fresh frame states must be truthful.
6. Maintain independent Camera01 and Camera02 session/frame routing; DO NOT dispose Hikrobot Station01 when connecting Basler Station02. Preserve existing detection/trigger/queue and event pipeline for Station01.
7. Camera02 LIVE image is allowed for image acquisition/testing without SCARA robot commissioning. The robot remains NOT COMMISSIONED; never issue servo, PLC, conveyor or SCARA motion commands from camera actions. Station02 uncommissioned must not block Gantry-only AUTO unless added to recipe in a future explicitly approved commissioning.
8. The old VISION WORKSPACE hidden from main navigation can remain an internal backend for discovery and SDK, but the Station02 operator must not need to visit it.

## Implementation and validation requirements
- Work only in E:\Orca\DATN\App\PlateVision50.sln and related WPF/USB camera source under E:\Orca\DATN. The folder C:\Users\aml\orca\DATN is NOT this task's workspace.
- Safeguard all currently uncommitted WPF/PLC files. Before editing, backup each relevant WPF file. Do not touch TwinCAT configuration/PLC ST.
- Reuse existing pylon installation and Basler .NET 8 SDK, do NOT change vendor driver installation without separate permission.
- Verify offline build, test USB device absent inventory, independent disconnect, frame age/state, Station02 and LINE OVERVIEW Cam02 display, responsive layout. If camera not physically connected then actual hardware frame test must be marked NOT TESTED.
- Report exact changed WPF files, build/test results and commissioning blockers in Docs result. No Git push or deployment unless explicitly requested. Small industrial buttons, dense layout, no fake PLC feedback.

This document overrides any earlier request to enter Camera02 IP for USB, without authorizing any physical motor/PLC actuation.