using System.IO;
using System.Reflection;

namespace NVKProject.DotNet.BaslerCamera;

/// <summary>
/// Basler.Pylon managed DLL requires native pylon runtime (PylonBase_v10.dll, PylonUsb_v10_TL.dll, ...).
/// </summary>
public static class PylonNativeLoader
{
    private static bool _initialized;

    public static string? RuntimeDirectory { get; private set; }
    public static string? ManagedDllPath { get; private set; }

    static PylonNativeLoader()
    {
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    public static bool Ensure()
    {
        if (_initialized) return ManagedDllPath != null || RuntimeDirectory != null;
        _initialized = true;

        var root = Environment.GetEnvironmentVariable("PYLON_ROOT") ?? DiscoverPylonRoot();
        if (string.IsNullOrWhiteSpace(root))
            return false;

        var runtime = Path.Combine(root, "Runtime", "x64");
        var managed = Path.Combine(root, "Development", "Assemblies", "Basler.Pylon", "x64", "Basler.Pylon.dll");
        if (!Directory.Exists(runtime))
            return false;

        RuntimeDirectory = runtime;
        if (File.Exists(managed))
            ManagedDllPath = managed;

        PrependPath(runtime);
        TryStageManagedDll(managed);

        return true;
    }

    private static void TryStageManagedDll(string managedSource)
    {
        if (!File.Exists(managedSource)) return;

        var target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Basler.Pylon.dll");
        try
        {
            var srcLen = new FileInfo(managedSource).Length;
            if (!File.Exists(target) || new FileInfo(target).Length != srcLen)
                File.Copy(managedSource, target, overwrite: true);
        }
        catch
        {
            // Best effort staging.
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        if (args.Name == null || !args.Name.StartsWith("Basler.Pylon,", StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var path in GetManagedDllCandidates())
        {
            if (!File.Exists(path)) continue;
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch
            {
                // Try next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetManagedDllCandidates()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        yield return Path.Combine(baseDir, "Basler.Pylon.dll");

        if (!string.IsNullOrWhiteSpace(ManagedDllPath))
            yield return ManagedDllPath;

        var root = Environment.GetEnvironmentVariable("PYLON_ROOT") ?? DiscoverPylonRoot();
        if (!string.IsNullOrWhiteSpace(root))
            yield return Path.Combine(root, "Development", "Assemblies", "Basler.Pylon", "x64", "Basler.Pylon.dll");
    }

    private static void PrependPath(string directory)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (path.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => string.Equals(p.TrimEnd('\\'), directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            return;

        Environment.SetEnvironmentVariable("PATH", directory + ";" + path);
    }

    private static string? DiscoverPylonRoot()
    {
        var baslerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Basler");
        if (!Directory.Exists(baslerDir)) return null;

        return Directory.GetDirectories(baslerDir, "pylon*")
            .OrderByDescending(static d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
