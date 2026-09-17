# Equipment slots and mod compatibility — editor implementation

Date: 2026-08-25

## Conclusions

| Claim | Current result | Evidence boundary |
|---|---|---|
| Stock Warframe metadata supports more than the usual 12 `ArtifactSlots` entries | TRUE | Modern Jade/Choir has 13 entries. |
| A second Aura is represented only by adding another polarity token | FALSE | Jade also declares two `AdditionalBaseModTypes`; both parts are reproduced in the experiment. |
| Octavia can render a 13-entry layout as two Aura positions while preserving the normal grid | TRUE (LIVE UI) | The 2026-08-25 test showed two Aura positions, eight ordinary positions, one Exilus position, and the existing two Arcane positions. |
| The combined Jade-style patch proves a ninth ordinary mod slot | FALSE | It changed both `ArtifactSlots` and `AdditionalBaseModTypes`; the added position became an Aura position. A length-only test is required. |
| Octavia's two existing Arcane positions survive the 13-entry experiment | TRUE (LIVE UI) | Both stock Arcane positions remained visible. Equip persistence/effects were not retested. |
| A third Arcane can be added with another `ArtifactSlots` entry | FALSE for the tested patch | The patch never requested a third Arcane. OpenWF exposes only `ARCANE_SLOT` and `SECOND_ARCANE_SLOT` feature bits, so a third requires separate client/UI and serialization research. |
| `ArcaneApertureType` is an Arcane slot-count field | FALSE for current evidence | It selects an aperture/unlocker family. It is not a demonstrated count control. |
| Changing `ItemCompatibility` can make Stretch appear on rifles | TRUE (LIVE BROWSER) | Stretch appeared in the primary-weapon mod browser. Equip persistence and gameplay consumption remain independently unproven. |
| One compatibility field automatically supports multiple unrelated equipment families | NOT PROVEN | The controlled experiment replaces Stretch's Warframe compatibility with rifle compatibility. A final additive/multi-family model requires live evidence or a stock metadata precedent. |

## Implemented editor behavior

- `ArtifactSlots`, `AdditionalBaseModTypes`, `CompatibilityTags`, and `IncompatibilityTags` are exposed as complete top-level set fields instead of being hidden as complex blocks.
- The former Octavia-only test button was retired. **Mod Slot Layout…** now targets the selected item and reads its current decoded `ArtifactSlots`.
- Generation is append-only: every stock polarity remains at its exact index and new positions use a user-selected `AP_*` polarity.
- Warframes and Necramechs have explicit profiles; unknown categories use conservative append-only generation and refuse unproven Aura/Stance requests.
- Warframe second-Aura generation also emits Jade's two stock `AdditionalBaseModTypes` values.
- The generator creates a reversible preview and never writes to the live game folder automatically.
- Slot sets are validated as `{...}` with `AP_*` tokens and a bounded entry count.
- Additional mod-base types and item compatibility values are validated as absolute object paths.
- The generator uses one authoritative set of constants, and the dev harness checks deterministic output.

## Exact U43 renderer mapping — 2026-08-25

The authoritative `shared/corpus/de-luau-stock/Lotus_Interface_DiegeticUpgradeCards.lua_B` was rendered with the canonical `repos/toolchains/de-luau-toolchain/bin/derecomp.exe`. The module defines:

```text
NUM_NORMAL_SLOTS = 8
MELEE_STANCE_SLOT = 9
AURA_SLOT_INDEX = 9
UTILITY_SLOT_INDEX = 10
EXTRA_AURA_SLOT_INDEX = 11
AURA_SLOTS = {9, 11}
```

It reads `GetArtifactSlots()`, obtains the Arcane-tail count from `LoadoutUtilities.GetArcaneSlots(...)`, and computes `mCardSlots = #mArtifactSlots - arcaneTailCount`. It then calls `LoadoutUtilities.GetRowColumnForInstallGrid(...)` and builds the installed grid with those native rows and columns.

The historical `LoadoutUtilities` disassembly identifies the default installed-grid result as **3 rows × 4 columns**. The current module-level render is fail-closed with `RENDER_BLOCK_EMITTED_TWICE` / `RENDER_BLOCK_COVERAGE`, so that historical evidence is used only where the current live screenshot independently confirms the 12-card boundary: the 16-entry Octavia patch still displayed exactly a 3×4 installed-card grid.

| Hypothesis | Evidence | Verdict |
|---|---|---|
| The unwanted extra Aura came from stale `AdditionalBaseModTypes`. | The renderer itself reserves card index 11 as `EXTRA_AURA_SLOT_INDEX`; the Aura-free live test still showed it. | FALSE |
| Array reordering can turn card index 11 into an ordinary Warframe slot. | Classification uses the numeric card index, not the polarity token or insertion history. | FALSE |
| Slots after index 11 can be ordinary while retaining native layout. | Only 9, 10, and 11 are special in the renderer; live 12-to-16 expansion showed added ordinary positions, and the grid delegates rows/columns to native `LoadoutUtilities`. | TRUE |
| Sixteen `ArtifactSlots` create fourteen clean visible Warframe cards. | The native grid remained 3×4 in the live screenshot, so positions beyond 12 were clipped rather than arranged into another row. | FALSE |
| A clean metadata-only Warframe maximum is 9 ordinary + 2 Aura + 1 Exilus. | Fourteen `ArtifactSlots` minus two Arcane-tail entries gives exactly twelve visible cards: 8 ordinary + three special + one overflow ordinary. | TRUE for the mapped renderer; final 14-entry live confirmation remains pending. |
| More than twelve clean visible cards can be delivered by metadata alone. | `GetRowColumnForInstallGrid` returns the native row/column capacity independently of `ArtifactSlots`; larger arrays were clipped. | FALSE |
| A clean expanded grid requires custom-drawn cards. | `GetRowColumnForInstallGrid` and the existing native card clips already lay out the expanded positions. | FALSE |

