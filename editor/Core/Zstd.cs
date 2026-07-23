using ZstdSharp.Unsafe;

namespace MetadataPatchEditor.Core;

/// <summary>Reusable magicless-ZSTD decompressor bound to a shared dictionary (mirrors LotusLib).</summary>
public sealed unsafe class ZstdDictDecompressor : IDisposable
{
    ZSTD_DCtx_s* _dctx;
    ZSTD_DDict_s* _ddict;

    public ZstdDictDecompressor(byte[] dict, bool magicless = true)
    {
        _dctx = Methods.ZSTD_createDCtx();
        if (magicless)
            Methods.ZSTD_DCtx_setParameter(_dctx, ZSTD_dParameter.ZSTD_d_experimentalParam1, 1); // ZSTD_d_format = magicless
        fixed (byte* dp = dict) _ddict = Methods.ZSTD_createDDict(dp, (nuint)dict.Length);
    }

    public byte[] Decompress(ReadOnlySpan<byte> frame, int decompressedLen)
    {
        var outBuf = new byte[decompressedLen];
        nuint got;
        fixed (byte* cp = frame)
        fixed (byte* op = outBuf)
            got = Methods.ZSTD_decompress_usingDDict(_dctx, op, (nuint)decompressedLen, cp, (nuint)frame.Length, _ddict);
        if (Methods.ZSTD_isError(got)) throw new InvalidDataException("zstd decompress error");
        return (int)got == decompressedLen ? outBuf : outBuf[..(int)got];
    }

    public void Dispose()
    {
        if (_ddict != null) { Methods.ZSTD_freeDDict(_ddict); _ddict = null; }
        if (_dctx != null) { Methods.ZSTD_freeDCtx(_dctx); _dctx = null; }
    }
}
