#!/bin/bash
export MSYS_NO_PATHCONV=1
cd "/c/Users/Bartek/OneDrive/Dokumenter/Warframe RE PROJECT RENOVICE/repos/apps/metadata-editor"
export OUT="/c/Users/Bartek/AppData/Local/Temp/claude/C--Users-Bartek-OneDrive-Dokumenter-Warframe-RE-PROJECT-RENOVICE/da3f97f0-d2c7-475a-a46d-2f428b02d1ea/scratchpad/wdec"
export EXE="/c/Users/Bartek/OneDrive/Dokumenter/Warframe RE PROJECT RENOVICE/repos/apps/metadata-editor/updater/bin/Release/net9.0/openwf-metadata-updater.exe"
dec() { p="$1"; f="$OUT/$(echo "$p" | sed 's#^/##; s#/#__#g').txt"; [ -s "$f" ] && return; "$EXE" inspect-type --game "C:\Program Files (x86)\Steam\steamapps\common\Warframe" --type "$p" > "$f" 2>&1; }
export -f dec
xargs -a "$1" -P 4 -I{} bash -c 'dec "{}"'
echo DECODE DONE
