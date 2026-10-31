# CHECKPOINT: DUAL IP CAMERA MAPPING (BASLER S1 IP, HIKROBOT S2 IP)
Date: 2026-10-28
Objective:
- Station 1 = Basler IP/GigE default, Station 2 = Hikrobot IP/GigE default.
- DefaultMode(1) = Ip, DefaultMode(2) = Ip.
- NormalizeSlot(1) preserves IP mode and valid Basler identity/IP; does not force USB.
- SaveSlot removes slot 1 USB force while keeping both slots IP.
- Basler CameraInfoKey.DeviceIpAddress extraction implemented in BaslerCameraService / StationCameraController.
- IP and Key deduplication enforced across slots.
- S1ModeBox SelectedIndex="0" (IP default).
