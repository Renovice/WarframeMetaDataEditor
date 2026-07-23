using System.Runtime.InteropServices;

namespace MetadataPatchEditor.Core;

/// <summary>P/Invoke wrapper for Warframe's Oodle (oo2core_9.dll), used to decompress cache blocks.</summary>
public static unsafe class Oodle
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate nint DecompressFn(nint src, nint srcLen, nint dst, nint dstLen,
        int a, int b, int c, nint d, nint e, nint f, nint g, nint h, nint i, int j);

    static DecompressFn? _fn;

    /// <summary>Explicit oo2core path (highest priority).</summary>
    public static string? DllPathOverride;
    /// <summary>The Warframe install folder (Oodle usually ships here) — set by Cache before decoding.</summary>
    public static string? GameDir;

    static readonly string[] DllNames =
        { "oo2core_9.dll", "oo2core_9_win64.dll", "oo2core_9win64.dll", "oo2core_8_win64.dll", "oo2core_8.dll" };

    static IEnumerable<string> CandidatePaths()
    {
        if (!string.IsNullOrEmpty(DllPathOverride)) yield return DllPathOverride;
        foreach (var n in DllNames) yield return Path.Combine(AppContext.BaseDirectory, n);      // bundled next to the exe
        if (!string.IsNullOrEmpty(GameDir))
            foreach (var n in DllNames) yield return Path.Combine(GameDir, n);                    // the user's Warframe install
        // dev fallback
        yield return @"C:\Users\Bartek\OneDrive\Dokumenter\Warframe RE PROJECT RENOVICE\tools\warframe-cache-tools\lib\oo2core_9.dll";
    }

    public static void EnsureLoaded()
    {
        if (_fn != null) return;
        string? path = CandidatePaths().FirstOrDefault(File.Exists);
        if (path == null)
            throw new FileNotFoundException("Oodle DLL not found. Put oo2core_9.dll next to the app, or make sure your Warframe install contains it.");
        var lib = NativeLibrary.Load(path);
        var p = NativeLibrary.GetExport(lib, "OodleLZ_Decompress");
        _fn = Marshal.GetDelegateForFunctionPointer<DecompressFn>(p);
    }

    public static byte[] Decompress(byte[] data, int decSize)
    {
        EnsureLoaded();
        var dst = new byte[decSize];
        nint r;
        fixed (byte* s = data)
        fixed (byte* dp = dst)
            r = _fn!((nint)s, data.Length, (nint)dp, decSize, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3);
        if ((long)r != decSize) throw new InvalidDataException($"Oodle decompress returned {r}, expected {decSize}");
        return dst;
    }
}
