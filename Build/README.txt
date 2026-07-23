Warframe Metadata Editor — prebuilt, standalone

WHAT'S HERE
  MetadataPatchEditor.exe   the whole app (self-contained: the .NET runtime is bundled inside;
                            no .NET install needed).
  oo2core_9.dll             Oodle decompressor, required to read the game cache. Must sit NEXT TO
                            the .exe. (It can't be embedded — it's a native game library.)

TO RUN
  Keep both files together in the same folder, then double-click MetadataPatchEditor.exe.
  Click "Decode from Cache…" and point it at your Warframe folder (or its Cache.Windows).

That's it — copy this whole Build folder anywhere and it runs.
