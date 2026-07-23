#!/usr/bin/env python3
"""Offline Packages.bin decoder — direct port of LotusLib PackagesBin.cpp (Puxtril).
Values are ZSTD-compressed (magicless) against an embedded raw-content dictionary.
Layout: [16 hash][u32 hdrSize=20][u32 ver][u32 flags][skip4][u32 refCount]{u32 len;ascii;skip2}*
        [u32 pkgCount=0][u32 comFlagsLen][comFlags][u32 comSizeLen][comSize][u32 comZLen][comZ]
        [u32 entityCount]{u32 len;pkg; u32 len;filename; skip3; u32 len;parentType}*
  comSize = [u32 dictSize][ULEB size per has-text entity]
  comZ    = [dict(dictSize)][frame per has-text entity]
  comFlags= bitstream (LSB first): per entity 1 bit hasText, then if hasText 1 bit isCompressed
"""
import struct, sys, zstandard

DEC = r"C:\Users\Bartek\AppData\Local\Temp\Packages.bin.dec"

class R:
    def __init__(s, b, p=0): s.b=b; s.p=p
    def u32(s): v=struct.unpack_from('<I',s.b,s.p)[0]; s.p+=4; return v
    def u8(s): v=s.b[s.p]; s.p+=1; return v
    def ascii(s,n): v=s.b[s.p:s.p+n]; s.p+=n; return v.decode('latin1')
    def skip(s,n): s.p+=n
    def take(s,n): v=s.b[s.p:s.p+n]; s.p+=n; return v
    def uleb(s):
        r=0; sh=0
        while True:
            byte=s.b[s.p]; s.p+=1; r|=(byte&0x7f)<<sh
            if not (byte&0x80): break
            sh+=7
        return r

def uleb_at(buf,pos):
    r=0; sh=0; start=pos
    while True:
        byte=buf[pos]; pos+=1; r|=(byte&0x7f)<<sh
        if not (byte&0x80): break
        sh+=7
    return r, pos-start

def decode(path=DEC, want_paths=None, save=None):
    d=open(path,'rb').read()
    r=R(d,16)
    hdr=r.u32(); ver=r.u32(); flags=r.u32(); r.skip(4)
    refCount=r.u32()
    refs=[]
    for _ in range(refCount):
        ln=r.u32(); refs.append(r.ascii(ln)); r.skip(2)
    pkgCount=r.u32()
    comFlags=R(r.take(r.u32()))
    comSize =R(r.take(r.u32()))
    comZ    =r.take(r.u32())
    dictSize=comSize.u32()
    dict_bytes=comZ[0:dictSize]
    czpos=dictSize
    entityCount=r.u32()
    print(f"ver={ver} hdr={hdr} flags={flags} refs={refCount} pkgCount={pkgCount} "
          f"entities={entityCount} dictSize={dictSize} comZlen={len(comZ)}")

    zdict=zstandard.ZstdCompressionDict(dict_bytes, dict_type=zstandard.DICT_TYPE_RAWCONTENT)
    dctx=zstandard.ZstdDecompressor(dict_data=zdict, format=zstandard.FORMAT_ZSTD1_MAGICLESS)

    # comFlags bit reader (LSB-first)
    fb=comFlags; cur=fb.u8(); bit=0
    def nextbit():
        nonlocal cur,bit
        v=(cur>>bit)&1; bit+=1
        if bit>7: cur=fb.u8(); bit-=8
        return v

    out={}
    n_text=n_comp=n_raw=0
    for i in range(entityCount):
        pl=r.u32(); pkg=r.ascii(pl)
        fl=r.u32(); fn=r.ascii(fl)
        r.skip(3)
        ptl=r.u32(); parentType=r.ascii(ptl)
        hasText=nextbit()
        text=None
        if hasText:
            n_text+=1
            size=comSize.uleb()
            frame=comZ[czpos:czpos+size]; czpos+=size
            isComp=nextbit()
            if isComp:
                declen,ulebn=uleb_at(frame,0)
                comp=frame[ulebn:]
                try:
                    text=dctx.decompress(comp, max_output_size=declen).decode('latin1')
                    n_comp+=1
                except Exception as e:
                    text=f"<<decompress error: {e}>>"
            else:
                text=frame.decode('latin1'); n_raw+=1
        fullpath=pkg+fn
        parent=parentType if (parentType[:1]=='/') else pkg+parentType
        out[fullpath]={"parent":parent,"text":text}
        if want_paths and any(w in fullpath for w in want_paths) and text:
            print(f"\n===== {fullpath}  (parent={parent}) =====")
            print(text[:900])
    print(f"\nparsed off={r.p}/{len(d)} rem={len(d)-r.p}; hasText={n_text} comp={n_comp} raw={n_raw}; total={len(out)}")
    if save:
        import json
        json.dump({k:v for k,v in out.items()}, open(save,'w',encoding='utf-8'), ensure_ascii=False)
        print("saved ->", save)
    return out

if __name__=="__main__":
    decode(want_paths=["GrnBow/GrnBowWeapon","Tenno/Rifle/Rifle","Bows/LotusLongBow"])
