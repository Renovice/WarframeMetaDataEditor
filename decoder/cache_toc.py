#!/usr/bin/env python3
# Offline reader for Warframe's Cache.Windows *.toc index.
# Format (confirmed on B.Misc.toc): 8-byte header [magic 0x1867C64E, version],
# then fixed 96-byte entries:
#   int64 cacheOffset | int64 timeStamp | int32 compLen | int32 decompLen
#   | int32 ? | int32 parentDirIndex | char[64] name
# Directories have offset -1; files have offset >= 0. Full path = walk parents.
import struct, sys

TOC = r"C:\Users\Bartek\OneDrive\Dokumenter\Warframe\Cache.Windows\B.Misc.toc"

def parse(path):
    data = open(path, "rb").read()
    magic, ver = struct.unpack_from("<II", data, 0)
    if magic != 0x1867C64E:
        raise SystemExit(f"bad magic {magic:#x}")
    n = (len(data) - 8) // 96
    off = [0]*n; comp = [0]*n; dec = [0]*n; par = [0]*n; nm = [""]*n
    for i in range(n):
        b = 8 + i*96
        off[i], _ts = struct.unpack_from("<qq", data, b)
        comp[i], dec[i], par[i], _u = struct.unpack_from("<iiii", data, b+16)
        nm[i] = data[b+32:b+96].split(b"\x00", 1)[0].decode("utf-8", "replace")
    # build full paths via parent tree (iterative to avoid deep recursion)
    paths = [None]*n
    def build(i):
        stack = []
        while paths[i] is None:
            p = par[i]
            if p == i or p < 0 or p >= n:
                paths[i] = "/" + nm[i]
                break
            if paths[p] is not None:
                paths[i] = paths[p] + "/" + nm[i]
                break
            stack.append(i); i = p
        while stack:
            j = stack.pop()
            paths[j] = paths[par[j]] + "/" + nm[j]
        return paths
    for i in range(n):
        build(i)
    return ver, n, off, comp, dec, par, paths

if __name__ == "__main__":
    toc = sys.argv[1] if len(sys.argv) > 1 else TOC
    ver, n, off, comp, dec, par, paths = parse(toc)
    files = [i for i in range(n) if off[i] >= 0]
    lotus = [i for i in files if paths[i].startswith("/Lotus/")]
    noext = [i for i in lotus if "." not in paths[i].rsplit("/", 1)[-1]]
    print(f"TOC: {toc}")
    print(f"  version {ver}  entries {n}  files {len(files)}  /Lotus files {len(lotus)}  /Lotus no-ext {len(noext)}")
    hit = next((i for i in range(n) if paths[i] == "/Lotus/Weapons/Tenno/Rifle/Rifle"), None)
    print("  BRATON /Lotus/Weapons/Tenno/Rifle/Rifle:",
          (f"off={off[hit]} comp={comp[hit]} decomp={dec[hit]}" if hit is not None else "NOT FOUND"))
    if len(sys.argv) > 2:
        sub = sys.argv[2]
        matches = [i for i in range(n) if sub in paths[i]]
        print(f"  paths containing '{sub}': {len(matches)}")
        for i in matches[:15]:
            print(f"    {paths[i]}  off={off[i]} comp={comp[i]} decomp={dec[i]}")
    else:
        print("  sample /Lotus no-ext paths:")
        for i in noext[:12]:
            print(f"    {paths[i]}   comp={comp[i]} decomp={dec[i]}")
