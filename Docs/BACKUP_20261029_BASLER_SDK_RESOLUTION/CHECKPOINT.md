# CHECKPOINT: BASLER SDK RESOLUTION FIX
Date: 2026-10-29
Component: Dependencies/BaslerCamera

## Problem Description
- `Dependencies/BaslerCamera/NVKProject.DotNet.BaslerCamera.csproj` defaulted `PylonRoot` to `C:\Program Files\Basler\pylon 8`.
- The locally installed Basler pylon SDK is located at `C:\Program Files\Basler\pylon`.
- Under the installed SDK:
  - Managed assembly is located at: `Development\Assemblies\Basler.Pylon\net8.0\x64\Basler.Pylon.dll`
  - Native runtime is located at: `Runtime\x64`
- Because `pylon 8` does not exist, `dotnet build` failed to find the SDK DLL and attempted fallback to nonexistent repo vendor binaries (`libs\Basler.Pylon.dll`).
- In addition, `PylonNativeLoader.cs` only searched for `Development\Assemblies\Basler.Pylon\x64\Basler.Pylon.dll` without checking `net8.0\x64\Basler.Pylon.dll`, and its discovery did not prioritize the standard `pylon` installation folder.

## Action Plan
1. Backup original `NVKProject.DotNet.BaslerCamera.csproj` and `PylonNativeLoader.cs` with SHA-256 manifest.
2. Update `NVKProject.DotNet.BaslerCamera.csproj`:
   - Support `$(PylonRoot)` local override property.
   - Fall back to `$(PYLON_ROOT)` environment variable if set.
   - Detect `C:\Program Files\Basler\pylon` if present.
   - Support `C:\Program Files\Basler\pylon 8` if present.
   - Maintain `C:\Program Files\Basler\pylon` as default.
   - Resolve `PylonManagedDll` to `net8.0\x64\Basler.Pylon.dll` then `x64\Basler.Pylon.dll`.
   - Preserve existing local override capability for `PylonManagedDll` and `PylonRuntimeDir`.
   - Preserve fallback reference and no vendor binaries committed in repo.
3. Update `Dependencies/BaslerCamera/PylonNativeLoader.cs`:
   - Support runtime discovery prioritizing `C:\Program Files\Basler\pylon` or `PYLON_ROOT` override.
   - Check managed assembly under `Development\Assemblies\Basler.Pylon\net8.0\x64\Basler.Pylon.dll` as well as legacy `x64\Basler.Pylon.dll`.
   - Ensure consistency between build-time and runtime loader resolution.

## Files Backed Up
- `Dependencies/BaslerCamera/NVKProject.DotNet.BaslerCamera.csproj`
- `Dependencies/BaslerCamera/PylonNativeLoader.cs`
