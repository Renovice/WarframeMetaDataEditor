# Riven physical-trait rules (Impact / Puncture / Slash)

Computes, per Riven-compatible weapon, which physical traits can roll, from the decoded client:
the weapon's effective primary attack `Type` (single physical type -> only that one; element -> none;
`DT_PHYSICAL` -> each physical needs a share > 0.25 of the split). Evidence and accuracy (976/1008 agreement
with PE+ `riven_unrollables`; DE has per-weapon exceptions at the 0.25-0.34 boundary):
`work/research/riven-roll-rules-2026-09-29/THRESHOLD_25_PERCENT.md`.

The server uses PE+ `riven_unrollables` first; this output only fills weapons newer than that list.

## Run (after a client update)
1. Decode the weapons: `python wrule2.py list` lists projectile types to decode; `bash decode_all.sh <pathlist>`
   runs `openwf-metadata-updater inspect-type` for each path into `wdec/` (weapons) and `pdec/` (projectiles).
   Edit `OUT`/paths at the top of `decode_all.sh` and `wrule2.py` for your machine.
2. `python wrule2.py eval` writes `weapon_rule.json`.
3. Pass it to the server generator:
   `python scripts/metadata-update/build-current-export.py --exports ... --metadata ... --server ... --weapon-rules weapon_rule.json`
