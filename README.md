# Warframe Metadata Editor

A tool to **read and patch Warframe's per-type metadata entirely offline** — every weapon /
warframe / mod stat (`reloadTime`, `AmmoCapacity`, `OmegaAttenuation`, …), decoded straight from
the game's local `Packages.bin`, with **no game running**.

## What lives where

| Folder           | Contents                                                                                                                                                                                                                                                                                                                            |
| ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`editor/`**    | The C# WPF editor app (`src` → `App` + `Core`; compiled build in `editor/build/`). Internal assembly name is still `MetadataPatchEditor`.                                                                                                                                                                                           |
| **`updater/`**   | Compiled, preview-first OpenWF metadata synchronizer. It now covers the complete client item/cosmetic registry, challenges/Nightwave, all current vendor manifests, current syndicate offerings, and current Dojo recipes. It hashes inputs, diffs snapshots, validates server paths, applies explicitly, backs up, and rolls back. |
| **`decoder/`**   | The offline `Packages.bin` decoder + cache tools. `pkg_decode_v46.py` is **the** decoder. See `decoder/README.md`. Exploratory RE history is parked in `decoder/_re_history/`.                                                                                                                                                      |
| **`data/`**      | Decoded datasets + dumps — **generated locally, git-ignored** (not in the repo). The editor doesn't need them; regenerate with the decoder scripts if you want them.                                                                                                                                                                |
| **`reference/`** | Third-party parser source (Puxtril/LotusLib etc.) — **git-ignored**; external upstreams, see `EDITOR_NOTES.md` §7.                                                                                                                                                                                                                  |
| **`findings/`**  | `FINDINGS.md` (the chronological RE log). The solved formats + all findings are consolidated in [`EDITOR_NOTES.md`](EDITOR_NOTES.md).                                                                                                                                                                                               |

## The data (`data/`)

| File                       | What it is                                                                                                                    |
| -------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `packages_owntext.jsonl`   | Every type's **raw decoded own-text** (201,546 types, full field text incl. nested blocks).                                   |
| `packages_effective.jsonl` | **Composed** metadata (own + inherited top-level fields) — the offline equivalent of `get_effective_metadata`, 309,787 types. |
| `editor_dataset.json`      | (Legacy) merged dataset for offline analysis. The **editor decodes live from the cache** and does not require it.             |
| `dumps/`                   | Ground-truth `get_effective_metadata` dumps used to validate the decoder.                                                     |

## How it works (one line)

`Packages.bin` stores per-type property text as **magicless-ZSTD frames compressed against a 1 MB
dictionary embedded in the file**. The decoder decompresses each frame against that dictionary and
composes values up the inheritance chain. Full spec (both `Packages.bin` **and** `Languages.bin`) plus
all game-mechanics findings: [`EDITOR_NOTES.md`](EDITOR_NOTES.md).

## Building the editor (the Windows app)

**Prerequisites**

- **Windows** — the editor is a WPF app.
- **.NET SDK 9.0+** — `dotnet --version` should report ≥ 9.
- **`oo2core_9.dll`** (Oodle) — copy it from your Warframe install folder into `editor/lib/`. It's
  proprietary so it isn't in the repo. The project **builds without it**, but the app can't read the
  game cache until this DLL sits next to the exe.

**Build & run**

```
# run it directly:
dotnet run --project editor/App -c Release

# …or produce the standalone single-file exe (lands in the publish folder AND in Build/):
dotnet publish editor/App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Keep restore enabled for this command after a normal framework-only build: the
single-file package needs a `net9.0-windows/win-x64` restore target, which the
generic build asset graph does not necessarily retain.

NuGet dependencies (`ZstdSharp.Port`) restore automatically. In the app: **Decode from Cache…** →
point it at your Warframe folder (or its `Cache.Windows`).

The explicitly selected cache is authoritative for saves: if that game root contains
`OpenWF/Metadata Patches`, the editor uses it instead of any auto-detected backup installation.

## Decoder scripts (Python, optional)

The standalone Python decoder — handy for bulk offline dumps; the editor doesn't need it.

```
# 1) (re)extract the current Packages.bin from the game cache -> %TEMP%\Packages.bin.dec
python decoder/cache_extract.py           # or point pkg_decode_v46.PATH at an existing .dec

