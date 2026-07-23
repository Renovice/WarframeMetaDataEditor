using System.Text;
using System.Text.RegularExpressions;

namespace MetadataPatchEditor.Core;

/// <summary>
/// Reads Warframe's Cache.Windows and extracts /Packages.bin (Oodle-decompressed).
/// Port of cache_toc.py / cache_extract.py. Point it at the Cache.Windows folder.
/// </summary>
public static class Cache
{
    const uint TOC_MAGIC = 0x1867C64E;

    /// <summary>Extract + Oodle-decompress /Packages.bin from a Cache.Windows folder.</summary>
    public static byte[] ExtractPackagesBin(string cacheWindowsDir)
    {
        Oodle.GameDir = Directory.GetParent(cacheWindowsDir)?.FullName;   // Oodle usually ships in the game root
        return Extract(Path.Combine(cacheWindowsDir, "H.Misc.toc"), Path.Combine(cacheWindowsDir, "H.Misc.cache"), "/Packages.bin");
    }

    /// <summary>Extract + Oodle-decompress the per-language /Languages.bin (default English).</summary>
    public static byte[] ExtractLanguagesBin(string cacheWindowsDir, string lang = "en")
    {
        Oodle.GameDir = Directory.GetParent(cacheWindowsDir)?.FullName;
        return Extract(Path.Combine(cacheWindowsDir, $"H.Misc_{lang}.toc"), Path.Combine(cacheWindowsDir, $"H.Misc_{lang}.cache"), "/Languages.bin");
    }

    static byte[] Extract(string toc, string cache, string fullPath)
    {
        if (!File.Exists(toc) || !File.Exists(cache))
            throw new FileNotFoundException($"{Path.GetFileName(toc)}/.cache not found");
        var (off, comp, dec) = FindEntry(toc, fullPath);
        byte[] blob = new byte[comp];
        using (var fs = File.OpenRead(cache)) { fs.Seek(off, SeekOrigin.Begin); fs.ReadExactly(blob); }
        return DecompressEntry(blob, comp, dec);
    }

