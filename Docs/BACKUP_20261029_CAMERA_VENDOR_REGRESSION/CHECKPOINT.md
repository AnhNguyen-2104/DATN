# Checkpoint: Camera Vendor Selection & Dispatch Regression Coverage

**Date**: 2026-10-29  
**Session / Task**: camera-sdk-implementation / regression coverage for camera vendor selection/dispatch  
**Backup Path**: `Docs/BACKUP_20261029_CAMERA_VENDOR_REGRESSION/`

## Backed Up Files
- `App/Tests/Program.cs` (SHA-256: `16dd22dc634940b78a2b9937a6e91ab399d9b29a5ba018f0ba425f72b0382aef`)

## Target Changes
Improving regression coverage in `App/Tests/Program.cs` for:
1. Basler and Hikrobot/Hikvision vendor recognition (`CameraTransportPolicy.IsBaslerVendor`, `IsHikVendor`, `IsSupported`, `UnsupportedReason`, `CameraSlotDefaults.MigrateVendor`, `IsKeyCompatibleWithVendor`, station auto-select).
2. Transport matching (`CameraTransportPolicy.MatchesMode`, `CanDispatch`, USB/IP separation, slot filtering, mode mismatch handling).
3. Manual-IP restriction (Hikrobot/Hikvision allowed for direct IP connect; Basler requires a scan and is blocked with `SCAN REQUIRED`; unknown vendors blocked).
4. SDK scan issue labeling and surfacing (`CameraScanIssue.Label`, `Summary`, `CameraScanResult.HasIssues`, `CameraSlotModel.ScanIssues` / `StatusText` integration).

## Restoration Instructions
To restore the pre-edit test suite:
```powershell
cp "Docs/BACKUP_20261029_CAMERA_VENDOR_REGRESSION/Program.cs" "App/Tests/Program.cs"
```
