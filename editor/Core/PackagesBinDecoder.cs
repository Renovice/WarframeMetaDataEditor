using System.Text;
using ZstdSharp.Unsafe;

namespace MetadataPatchEditor.Core;

/// <summary>One decoded type: its full path, its base-type path, and its own property text.</summary>
public record DecodedType(string Path, string Parent, string? OwnText);

/// <summary>
/// Offline decoder for Warframe's Packages.bin (the Oodle-decompressed blob). Port of
/// pkg_decode_v46.py / LotusLib PackagesBin.cpp. Per-type property text is a MAGICLESS ZSTD frame
/// compressed against a dictionary embedded inline in comZ.
///
/// The three payload buffers (comFlags / comSize / comZ) and the entity table are LOCATED
/// STRUCTURALLY, not read at fixed offsets — so the decoder survives content updates (which shift
/// everything after the growing comFlags/comSize buffers) and version bumps that keep the same
/// buffer shape. If the format genuinely changes, location fails loudly rather than returning garbage.
/// </summary>
public static unsafe class PackagesBinDecoder
{
    static uint U32(byte[] d, int o) => BitConverter.ToUInt32(d, o);

    static (ulong val, int next) Uleb(byte[] b, int o)
    {
        ulong r = 0; int sh = 0;
        while (true)
        {
            byte x = b[o++];
            r |= (ulong)(x & 0x7f) << sh;
            if ((x & 0x80) == 0) break;
            sh += 7;
        }
        return (r, o);
    }

    /// <summary>LSB-first bit reader over the comFlags buffer.</summary>
    sealed class BitReader
    {
        readonly byte[] _b; int _pos; int _bit; byte _cur;
        public BitReader(byte[] b) { _b = b; _cur = b.Length > 0 ? b[0] : (byte)0; }
        public int Read()
        {
            int v = (_cur >> _bit) & 1;
            if (++_bit > 7) { _bit -= 8; _pos++; _cur = _pos < _b.Length ? _b[_pos] : (byte)0; }
            return v;
        }
    }

    public sealed class DecodeResult
    {
        public Dictionary<string, DecodedType> Types = new(StringComparer.Ordinal);
        public int EntityCount, TextCount, Compressed, Raw, NoText;
        public bool Aligned;
    }

    // Structurally-located layout of the four regions we need.
    readonly record struct Layout(int CfOff, int CsOff, int CzOff, int CzLen, int DictSize, int EntityCount, int TableOff);

    /// <summary>From `from`, find the first plausible dir-block start: [u32 dirLen][ascii path starting '/'].</summary>
    static int FindDirBlock(byte[] d, int from, int maxScan)
    {
        for (int g = 0; g <= maxScan; g++)
        {
            int p = from + g;
            if (p + 8 > d.Length) break;
            int dl = (int)U32(d, p);
            if (dl < 1 || dl > 1024 || p + 4 + dl > d.Length) continue;
            if (d[p + 4] != (byte)'/') continue;
            bool ascii = true;
            for (int k = 0; k < dl; k++) { byte b = d[p + 4 + k]; if (b < 0x20 || b > 0x7e) { ascii = false; break; } }
            if (ascii) return p;
        }
        return -1;
    }

    /// <summary>comSize sits just before comZ: [u32 len][u32 dictSize][ULEBs]. The ULEBs must fill the
    /// buffer exactly AND sum to framesLen (czLen − dictSize). Returns (off, dictSize, textCount) or null.</summary>
    static (int off, int dictSize, int textCount)? FindComSize(byte[] d, int czOff, int czLen)
    {
        for (int off = 0; off + 8 <= czOff; off++)
        {
            long len = U32(d, off);
            long bufEnd = (long)off + 4 + len;
            if (bufEnd > czOff || czOff - bufEnd > 8192 || len < 8) continue;   // must end shortly before comZ
            int dictSize = (int)U32(d, off + 4);
            if (dictSize <= 0 || dictSize >= czLen) continue;
            long frames = (long)czLen - dictSize, sum = 0;
            int q = off + 8, end = off + 4 + (int)len, cnt = 0; bool ok = true;
            while (q < end)
            {
                var (v, nq) = Uleb(d, q);
                if (nq > end) { ok = false; break; }
                sum += (long)v; q = nq; cnt++;
                if (sum > frames) { ok = false; break; }
            }
            if (ok && q == end && sum == frames) return (off, dictSize, cnt);
        }
        return null;
    }

    /// <summary>comFlags sits just before comSize. It holds entityCount hasText bits + textCount
    /// isCompressed bits, so its length is at least ceil((entityCount+textCount)/8) — plus up to a few
    /// bytes of trailing padding. Match the length-prefix whose buffer ends in a small gap before
    /// comSize with a length in that window. Returns off or −1.</summary>
    static int FindComFlags(byte[] d, int csOff, int entityCount, int textCount)
    {
        int min = (entityCount + textCount + 7) / 8;          // minimum bytes to hold every bit
        for (int len = min; len <= min + 8; len++)
            for (int gap = 0; gap <= 512; gap++)
            {
                int off = csOff - gap - 4 - len;
                if (off < 0) break;
                if (U32(d, off) == (uint)len) return off;      // length-prefix whose buffer ends `gap` before comSize
            }
        return -1;
    }