    /// <summary>A folder is a valid Cache.Windows if it holds the H.Misc.toc index.</summary>
    public static bool IsCacheWindows(string dir) =>
        !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "H.Misc.toc"));

    /// <summary>Normalise a user-picked folder to a Cache.Windows path (accepts the game root too).</summary>
    public static string? NormalizeToCacheWindows(string folder)
    {
        if (IsCacheWindows(folder)) return folder;
        var sub = Path.Combine(folder, "Cache.Windows");
        return IsCacheWindows(sub) ? sub : null;
    }

    /// <summary>Auto-locate Cache.Windows: walk up from the app, then probe known Warframe installs.</summary>
    public static string? FindCacheWindows(string start)
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "Cache.Windows");
            if (IsCacheWindows(c)) return c;
        }
        foreach (var wf in WarframeInstallDirs())
        {
            var c = Path.Combine(wf, "Cache.Windows");
            if (IsCacheWindows(c)) return c;
        }
        return null;
    }

    /// <summary>Best folder to open the picker at when auto-detect fails (likely install, else Steam/Program Files).</summary>
    public static string BestGuessStart()
    {
        foreach (var wf in WarframeInstallDirs()) if (Directory.Exists(wf)) return wf;
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var steamCommon = Path.Combine(pfx86, "Steam", "steamapps", "common");
        return Directory.Exists(steamCommon) ? steamCommon : pfx86;
    }

    /// <summary>Candidate Warframe install roots: Steam (+library folders), Epic, standalone, OpenWF-in-Documents.</summary>
    public static IEnumerable<string> WarframeInstallDirs()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToList();

        // Steam roots to probe
        var steamRoots = new List<string>();
        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            if (!string.IsNullOrEmpty(pf)) steamRoots.Add(Path.Combine(pf, "Steam"));
        foreach (var d in drives) { steamRoots.Add(Path.Combine(d, "Steam")); steamRoots.Add(Path.Combine(d, "SteamLibrary")); }

        // Steam library folders (default root + any in libraryfolders.vdf)
        var libs = new List<string>();
        foreach (var s in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libs.Add(s);
            var vdf = Path.Combine(s, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                try { foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"")) libs.Add(m.Groups[1].Value.Replace(@"\\", @"\")); }
                catch { }
        }
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            yield return Path.Combine(lib, "steamapps", "common", "Warframe");

        // OpenWF / standalone often live under Documents\Warframe (covers OneDrive-redirected Documents too)
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(docs)) yield return Path.Combine(docs, "Warframe");

        // Epic + generic per-drive guesses
        foreach (var d in drives)
        {
            yield return Path.Combine(d, "Program Files", "Epic Games", "Warframe");
            yield return Path.Combine(d, "Program Files (x86)", "Warframe");
            yield return Path.Combine(d, "Warframe");
        }
    }

    // Resolve full paths via the directory-only tree (parentDirIndex indexes m_dirs; root=0),
    // then match the requested full path. Mirrors cache_extract.py parse_toc.
    static (long off, int comp, int dec) FindEntry(string tocPath, string wantFullPath)
    {
        byte[] t = File.ReadAllBytes(tocPath);
        if (BitConverter.ToUInt32(t, 0) != TOC_MAGIC) throw new InvalidDataException("bad .toc magic");
        int n = (t.Length - 8) / 96;

        var dirName = new List<string> { "" };
        var dirParent = new List<int> { 0 };
        var files = new List<(long off, int comp, int dec, int par, string name)>();

        string NameAt(int b)
        {
            int nul = Array.IndexOf(t, (byte)0, b + 32, 64);
            return Encoding.Latin1.GetString(t, b + 32, (nul < 0 ? b + 96 : nul) - (b + 32));
        }

        for (int i = 0; i < n; i++)
        {
            int b = 8 + i * 96;
            long off = BitConverter.ToInt64(t, b);
            int comp = BitConverter.ToInt32(t, b + 16);
            int dec = BitConverter.ToInt32(t, b + 20);
            int par = BitConverter.ToInt32(t, b + 28);
            string nm = NameAt(b);
            if (off == -1) { dirName.Add(nm); dirParent.Add(par); }
            else files.Add((off, comp, dec, par, nm));
        }

        var dpath = new string?[dirName.Count]; dpath[0] = "";
        string BuildDir(int k)
        {
            if (dpath[k] != null) return dpath[k]!;
            int p = dirParent[k];
            dpath[k] = (p < 0 || p >= dirName.Count || p == k) ? "/" + dirName[k] : BuildDir(p) + "/" + dirName[k];
            return dpath[k]!;
        }
        for (int k = 0; k < dirName.Count; k++) BuildDir(k);

        // Duplicate entries can resolve to the same path (ts==0 dupes); last valid wins (mirrors cache_extract.py dict).
        (long off, int comp, int dec)? found = null;
        foreach (var f in files)
            if (f.par >= 0 && f.par < dpath.Length && dpath[f.par] + "/" + f.name == wantFullPath)
                found = (f.off, f.comp, f.dec);
        if (found is { } v) return v;

        throw new FileNotFoundException($"{wantFullPath} not found in {tocPath}");
    }

    static byte[] DecompressEntry(byte[] blob, int comp, int dec)
    {
        if (comp == dec) return blob;                                             // stored uncompressed
        if (blob.Length >= 8 && blob[0] == 0x80 && (blob[7] & 0x0F) == 0x01)
            return DecompressOodleChunk(blob, 0, dec);                            // BE 0x80-block loop
        if (blob.Length > 0 && blob[0] == 0x8C) return Oodle.Decompress(blob, dec);
        return blob;
    }

    static byte[] DecompressOodleChunk(byte[] packed, int offset, int decSize)
    {
        var outp = new byte[decSize]; int outPos = 0;
        while (outPos < decSize)
        {
            if (offset + 8 > packed.Length || packed[offset] != 0x80 || (packed[offset + 7] & 0x0F) != 0x01)
                throw new InvalidDataException("bad Oodle block header");
            uint num1 = BE32(packed, offset), num2 = BE32(packed, offset + 4); offset += 8;
            int csize = (int)((num1 >> 2) & 0xFFFFFF), bdec = (int)((num2 >> 5) & 0xFFFFFF);
            if (packed.Length < offset + csize || packed[offset] != 0x8C)
                throw new InvalidDataException("bad Oodle block payload");
            var block = packed[offset..(offset + csize)]; offset += csize;
            var d = Oodle.Decompress(block, bdec);
            Buffer.BlockCopy(d, 0, outp, outPos, bdec); outPos += bdec;
        }
        return outp;
    }

    static uint BE32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
}
