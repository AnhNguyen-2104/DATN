# CHECKPOINT: MVS NATIVE RUNTIME RESOLUTION FIX
Date: 2026-10-29
Component: Dependencies/Devices, App/Vision, App/Tests

## Problem Description
- User screenshot shows Station 02 camera connection failing with:
  `Unable to load DLL 'MvCameraControl.dll' or one of its dependencies: The specified module could not be found. (0x8007007E)`
- Verified that `MvCameraControl.dll` is installed on this workstation at:
  `C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64\MvCameraControl.dll`
- Root causes:
  1. No runtime loader or process initialization discovered the x64 MVS runtime directory or added it to the process `PATH`.
  2. The process `PATH` contained `Win32_i86` before `Win64_x64` (`C:\Program Files (x86)\Common Files\MVS\Runtime\Win32_i86;C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64;...`), which on 64-bit .NET causes Windows to attempt loading incompatible 32-bit binaries first or fails to locate the 64-bit module and dependencies.
  3. `Dependencies/Devices/MyCamera.cs` and `App/Vision/Vision/HikDirectIpCamera.cs` both use bare `[DllImport("MvCameraControl.dll")]` without ensuring the native x64 runtime directory is prepended to `PATH` before invoking native methods.

## Action Plan
1. Create restorable backup and checkpoint with SHA-256 manifest under `Docs/BACKUP_20261029_MVS_RUNTIME_RESOLUTION`.
2. Implement `Dependencies/Devices/MvsNativeLoader.cs`:
   - Discovers x64 MVS runtime directory under standard Common Files path (`C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64`, `CommonProgramFilesX86`, `CommonProgramFiles`) and environment overrides (`MVS_RUNTIME_PATH`, `MVCAM_COMMON_RUNENV`, `MVS_ROOT`).
   - Prepends the resolved x64 runtime directory to the process `PATH` before any Hik SDK call.
   - Filters out `Win32_i86` from `PATH` for 64-bit processes to eliminate bitness collisions.
   - Pyns the native DLL via `NativeLibrary.TryLoad` and registers `NativeLibrary.SetDllImportResolver` on the calling assembly.
   - Maintains fail-closed diagnostics via `LastError` if runtime is not found or fails to load.
3. Link `MvsNativeLoader.cs` in `App/Vision/PlateVision50.csproj` and `App/Tests/Tests.csproj`.
4. Update `Dependencies/Devices/HikCameraService.cs` to call `MvsNativeLoader.Ensure()` before SDK operations and surface fail-closed diagnostics.
5. Update `App/Vision/Vision/HikDirectIpCamera.cs` to call `MvsNativeLoader.Ensure()` before `Native.Initialize()` and preserve fail-closed diagnostics.
6. Update `Dependencies/Devices/UnifiedCameraService.cs` to invoke `MvsNativeLoader.Ensure()` during initialization.
7. Update `Dependencies/Devices/MyCamera.cs` static constructor to ensure `MvsNativeLoader.Ensure()` is invoked on any direct class access.
8. Add deterministic regression test coverage in `App/Tests/Program.cs` for:
   - Path discovery of standard x64 Common Files runtime.
   - PATH computation prepending x64 runtime and filtering 32-bit MVS runtime in 64-bit process.
   - Fail-closed error reporting when an invalid runtime directory is specified.
   - Idempotent loader behavior.

## Files Backed Up
- `App/Vision/PlateVision50.csproj`
- `App/Tests/Tests.csproj`
- `App/Tests/Program.cs`
- `Dependencies/Devices/HikCameraService.cs`
- `Dependencies/Devices/UnifiedCameraService.cs`
- `Dependencies/Devices/MyCamera.cs`
- `App/Vision/Vision/HikDirectIpCamera.cs`
