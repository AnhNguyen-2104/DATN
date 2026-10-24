# URGENT ORCA WPF FIX — CAMERA NOT DISCOVERED / REMOVE AUTO / IP+USB ONLY
Task: DATN-CAMERA-SCAN-CONNECT-FIX-20261022
User explicitly approved this new revision. **Use only E:\Orca\DATN**. Existing WPF solution: E:\Orca\DATN\App\PlateVision50.sln. Preserve all uncommitted WPF and unrelated TwinCAT/PLC edits.
User screenshot 2026-10-22 10:45: STATION01 CAM1 [AUTO] [EMPTY DEVICE] [SCAN] [CONNECT disabled] [NO CAMERA], while the camera is expected on AML.

## VERIFIED DIAGNOSTICS ON AML (not guesses)
- Windows Get-PnpDevice -PresentOnly reports NO physical USB3 Vision/Basler/Hik USB camera, only Remote Desktop Camera Bus. Basler pylon 8.1 USB3 SDK/driver are installed; USB hardware has not been detected or tested.
- Ethernet 2 is UP 1 Gbps. A device at **169.254.113.1** is ping-reachable in ~1 ms with neighbor MAC **34-BD-20-6E-8D-AB**. AML has 169.254.113.10 / 169.254.113.20 on Ethernet 2. **DO NOT assert the responder is a supported camera before GVCP/SDK confirms vendor/model/serial.** 192.168.1.100 did NOT respond. Do NOT change NIC addresses, SDK installation, firewall, or camera IP during this task.
- Current code in App/Vision/MainWindow.xaml.cs: ModeOf(ComboBox) indexes 0 AUTO,1 USB,2 IP; S1/S2 mode controls have AUTO. ScanSlotAsync uses controller.Scan(mode), which returns empty if nothing found, with only an optional MANUAL Hik fake entry when IP text entered and valid.
- Critical failure: App/Vision/Vision/StationCameraController.cs AddTransport(...) **silently catches and swallows ALL SDK enumeration exceptions** (lines around 55-79). A missing vendor runtime or discovery error then misleadingly displays NO CAMERA rather than SDK ERROR.
- Existing camera IP discovery uses GvcpDiscovery.ScanAsync() only in OLD ScanCameras_Click for hidden camera workspace; NEW station ScanSlotAsync does NOT use GVCP discovery. This is a concrete reason why Station 01 may show no device even while 169.254.113.1 is pingable.
- Station01/Station02 controllers/independent frames already implemented, report Docs/ORCA_DUAL_STATION_CAMERA_UI_RESULT_20261022.md shows prior offline build/test success. Do not regress.

## USER-APPROVED UI — TWO MODES AND ONLY TWO MODES
For **both Station01 and Station02**, show toolbar (small industrial controls, no needless words):
  CAM 1 [IP v] [IP ADDRESS (when IP)] [DEVICE dropdown] [SCAN] [CONNECT] [DISCONNECT] [OFFLINE/LIVE/ERR]
  CAM 2 [USB v] [DEVICE dropdown] [SCAN] [CONNECT] [DISCONNECT] [OFFLINE/LIVE/ERR]