# 2) decode + validate against the dumps
python decoder/pkg_decode_v46.py

# 3) produce the full offline datasets in data/
python decoder/export_packages_full.py
```

Requires Python 3 + `zstandard` (`pip install zstandard`).

## Editing values

The decoded value is the **static base** DE ships. To change what the game uses, write an
**OpenWF metadata patch** (`OpenWF/Metadata Patches/*.txt`) — patches override the base on next
launch. The editor helps author these; the decoder gives you the true base to start from.

### Status behavior and weapon damage/status editor

The live-cache catalog now includes a dedicated **Status Effects** category. Pick
a native handler and click **Open Visual Status Editor…** (or double-click the
handler) to edit its reflected duration,
stacking, DOT/radial damage, damage type, caps, and scalar behavior switches.
Normal enemy, player/Tenno, faction-specific, VIP, and Railjack/space handlers
are labelled separately so a space override cannot be mistaken for ordinary
ground-enemy behavior. Internal `Radiant` and `Sentient` handlers are presented
under their player-facing names, **Void** and **Tau**. Intentionally immune enemies still remain immune: there
is no accepted proc instance for metadata to alter.

Select a weapon and click **Open Visual Weapon Editor…** (or double-click it).
The normal view is task-based rather than a raw metadata table: clickable Overview,
Damage & Elements, Fire Controls, and Advanced sections; stat cards; colored active
element chips; and plain-language help beside every value. Choose an exact direct,
alternate, radial, or damage-over-time profile, then add/remove physical or elemental
damage as absolute numbers. Any supported components can coexist, including Magnetic
plus Viral. **Status & Procs** exposes that profile's `ProcChance` as a readable
percentage, converts it to the native decimal automatically, and keeps forced procs
separate. Values above 100% are accepted. A status-only change does not convert or
rewrite the profile's damage encoding. Fire Controls exposes each firing state's native `fireRate` in RPM, shows
the converted shots-per-second value, and exposes its reload plus applicable charge
or burst timing. Raw query paths and storage details remain available only under
Advanced.

The clean visual shell is now shared application infrastructure rather than a
weapon-window-only style: task navigation, stat cards, effect cards, section panels,
and explanation panels use common resources. Weapon, status-behavior, and DOT views
already use that system. Exact metadata remains the universal Advanced fallback while
additional category-specific visual editors are migrated without removing raw access.

Select a Warframe and click **Open Visual Warframe Editor…** to use the same
task-based shell for powersuit stats. The overview shows native base-to-rank-30
Health, Shields, Armor, and Energy plus starting Energy and Sprint Speed. Core
Stats edits the exact existing top-level metadata fields. Rank Scaling presents
the total bonuses from the existing `LevelUpgrades` milestone array; changing a
total scales those native milestones proportionally without creating new rank
events. The preview is explicitly unmodded and does not pretend to be the final
Arsenal value. See
[`findings/WARFRAME_VISUAL_STATS_EDITOR_2026-09-02.md`](findings/WARFRAME_VISUAL_STATS_EDITOR_2026-09-02.md).

The composer totals damage and converts legacy fraction profiles to the game's
native explicit `UseNewFormat=1` representation only when changed.
Because the bootstrapper query operation cannot create a missing nested `DT_*`
field, the composer replaces the complete containing top-level block after parsing
and preserving all unrelated fire-mode, crit, status, forced-proc, and animation
metadata. Composed weapon edits are refused in batch mode because weapon block
layouts differ.

See [`findings/STATUS_AND_WEAPON_METADATA_EDITOR_2026-08-28.md`](findings/STATUS_AND_WEAPON_METADATA_EDITOR_2026-08-28.md)
for the evidence, boundaries, and verification ledger.

The main category selector is hierarchical rather than one long flat list.
Categories are divided into icon-labelled weapon, character, upgrade, status,
Railjack, mission, crafting, cosmetic, and miscellaneous submenus. Field sections
carry their own icons, both scrollbar orientations use the shared dark template,
and toolbar colors follow a consistent semantic action palette. Internal operation
and path columns are hidden behind **Advanced paths**, and numeric step arrows
appear only for the active value instead of filling every row. Large families
are subdivided further: Mods are split into Warframe, Primary, Secondary, Melee,
Companion, Aura/Stance, Archwing/Space, and Other; status handlers are split into
Ordinary Ground Enemies, Player & Tenno, Specialized Enemy Overrides, and Railjack/Space Combat.
The category tree remains open while groups are expanded and closes only after a
real category is selected. Its wider explanation pane sits beside the main field
workspace; Field Details, Actions, and Patch Controls are arranged below so they
do not consume a permanent right-hand column.

Metadata values are type-aware rather than one undifferentiated text column:
decoded enum families such as `QA_*`, `AP_*`, `DT_*`, `RM_*`, and
`UpgradeType` are strict dropdowns; numerical fields have up/down controls and
keyboard stepping; proven `0/1` switches use a two-state toggle; unknown or
free-form values remain text. Hovering a category or field explains its role
and retains the exact metadata path. The field grid is divided into collapsible
sections so upgrade, equipment, scripting, status, and attack-profile values do
not form one unreadable flat table.

Damage labels are structural rather than guessed from a bare field name. For
example, `StackedUpgrades.0.DamageType=DT_ANY` is shown as an **Upgrade damage
filter**: it means that nested debuff accepts incoming damage of every type. The
selected status handler path still defines Cold, Heat, Corrosive, and so on.
Actual DOT/attack/radial element fields retain distinct damage-type labels.

### Reusable native mod effects

Select a mod with an `Upgrades` collection and click **Add Mod Effect…**. The
composer preserves every existing entry and appends an ordinary native upgrade
using decoded `UpgradeType`, operation, and damage-type choices. The supplied
Fire-resistance preset uses the exact current-cache Flame Repellent shape:
`AVATAR_DAMAGE_TAKEN`, `MULTIPLY`, and `DT_FIRE`.

Native multi-effect mods store a localization key per upgrade entry, so the
preset can also carry Flame Repellent's existing effect-description key. A
checkbox chooses whether that translated line is included or whether only the
gameplay effect is emitted. This is
structurally the same description mechanism used by current multi-effect mods,
but the newly combined card still requires an in-game rendering check. Custom
effects with an empty localization key can change gameplay without gaining a
new visible description line. The editor refuses a simultaneous whole-list
addition and conflicting manual nested edits to that same `Upgrades` list.

See [`findings/TYPED_FIELDS_AND_MOD_EFFECTS_2026-08-28.md`](findings/TYPED_FIELDS_AND_MOD_EFFECTS_2026-08-28.md)
for the live-cache evidence and the exact verified boundaries.

### Beginner-readable status behavior

The **Visual Status Editor…** uses the same task-based layout as the weapon
editor. Every physical, elemental, combined, Void, and Tau status family has a
distinct scalable vector icon, status-colored metric cards, clickable effect
cards, and separate Behaviour & Stacks, Damage Over Time, Status Modifiers, and
Advanced pages. The visual pages hide raw paths while preserving every supported
field in Advanced.

Cold explicitly distinguishes the first proc from repeated stacks. Current
metadata stores `MaxStacks=9` and `RepeatFreezeModifier=0.05`; the editor therefore
shows **10 total procs to full freeze** (1 initial + 9 added) and **+5% freeze
buildup per added stack**. It also exposes the separate Overguard repeat-stack cap
and critical-vulnerability scaling. Full freeze is described as a cap-triggered
state rather than incorrectly claiming that an exposed modifier reaches 100%.
Other statuses receive their own effect chain and decoded values: DOT/source-hit
scaling, radial behavior, armor or health vulnerability, shield effects, stagger,
confusion, bullet attraction, and other fields are shown only when relevant to
that handler. Known enums render as dropdowns and numeric values remain directly
editable.

Dropdowns use friendly labels and per-option hover help throughout the shared
metadata editor. Known native modes explain their practical behavior; for
example, the three `StackStyle` choices explain shared, per-instigator, and
per-hit ownership. Damage types, proc types, operations, rarity/fusion tiers,
and slot polarities receive the same treatment. A decoded enum without a
verified hand-written explanation keeps its exact native token and shows the
field-level description instead of receiving a guessed meaning.

**Add/Edit DOT…** opens a dedicated native payload builder. It chooses the tick
damage type, the fraction of the triggering hit used for each tick, an observed
native repeated-hit mode, and the optional highest-damage-basis rule. That rule
only selects a basis when the handler already matches or consolidates DOT damage;
it does not collapse stock Toxin's independent per-hit instances. Existing
payloads are preserved; a status such as Cold can receive a newly populated
payload because `LotusFreezeHandler` exposes the common reflected DOT fields.
Newly converting a stock non-DOT status is labelled as requiring live damage QA;
the editor does not claim that static schema verification is an in-game test.

Base enemy handlers explicitly state that ordinary child handlers inherit their
unchanged fields. Triangle and Nokko entries are labeled as specialized child
overrides; the editor does not duplicate a Base Heat patch into them because
they already inherit Base Heat behavior. Exact query paths remain available
through **Advanced: show exact metadata paths**.

### Universal status DOT and proc-storage controls

Every status-behavior page exposes **Proc storage / damage ownership** even when
the concrete metadata record inherits that field. The dropdown has four safe
choices:

- **Original / inherited** — write no override;
- **OneActiveInstance** — one shared proc record;
- **OneInstancePerInstigator** — one record for each weapon, ability, or other source;
- **OneInstancePerHit** — every proc keeps an independent record.

The same page universally exposes **DOT consolidation mode** and **Keep highest
DOT damage basis** with an **Original / inherited** choice. **Add/Edit DOT…** is
the general builder for any selected status: choose its tick damage type,
source-hit fraction, repeated-DOT mode, proc-storage mode, and highest-basis
mode. No Heat-, Toxin-, or other element-specific preset button is used.

These controls prepare ordinary client gameplay metadata fields, not an OpenWF
server-definition overlay and not a per-frame Lua addon. Save whatever
combination you choose as your own patch; remove that generated file and
reload/restart metadata to revert it. See
[`findings/UNIVERSAL_STATUS_STACK_STORAGE_2026-08-29.md`](findings/UNIVERSAL_STATUS_STACK_STORAGE_2026-08-29.md).

### Native mod-slot layout editor

After decoding the current cache and selecting any item with `ArtifactSlots`, click **Mod Slot Layout…**. The editor creates a reviewable, reversible, append-only patch that:

- preserves every decoded stock polarity at its exact original index;
- adds a chosen number of ordinary `AP_*` slots using the game's native card grid;
- supports the proven Warframe second-Aura profile using Jade's native `AdditionalBaseModTypes`;
- supports the proven Necramech all-ordinary layout (stock is a clean 12-slot grid);
- leaves Arcane behavior at the normal maximum of two;
- refuses category/layout combinations whose special-slot behavior is not proven.

The U43 renderer does not infer every Warframe position as ordinary. It fixes card indices 1–8 as ordinary, 9 as Aura, 10 as Utility/Exilus, and 11 as the second Aura; overflow after index 11 is ordinary again. Its default installed grid is 3 rows × 4 columns. The largest clean metadata-only Warframe layout is therefore **9 ordinary + 2 Aura + 1 Exilus**, backed by 14 `ArtifactSlots` entries including the two Arcane-tail entries. Larger arrays are clipped, so the editor refuses them until a runtime row/column hook is available.

The button only prepares editor rows and a preview; it does not deploy to the live game folder. Generic equipment categories remain append-only and disable Aura/Stance creation until their precise renderer mapping is proven. Malformed slot sets and object paths are rejected before saving. See [`findings/EQUIPMENT_SLOT_COMPATIBILITY_EDITOR_2026-08-25.md`](findings/EQUIPMENT_SLOT_COMPATIBILITY_EDITOR_2026-08-25.md) for the evidence and hypothesis ledger.

### Mod client metadata and optional OpenWF definition packages

Select a normal mod and click **Mod Client & Server…**. The dialog keeps three concerns separate:

- **Client metadata:** visible `Rarity` plus an explicit selector for every known client
  `FusionLimit` enum (`QA_NONE` through `QA_VERY_HIGH`), or `INHERIT / no client patch`.
- **OpenWF server definition:** optional and off by default. When enabled, the editor installs a
  validated two-file package under `SpaceNinjaServer/Metadata Patches/Enabled/<name>/`.
- **Inventory:** never changed implicitly. Definition packages do not add, remove, rank, or rewrite
  any owned mod.

Client-only mode lets the game use modified presentation/progression metadata while OpenWF's vanilla
definition, WebUI, Public Export, drop tables, and definition-based tools remain unchanged. OpenWF can
still preserve the inventory fingerprint the client sends, including `{"lvl":10}`; that persistence
does not require publishing a rank-10 server definition.

When the optional server component is enabled, OpenWF overlays its effective definition at startup.
The package can be disabled safely from the same dialog, which moves the whole folder from `Enabled`
to `Disabled`; restarting OpenWF then restores the vanilla effective definition. The server validates
all packages before mutating its in-memory export and refuses duplicate package IDs, unknown upgrade
paths, invalid enums/ranks, and two enabled packages claiming the same field. See
[`findings/COORDINATED_CLIENT_SERVER_METADATA_PATCHES_2026-08-26.md`](findings/COORDINATED_CLIENT_SERVER_METADATA_PATCHES_2026-08-26.md).

### Generic OpenWF server metadata packages

Select any decoded client type and click **Generic Server Metadata…**. The editor scans the selected
SpaceNinjaServer installation's `warframe-public-export-plus` datasets and finds exact entries whose
key or nested `uniqueName` matches the selected `/Lotus/...` path. It can then author a removable
`server-definitions.json` package for any mapped entry, including nested entries such as an ability
inside `ExportWarframes`.

The current OpenWF dependency exposes 44 `Export*` datasets. The generic format uses an explicit
dataset, an exact JSON path, and only the changed fields. Existing fields retain their JSON type;
arrays and objects are edited as JSON; new fields are permitted but only matter if OpenWF code
actually consumes them. Packages live under `SpaceNinjaServer/Metadata Patches/Enabled/<name>/` and
can be moved to `Disabled` from the dialog. OpenWF validates the complete enabled plan before applying
anything, rejects unknown datasets/paths, prototype keys, type changes, duplicate IDs, and field-level
conflicts, and never edits the dependency files, inventory, or save data.

This is generic for **server data that actually exists in Public Export Plus**. It is not a promise
that every client `Packages.bin` field has a server equivalent. If the selected client type has no
exact server mapping, the editor refuses automatic generation instead of guessing. See
[`findings/GENERIC_OPENWF_SERVER_METADATA_PATCHES_2026-08-26.md`](findings/GENERIC_OPENWF_SERVER_METADATA_PATCHES_2026-08-26.md).

## License

MIT — see [LICENSE](LICENSE). The code and docs in this repo are MIT-licensed.

**Not included / not covered by this license:** `oo2core_9.dll` (Oodle, RAD Game Tools / Epic) is
proprietary and must come from your own Warframe install — it is **not** redistributed here. The
third-party reference decoders this port learned from (Puxtril/LotusLib et al.) keep their own
licenses; see `EDITOR_NOTES.md` §7 for the upstreams. This is a fan-made tool, not affiliated with
or endorsed by Digital Extremes.