## Why Jade matters

The relevant stock suit is `/Lotus/Powersuits/Choir/ChoirBaseSuit`. It carries 13 `ArtifactSlots` and declares:

```text
AdditionalBaseModTypes={/Lotus/Types/Game/LotusAuraUpgrade,/Lotus/Upgrades/Mods/Aura/FairyQuest/FairyQuestBaseAuraMod}
```

The experiment copies this structural precedent to Octavia Prime while preserving Octavia's decoded polarities. This is substantially stronger than guessing that array length alone controls the whole Arsenal layout, but it still requires a live client/server test because metadata structure does not prove UI layout, persistence, or loadout sanitization behavior.

## Live result — 2026-08-25

The all-in-one patch reached the intended metadata systems, but it did not create a ninth ordinary grid position:

- PASS: a second Aura position appeared;
- PASS: Octavia's eight ordinary positions and two existing Arcane positions remained visible;
- PASS: Stretch appeared in the primary-weapon browser after its compatibility change;
- NOT TESTED: equipping/persisting both Auras, equipping/persisting Stretch, and whether Stretch has a meaningful rifle-side stat consumer;
- NOT ATTEMPTED: a third Arcane position;
- INCONCLUSIVE: whether a 13th `ArtifactSlots` entry without Jade's `AdditionalBaseModTypes` becomes an ordinary slot.

The next controlled experiment therefore changes only `ArtifactSlots`. It deliberately omits `AdditionalBaseModTypes` and the already-proven Stretch compatibility change. If the extra position accepts an ordinary Warframe mod, metadata is sufficient for a ninth logical slot. If the position is absent or unusable, the remaining blocker is the Arsenal's slot classification/layout logic rather than the patch parser.

### Correction after attempted isolated test

The subsequent report that the Aura remained was initially not an isolated-test result. Inspection of the live patch directory showed that `Octavia_AllSlots_Compatibility_TEST.txt` was still active and still contained Jade's `AdditionalBaseModTypes`. The Aura behavior was therefore expected and does not falsify the Aura-free hypothesis. The live file was replaced with `Octavia_MultiOrdinarySlots_TEST.txt`, which contains no Aura-type or Stretch edits and increases the array from 12 to 16 entries for a more visible slot-count test.

### Live multi-slot result and ordering correction

The 12-to-16 test successfully expanded the ordinary mod layout and accepted ordinary Warframe mods: TRUE. It also displayed another Aura. A post-test disk audit found no old Octavia patch and no active `AdditionalBaseModTypes` directive. If the client was freshly launched, the extra Aura was therefore not produced by the removed Jade patch.

The test inserted four candidates at index 10 based on the unproven assumption that Octavia's final two `ArtifactSlots` entries were Arcane-reserved. OpenWF evidence shows Arcane positions use separate equipment feature bits. Follow-up H7 preserves every stock entry at its original index and appends the candidates at indices 12–15, distinguishing array-length behavior from index-shift behavior.

H7 live result: FALSE. Append ordering still produced an additional Aura and the additional ordinary positions. The resulting rule for this client build is: increasing a Warframe's `ArtifactSlots` expands the ordinary layout, but the first surplus position is Aura-classified even when the patch contains no `AdditionalBaseModTypes`. This is a client layout/classification rule, not residue from the removed Jade patch.

## Third Arcane feasibility boundary

No third-Arcane test has yet been performed. Current evidence shows why `ArtifactSlots` is the wrong lever:

- the client exports only `WF_ARCANE_SLOT_0` and `WF_ARCANE_SLOT_1`;
- it exports only `UOT_ARCANE_UNLOCK_0`, `_1`, and `_ALL`;
- OpenWF defines only `ARCANE_SLOT` and `SECOND_ARCANE_SLOT` feature bits;
- `DiegeticUpgradeCards.lua` explicitly caps its Arcane controller at `Slots = 2` and checks only the two exported feature constants;
- the controller's `Arcanes` table and save loop are dynamic over `1..Slots`, which makes a real three-slot extension plausible but not metadata-only.

A legitimate proof of concept must extend the Arsenal UI/controller to three slots, create a third movie-clip target, then observe whether the existing dynamic payload is accepted, persisted, returned, and applied by OpenWF and the mission loadout. Guessing bit `128` would not constitute evidence because no corresponding client enum consumer exists.

## Arcane boundary

OpenWF's equipment feature model contains two Arcane bits: `ARCANE_SLOT` and `SECOND_ARCANE_SLOT`. Its "unlock Arcanes everywhere" behavior sets both bits. No third Arcane feature bit is defined. Consequently, adding a third Arcane is a different project from extending `ArtifactSlots`: it likely needs a client UI change plus compatible loadout serialization and server validation, and must not be presented as a metadata-only preset until those layers are proven.

## Experiment artifact

The generated offline bundle lives under:

`work/staging/metadata-editor/octavia-slot-compatibility-test-2026-08-25/`

It contains the exact patch and a test/rollback manifest. Do not treat the patch as a permanent balance change until every hypothesis has a recorded live result.