- Mode options only **IP** and **USB** — remove AUTO entirely, including persistence migration, code paths and default displayed state. Station01 initial default IP and Station02 initial default USB are convenient defaults ONLY; operator can change either to other mode at any time, both identical capabilities.
- Changing mode clears incompatible device selection, cancels in-flight scans, keeps per-mode saved settings where appropriate. Re-scan list contains ONLY devices matching selected mode. USB requires no IP, hide IP and NIC fields; IP mode presents camera IP input / local NIC selection if required.
- When clicking **SCAN IP**: enumerate BOTH Hikvision/MVS and Basler/pylon GigE on available camera NIC(s), plus **non-invasive GVCP read-only discovery** using the actual selected NIC (prefer Ethernet 2 / 169.254.113.10 for the 169.254.113.1 network). Populate DEVICE dropdown with actual discovered camera Vendor/Model/Serial/IP; auto-select the first discovered compatible device if uniquely selectable. If SDK scan yields none, retain typed-IP manual option with EXPLICIT vendor choice (Hikrobot direct IP may be supported; do NOT pretend Basler direct IP is supported by Hik's constructor). No fake device from a mere ping. Do not scan/control PLC/EtherCAT networks.
- When clicking **SCAN USB**: run vendor enumeration of supported USB3 Vision cameras (Basler pylon is installed; Hik USB only if SDK+USB driver tested). Populate dropdown with Model/Serial/Vendor. Empty => 'NO USB CAMERA' and actionable diagnosis: Windows not detecting a device / check cable USB3, pylon Viewer. Not a connection bug if no device physically attached.
- If scan encounters SDK load/transport exceptions: **DO NOT silently swallow**. Log vendor, transport, error HRESULT/type/message to Docs local logs with limited PII; display succinct 'BASLER SDK ERROR'/'HIK SDK ERROR' and let user expand DETAILS (show full diagnostic). Partial discovery should still show devices from working SDK.
- Scanning is image acquisition discovery only, NEVER issues PLC or motion commands. Error state should not be mislabeled NO CAMERA.

## CONNECT behavior
- Device list shows only compatible scanned device. CONNECT enabled when selected, fully eligible and not in use by opposite station.
- IP mode with typed valid IP: allow manual Hikrobot IP if vendor explicitly Hik; use appropriate local NIC, confirm GVCP SDK/model before calling success. For Basler GigE use Basler SDK discovered CameraInfo, not Hik direct IP. Unsupported/unknown manual IP => clear 'Select vendor / scan first'.
- USB mode: selected camera opened using Basler (or verified Hik) SDK and stream; no IP needed. Status must distinguish CONNECTED but NO FRAME vs LIVE fresh frame, with frame count/FPS accurate.
- Two independent sessions: disconnecting one must not stop tracking/source/preview of other or clear opposite slot UI. Offline camera never claims a frame. Camera02 image allowed while SCARA robot remains not commissioned.
- On USB device absence, do not invent connected result. On IP discovery found, verify Actual camera SDK protocol/frames. Open only when application can claim camera; if pylon Viewer holds it, show 'Camera in use' rather than silently fail.
- Do not hardcode the existing 169.254.113.1 response as a Basler/Hik camera until confirmed. Do not rewrite any NIC or camera config.

## Engineering execution proof (don't stop at plan)
1. Record git status, preserve all current source modifications, back up EACH WPF file before changing, with SHA256 manifest.
2. Read exact implementation: MainWindow.xaml(.cs), StationCameraController.cs, CameraSlotModel.cs, UnifiedCameraService, HikCameraService, BaslerCameraService, GvcpDiscovery. Fix root-causes rather than just labels.
3. Build original solution Debug and Release x64; run entire test suite. Extend tests: only IP/USB modes; legacy Auto migration; scan filter, successful and failing enumeration, no silent SDK errors; device selection & Connect; IP network responder without GVCP does not become confirmed camera; independence & stale frames.
4. Visually test BOTH stations at 1366x768 and 1649x928; confirm no AUTO visible and SCAN results in dropdown or an actionable error. Test offline with fakes; perform read-only physical IP SDK discovery on AML, and actual video grab ONLY IF identified camera and safe to open, with no changes to camera parameters.
5. Save screenshot, build/test logs, actual device list, exact files touched to Docs/ORCA_FIX_CAMERA_SCAN_IP_USB_ONLY_RESULT_20261022.md. Do not overwrite active Release or any TwinCAT/Mitsubishi files, no Git push unless user asks.
6. If hardware/driver cannot be reached, distinguish software completed vs physical connection unverified. Never report "connected" simply from successful ping.

## SAFETY / SCOPE
ONLY WPF camera software and read-only camera network/device discovery. No ADS writes/PLC/TwinCAT changes, no motor/robot operations, no network reconfiguration or SDK driver installation. Keep interlock and mm/s system unchanged.