    static Layout Locate(byte[] d)
    {
        int n = d.Length;
        if (n < 8192 || U32(d, 16) != 20 || U32(d, 24) != 1)
            throw new InvalidDataException("Not a Packages.bin (header field mismatch).");

        // Anchor on comZ (embedded ZSTD dictionary magic 37 A4 30 EC). Try each candidate; the first
        // one that also yields a valid comSize + comFlags is the real one (a stray magic in the
        // compressed data fails the downstream ULEB-sum / length checks and is skipped).
        for (int i = 2048; i + 8 < n; i++)
        {
            if (d[i] != 0x37 || d[i + 1] != 0xA4 || d[i + 2] != 0x30 || d[i + 3] != 0xEC) continue;
            int czOff = i - 4;
            long czLen = U32(d, czOff);
            long entityOff = (long)i + czLen;
            if (czLen < 4096 || entityOff + 8 > n) continue;
            long ec = U32(d, (int)entityOff);
            if (ec < 1 || ec > 10_000_000) continue;
            int tableOff = FindDirBlock(d, (int)entityOff + 4, 64);
            if (tableOff < 0) continue;

            var cs = FindComSize(d, czOff, (int)czLen);
            if (cs == null) continue;
            int cfOff = FindComFlags(d, cs.Value.off, (int)ec, cs.Value.textCount);
            if (cfOff < 0) continue;

            return new Layout(cfOff, cs.Value.off, czOff, (int)czLen, cs.Value.dictSize, (int)ec, tableOff);
        }
        throw new InvalidDataException("Could not locate the Packages.bin payload buffers — the format may have changed (re-derive per EDITOR_NOTES §7.2).");
    }

    /// <summary>Decode every entity from an Oodle-decompressed Packages.bin file (.dec).</summary>
    public static DecodeResult DecodeAll(string decPath) => DecodeBytes(File.ReadAllBytes(decPath));

    /// <summary>Decode every entity from the Oodle-decompressed Packages.bin bytes.</summary>
    public static DecodeResult DecodeBytes(byte[] d)
    {
        var L = Locate(d);

        int cfLen = (int)U32(d, L.CfOff);
        var comFlags = d[(L.CfOff + 4)..(L.CfOff + 4 + cfLen)];

        int csLen = (int)U32(d, L.CsOff);
        var comSize = d[(L.CsOff + 4)..(L.CsOff + 4 + csLen)];

        int czStart = L.CzOff + 4;
        var dict = d[czStart..(czStart + L.DictSize)];
        int framesBase = czStart + L.DictSize;      // absolute offset of first frame
        int framesLen = L.CzLen - L.DictSize;
        int entityCount = L.EntityCount;

        // ZSTD magicless decompressor + embedded full dictionary (mirrors LotusLib exactly).
        ZSTD_DCtx_s* dctx = Methods.ZSTD_createDCtx();
        Methods.ZSTD_DCtx_setParameter(dctx, ZSTD_dParameter.ZSTD_d_experimentalParam1, 1); // ZSTD_d_format = magicless
        ZSTD_DDict_s* ddict;
        fixed (byte* dp = dict) ddict = Methods.ZSTD_createDDict(dp, (nuint)dict.Length);

        var res = new DecodeResult { EntityCount = entityCount };
        try
        {
            var flags = new BitReader(comFlags);
            int sizePos = 4;                       // skip leading dictSize u32 in comSize
            int framePos = 0;
            int o = L.TableOff, seen = 0, n = d.Length;

            while (seen < entityCount && o < n)
            {
                int dirLen = (int)U32(d, o); o += 4;
                string dir = Encoding.Latin1.GetString(d, o, dirLen); o += dirLen;
                o += 1;                            // 0x01 dir-block marker
                int typeCount = (int)U32(d, o); o += 4;

                for (int t = 0; t < typeCount && seen < entityCount; t++)
                {
                    int nameLen = (int)U32(d, o); o += 4;
                    string name = Encoding.Latin1.GetString(d, o, nameLen); o += nameLen;
                    o += 3;                        // 3 flag bytes
                    int baseLen = (int)U32(d, o); o += 4;
                    string bas = Encoding.Latin1.GetString(d, o, baseLen); o += baseLen;

                    string full = dir + name;
                    string parent = (bas.Length > 0 && bas[0] == '/') ? bas
                                  : (bas.Length > 0 ? dir + bas : "");

                    string? ownText = null;
                    if (flags.Read() == 1)                          // hasText
                    {
                        res.TextCount++;
                        (ulong size, int nextSize) = Uleb(comSize, sizePos); sizePos = nextSize;
                        int frameAbs = framesBase + framePos;
                        framePos += (int)size;
                        if (flags.Read() == 1)                       // isCompressed
                        {
                            res.Compressed++;
                            (ulong declen, int fo) = Uleb(d, frameAbs);
                            int compLen = (int)size - (fo - frameAbs);
                            var outBuf = new byte[declen];
                            nuint got;
                            fixed (byte* cp = &d[fo])
                            fixed (byte* op = outBuf)
                                got = Methods.ZSTD_decompress_usingDDict(dctx, op, (nuint)declen, cp, (nuint)compLen, ddict);
                            if (Methods.ZSTD_isError(got))
                                throw new InvalidDataException($"zstd error at {full}");
                            ownText = Encoding.Latin1.GetString(outBuf, 0, (int)got);
                        }
                        else
                        {
                            res.Raw++;
                            ownText = Encoding.Latin1.GetString(d, frameAbs, (int)size);
                        }
                    }
                    else res.NoText++;

                    res.Types[full] = new DecodedType(full, parent, ownText);
                    seen++;
                }
            }
            res.Aligned = framePos == framesLen;
            if (!res.Aligned)
                throw new InvalidDataException("Frame misalignment after decode — the located layout did not fully lock onto this build.");
        }
        finally
        {
            Methods.ZSTD_freeDDict(ddict);
            Methods.ZSTD_freeDCtx(dctx);
        }
        return res;
    }
}
