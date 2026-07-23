using System.Text;

namespace MetadataPatchEditor.Core;

/// <summary>
/// Decoder for Warframe's v44 Languages.bin (Oodle-decompressed). Port of decoder/lang_parse.py.
/// Returns {fullTag -> localizedString}, e.g. "/Lotus/Language/Weapons/KuvaGrnBowName" -> "Kuva Bramma".
/// Values are magicless-ZSTD compressed against a shared dictionary embedded in the file.
/// </summary>
public static class LanguagesBin
{
    const ushort FLAG_COMPRESSED = 0x0200;

    static uint U32(byte[] d, int o) => BitConverter.ToUInt32(d, o);
    static ushort U16(byte[] d, int o) => BitConverter.ToUInt16(d, o);

    static (ulong val, int next) Leb(ReadOnlySpan<byte> b, int i)
    {
        ulong r = 0; int sh = 0;
        while (true) { byte x = b[i++]; r |= (ulong)(x & 0x7f) << sh; if ((x & 0x80) == 0) break; sh += 7; }
        return (r, i);
    }

    public static Dictionary<string, string> Decode(byte[] d)
    {
        var outp = new Dictionary<string, string>(StringComparer.Ordinal);

        int numLang = (int)U32(d, 28);
        int o = 32;                                   // skip 16B hash + u32x4 (20,44,1,numLang)
        for (int i = 0; i < numLang; i++) o += 4 + (int)U32(d, o);

        int dictLen = (int)U32(d, o); o += 4;
        var dict = d[o..(o + dictLen)]; o += dictLen; // shared zstd dictionary (magic 37 A4 30 EC)
        int numPaths = (int)U32(d, o); o += 4;

        using var z = new ZstdDictDecompressor(dict, magicless: true);

        int p = o;
        for (int s = 0; s < numPaths; s++)
        {
            int plen = (int)U32(d, p); p += 4;
            string prefix = Encoding.UTF8.GetString(d, p, plen); p += plen;
            int chunkLen = (int)U32(d, p); p += 4;
            int chunkStart = p; p += chunkLen;
            int numLabels = (int)U32(d, p); p += 4;

            for (int l = 0; l < numLabels; l++)
            {
                int nlen = (int)U32(d, p); p += 4;
                string name = Encoding.UTF8.GetString(d, p, nlen); p += nlen;
                int offset = (int)U32(d, p); p += 4;
                int size = U16(d, p); p += 2;
                int flags = U16(d, p); p += 2;

                int sliceStart = chunkStart + offset;
                string tag = prefix + name;

                if ((flags & FLAG_COMPRESSED) != 0)
                {
                    var slice = d.AsSpan(sliceStart, size);
                    var (usize, j) = Leb(slice, 0);
                    try { outp[tag] = Encoding.UTF8.GetString(z.Decompress(slice[j..], (int)usize)); }
                    catch { /* skip undecodable */ }
                }
                else
                {
                    int len = size;
                    if (len > 0 && d[sliceStart + len - 1] == 0) len--;   // strip trailing NUL
                    outp[tag] = Encoding.UTF8.GetString(d, sliceStart, len);
                }
            }
        }
        return outp;
    }
}
