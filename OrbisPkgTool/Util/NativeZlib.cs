using System.Runtime.InteropServices;

namespace OrbisPkgTool.Util;

/// <summary>
/// Locates and loads the platform's native zlib.
///
/// Search order: ORBISPKG_ZLIB env var → (Windows) zlib1.dll next to the
/// executable, Git for Windows mingw64, the system PATH → (Linux) libz.so.1 /
/// libz.so → (macOS) libz.1.dylib / libz.dylib.
///
/// z_stream declares its counters as C <c>unsigned long</c>, which is 32-bit
/// on Windows and 64-bit on Linux/macOS; structs passed to zlib must use
/// <see cref="CULong"/> for those fields so one layout fits every platform.
/// </summary>
internal static class NativeZlib
{
    private static readonly Lazy<IntPtr> _handle = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Loaded library handle, or <see cref="IntPtr.Zero"/> when no zlib was found.</summary>
    public static IntPtr Handle => _handle.Value;

    public static bool IsAvailable => Handle != IntPtr.Zero;

    /// <summary>Resolves an export as a delegate, or null when zlib or the export is missing.</summary>
    public static T? GetExport<T>(string name) where T : Delegate
    {
        if (!IsAvailable || !NativeLibrary.TryGetExport(Handle, name, out IntPtr fn))
            return null;
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    private static IntPtr Load()
    {
        foreach (string candidate in Candidates())
        {
            try
            {
                if (NativeLibrary.TryLoad(candidate, out IntPtr lib))
                    return lib;
            }
            catch
            {
                // Bad path / wrong architecture: keep looking.
            }
        }
        return IntPtr.Zero;
    }

    private static IEnumerable<string> Candidates()
    {
        string? env = Environment.GetEnvironmentVariable("ORBISPKG_ZLIB");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            yield return env;

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(AppContext.BaseDirectory, "zlib1.dll");
            yield return @"C:\Program Files\Git\mingw64\bin\zlib1.dll";
            yield return "zlib1";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "libz.1.dylib";
            yield return "libz.dylib";
        }
        else
        {
            yield return "libz.so.1";
            yield return "libz.so";
        }
    }
}
