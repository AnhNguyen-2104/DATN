using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WpfYolo_DotNet.Devices;

/// <summary>
/// Discovers and loads the native Hikrobot MVS x64 runtime (MvCameraControl.dll)
/// from the standard Common Files path or environment overrides.
/// Prepends the x64 runtime directory to PATH before any Hik SDK call,
/// filters out any 32-bit runtime collision, and preserves fail-closed diagnostics.
/// </summary>
public static class MvsNativeLoader
{
    private static readonly object _sync = new();
    private static bool _initialized;
    private static bool _isAvailable;
    private static string? _runtimeDirectory;
    private static string _lastError = string.Empty;
    private static IntPtr _nativeHandle = IntPtr.Zero;

    public const string DefaultCommonFilesSubPath = @"MVS\Runtime\Win64_x64";
    public const string MvCameraControlDll = "MvCameraControl.dll";

    public static bool IsAvailable
    {
        get
        {
            Ensure();
            return _isAvailable;
        }
    }

    public static string? RuntimeDirectory => _runtimeDirectory;
    public static string LastError => _lastError;

    /// <summary>
    /// Discovers the standard x64 MVS runtime directory, prepends it to PATH,
    /// and pre-loads the native library if present.
    /// Returns true if the x64 runtime was found and successfully initialized; false otherwise.
    /// Fail-closed diagnostics are stored in LastError.
    /// </summary>
    public static bool Ensure()
    {
        lock (_sync)
        {
            if (_initialized) return _isAvailable;
            _initialized = true;

            var dir = DiscoverRuntime();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                _isAvailable = false;
                _runtimeDirectory = null;
                _lastError = $"MVS x64 native runtime not found ({MvCameraControlDll}) under standard Common Files path: {GetStandardCommonFilesPath()}";
                return false;
            }

            var dllPath = Path.Combine(dir, MvCameraControlDll);
            if (!File.Exists(dllPath))
            {
                _isAvailable = false;
                _runtimeDirectory = null;
                _lastError = $"Native library {MvCameraControlDll} not found in directory: {dir}";
                return false;
            }

            _runtimeDirectory = dir;
            PrependPath(dir);

            try
            {
                if (NativeLibrary.TryLoad(dllPath, out var handle))
                {
                    _nativeHandle = handle;
                    _isAvailable = true;
                    _lastError = string.Empty;
                }
                else
                {
                    _isAvailable = false;
                    _lastError = $"NativeLibrary.TryLoad failed for '{dllPath}' (error: {Marshal.GetLastWin32Error()}).";
                    return false;
                }
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                _lastError = $"Failed loading MVS runtime from '{dllPath}': {ex.Message}";
                return false;
            }

            TryRegisterResolver(typeof(MvsNativeLoader).Assembly);
            return true;
        }
    }

    /// <summary>
    /// Registers a DllImport resolver on the specified assembly to redirect
    /// references to MvCameraControl.dll directly to the loaded native handle.
    /// </summary>
    public static void TryRegisterResolver(Assembly assembly)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(assembly, (libraryName, asm, searchPath) =>
            {
                if (string.Equals(libraryName, MvCameraControlDll, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(libraryName, "MvCameraControl", StringComparison.OrdinalIgnoreCase))
                {
                    if (_nativeHandle != IntPtr.Zero) return _nativeHandle;
                    if (!string.IsNullOrWhiteSpace(_runtimeDirectory))
                    {
                        var dll = Path.Combine(_runtimeDirectory, MvCameraControlDll);
                        if (File.Exists(dll) && NativeLibrary.TryLoad(dll, out var h))
                            return h;
                    }
                }
                return IntPtr.Zero;
            });
        }
        catch
        {
            // SetDllImportResolver can only be registered once per assembly.
        }
    }

    /// <summary>
    /// Discovers candidate paths for MVS x64 runtime in priority order.
    /// </summary>
    public static string? DiscoverRuntime(string? explicitRoot = null)
    {
        foreach (var candidate in GetCandidatePaths(explicitRoot))
        {
            if (IsValidRuntimeDirectory(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Validates whether a directory contains the x64 MvCameraControl.dll.
    /// </summary>
    public static bool IsValidRuntimeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            return Directory.Exists(directory) &&
                   File.Exists(Path.Combine(directory, MvCameraControlDll));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the standard default Common Files path where Hikrobot MVS x64 runtime installs.
    /// </summary>
    public static string GetStandardCommonFilesPath()
    {
        var commonX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(commonX86))
            return Path.Combine(commonX86, DefaultCommonFilesSubPath);

        return @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64";
    }

    /// <summary>
    /// Returns all candidate paths for MVS x64 runtime discovery.
    /// </summary>
    public static IEnumerable<string> GetCandidatePaths(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            yield return explicitRoot;
            yield return Path.Combine(explicitRoot, "Win64_x64");
            yield return Path.Combine(explicitRoot, "Runtime", "Win64_x64");
            yield return Path.Combine(explicitRoot, "Development", "Bin", "win64");
        }

        // 1. Explicit environment variable overrides
        var envRuntime = Environment.GetEnvironmentVariable("MVS_RUNTIME_PATH");
        if (!string.IsNullOrWhiteSpace(envRuntime))
            yield return envRuntime;

        var envRunenv = Environment.GetEnvironmentVariable("MVCAM_COMMON_RUNENV");
        if (!string.IsNullOrWhiteSpace(envRunenv))
        {
            yield return Path.Combine(envRunenv, "Win64_x64");
            yield return envRunenv;
        }

        var envRoot = Environment.GetEnvironmentVariable("MVS_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            yield return Path.Combine(envRoot, "Runtime", "Win64_x64");
            yield return Path.Combine(envRoot, "Development", "Bin", "win64");
        }

        // 2. Standard Common Files (x86) -> MVS\Runtime\Win64_x64
        var commonX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(commonX86))
            yield return Path.Combine(commonX86, DefaultCommonFilesSubPath);

        // 3. Standard Common Files -> MVS\Runtime\Win64_x64
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        if (!string.IsNullOrWhiteSpace(common))
            yield return Path.Combine(common, DefaultCommonFilesSubPath);

        // 4. Standard hardcoded fallback paths
        yield return @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64";
        yield return @"C:\Program Files\Common Files\MVS\Runtime\Win64_x64";
        yield return @"C:\Program Files (x86)\MVS\Development\Bin\win64";
        yield return @"C:\Program Files\MVS\Development\Bin\win64";
    }

    /// <summary>
    /// Prepends the specified directory to the process PATH environment variable.
    /// If Win32_i86 exists in PATH and this is a 64-bit process, filters it out to prevent
    /// 32-bit DLL architecture mismatch errors (0x8007007E / 0x800700C1).
    /// </summary>
    public static void PrependPath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var newPath = ComputePrependedPath(currentPath, directory, Environment.Is64BitProcess);
        if (!string.Equals(currentPath, newPath, StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable("PATH", newPath);
        }
    }

    /// <summary>
    /// Pure deterministic helper to compute prepended PATH string for testing and runtime.
    /// </summary>
    public static string ComputePrependedPath(string? currentPath, string? directory, bool is64BitProcess = true)
    {
        if (string.IsNullOrWhiteSpace(directory)) return currentPath ?? string.Empty;

        var normalizedDir = directory.Trim().TrimEnd('\\', '/');
        var segments = (currentPath ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var remaining = segments.Where(s =>
        {
            var trimmed = s.TrimEnd('\\', '/');
            if (string.Equals(trimmed, normalizedDir, StringComparison.OrdinalIgnoreCase))
                return false;

            if (is64BitProcess)
            {
                if (trimmed.EndsWith(@"MVS\Runtime\Win32_i86", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(@"MVS/Runtime/Win32_i86", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(trimmed, @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win32_i86", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        });

        var remainingJoined = string.Join(";", remaining);
        return string.IsNullOrEmpty(remainingJoined) ? normalizedDir : normalizedDir + ";" + remainingJoined;
    }

    /// <summary>
    /// For testing purposes only: resets the loader state.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (_sync)
        {
            _initialized = false;
            _isAvailable = false;
            _runtimeDirectory = null;
            _lastError = string.Empty;
        }
    }
}
