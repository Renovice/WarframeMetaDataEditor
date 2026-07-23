#!/usr/bin/env python3
# -----------------------------------------------------------------------------
#  dump_data.py  --  pull WARFRAMES and PROJECTILE entities into data\dumps using
#  REAL paths only (no guessing):
#     * warframes  : ExportWarframes.json  (DE's real 125 /Lotus/Powersuits paths)
#     * projectiles: EE.log, which logs "Reading metadata for <real path>" for
#                    EVERY type the game reads -- so firing a projectile weapon
#                    puts its real projectile path in the log.
#  Values come from the live game (get_effective_metadata) -- same as your other dumps.
#
#  ONE-TIME SETUP:  in  OpenWF\Client Config.json  set   "save_all_metadata": true
#     (makes every read type queryable). "write_all_metadata_reads_to_ee_log" is
#     already true. Restart the game after changing it.
#
#  THEN, with the game running:
#     - open the Arsenal Warframe-selection screen (loads warframes)
#     - go into a mission and FIRE the projectile weapons you care about
#     - python dump_data.py
# -----------------------------------------------------------------------------
import json, os, re, urllib.request

# ---- CONFIG (edit if paths differ) -----------------------------------------
PORT = 6900   # client_http_port from Client Config.json
SERVER_EXPORT = r"C:\Users\Bartek\OneDrive\Dokumenter\OpenWF Server 08.07.2026\SpaceNinjaServer\node_modules\warframe-public-export-plus"
EE_LOG = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Warframe", "EE.log")
# ----------------------------------------------------------------------------

HERE = os.path.dirname(os.path.abspath(__file__))
DUMPS = os.path.join(HERE, "data", "dumps")

DICT = {}
try:
    with open(os.path.join(DUMPS, "dict.en.json"), encoding="utf-8") as f:
        DICT = json.load(f)
except Exception:
    pass

def fetch(path):
    try:
        with urllib.request.urlopen(f"http://localhost:{PORT}/get_effective_metadata?{path}", timeout=8) as r:
            return r.read().decode("utf-8", "replace")
    except Exception:
        return ""

def sanitize(name):
    for ch in '<>:"/\\|?*':
        name = name.replace(ch, "_")
    return name.strip() or "unnamed"

def name_from_body(body, path):
    for line in body.splitlines():
        if line.startswith("LocalizeTag="):
            tag = line.split("=", 1)[1].strip()
            if tag in DICT:
                return DICT[tag]
    return path.rsplit("/", 1)[-1]

def write_dump(folder, path, body):
    os.makedirs(os.path.join(DUMPS, folder), exist_ok=True)
    fn = sanitize(name_from_body(body, path)) + ".txt"
    with open(os.path.join(DUMPS, folder, fn), "w", encoding="utf-8") as out:
        if not body.startswith(">"):
            out.write(">" + path + "\n")
        out.write(body)

def dump_paths(paths, folder):
    got = empty = 0
    for p in paths:
        body = fetch(p)
        if len(body) < 20:
            empty += 1
            continue
        write_dump(folder, p, body)
        got += 1
    print(f"{folder:12s}: dumped {got}, empty/unloaded {empty}")
    return empty

def warframe_paths():
    fn = os.path.join(SERVER_EXPORT, "ExportWarframes.json")
    if not os.path.isfile(fn):
        print(f"[!] ExportWarframes.json not found at {fn} -- edit SERVER_EXPORT."); return []
    with open(fn, encoding="utf-8") as f:
        return list(json.load(f).keys())

def projectile_paths_from_log():
    if not os.path.isfile(EE_LOG):
        print(f"[!] EE.log not found at {EE_LOG}"); return []
    rx = re.compile(r"Reading metadata for (/Lotus/\S+)")
    seen = []
    with open(EE_LOG, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = rx.search(line)
            if m:
                p = m.group(1)
                if "rojectile" in p and p not in seen:
                    seen.append(p)
    return seen

if __name__ == "__main__":
    alive = fetch("/Lotus/Weapons/Tenno/Rifle/Rifle") or fetch("/Lotus/Powersuits/Excalibur/Excalibur")
    if not alive:
        print(f"[!] No response on http://localhost:{PORT} — is the game running, and is save_all_metadata=true?")
    print(f"Dumping into {DUMPS}\n")

    e1 = dump_paths(warframe_paths(), "Warframes")

    projs = projectile_paths_from_log()
    print(f"(EE.log had {len(projs)} real projectile paths)")
    e2 = dump_paths(projs, "Projectiles")

    if e1 or e2:
        print("\nEmpties = either save_all_metadata is off, or that type wasn't loaded this session.")
        print("Fix: OpenWF\\Client Config.json -> \"save_all_metadata\": true, restart, load warframes /")
        print("fire the weapons, then re-run this script.")
    print("\nDone. Reopen the editor — the Warframes / Projectiles tabs will list what got dumped.")
