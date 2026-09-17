# Status-effect and weapon-damage metadata editor — 2026-08-28

## Scope

This work adds a general, cache-driven editing layer for native status handlers
and weapon attack profiles. It does not hard-code one weapon or one status. The
editor derives every visible scalar from the selected type's composed live-cache
metadata and retains its exact structural path for patch generation.

## H1 — ordinary enemies need separate per-enemy Heat patches

**Result: FALSE.** Ordinary ground-enemy controllers use the normal status
handler inheritance family. A change to the appropriate base handler is inherited
unless a child handler overrides that field.

An enemy that intentionally rejects Heat is outside this handler path because
there is no accepted proc to modify. Railjack/space damage uses separate handler
types and is displayed with a `Railjack / Space` scope instead of being silently
mixed with ground status behavior.

## H2 — status behavior is editable as metadata

**Result: TRUE, for reflected metadata switches and values.** The new
`Status Effects` category contains 90 distinct handler types in the 2026-08-28 live cache.
The guided editor exposes existing scalar fields including duration, stacking
rules, maximum stacks, DOT/radial type and amount, native stack modifiers, and
the `UseHighestDamageOverTime` engine switch.

Vanilla `BaseFireDamageProc` does not serialize
`UseHighestDamageOverTime`; absence selects the engine default. Because exact
native analysis already proved this reflected switch, the editor offers it as an
optional top-level field only on handlers that actually contain a
`DamageOverTime` block. Leaving it as `(absent)` emits nothing. Changing it to
`1` adds the field reversibly.

The switch is now described narrowly in the UI: it chooses the higher incoming
basis only where the native handler already matches or consolidates DOT damage.
It does not merge or replace independent per-hit DOT instances such as stock
Toxin. Stack storage (`StackStyle`) and DOT consolidation are displayed as
separate controls so the editor does not claim one switch changes all three.

The 2026-08-28 cache contains 90 distinct status handlers, but only 22 contain a
`DamageOverTime` block and therefore meaningfully expose this switch. Those 22
span all four target scopes: four ordinary-ground handlers, thirteen specialized
enemy overrides, four Player/Tenno handlers, and one Railjack/space handler.
Only `KahlFireDamageProc` serializes the switch explicitly in vanilla; the
editor offers the same proven optional field on the other 21 DOT handlers. A
non-DOT status does not show it because there is no consolidated DOT basis to
choose.

The UI now states the scope persistently: `Enemy Base` is the broad ordinary
enemy default; faction/special entries are narrow child overrides, not every
member of that faction; Player/Tenno is the player status-receiver branch; and
Railjack/space is a separate combat system.

The previous broad display labels also caused an unintended catalog collision:
Capture Target Heat and Teshin Heat both appeared as `Heat — Grineer`, so the
normal same-name deduplicator retained only one. Target-aware labels such as
`Heat — Capture Target · Grineer override` and
`Heat — Teshin · Grineer override` preserve all distinct handlers. This is why
the corrected catalog count increased from 86 to 90 without inventing any game
types.

## H3 — one flat weapon regex is sufficient for every fire mode

**Result: FALSE.** Weapons can contain several anonymous `Behaviors` entries,
and each can contain direct, alternate, radial, or DOT attack data. A flat anchor
can hit the wrong repeated `Amount`, `Type`, or `ProcChance` field.

The editor now parses named objects and anonymous arrays into exact structural
paths such as:

```text
Behaviors.0.impact:LotusWeaponImpactBehavior.AttackData.Amount
Behaviors.1.impact:LotusWeaponImpactBehavior.AttackData.Type
```

The shipped bootstrapper parses metadata with `EeNotationParser`, then resolves
`q|` paths through generic `JsonObject::queryUp` and `JsonArray::queryUp` code.
Array indices are therefore a general part of the patch runtime, not a
weapon-specific workaround.

## H4 — query assignment can safely replace every metadata node

**Result: FALSE.** The bootstrapper's current query-assignment writer supports
string and numeric scalar nodes. It does not replace array/object nodes. The
guided editors consequently exclude a collection node such as the complete
`ForcedProcs={...}` set instead of generating a patch that would throw or silently
misapply. Existing entries inside simple collections are expanded to scalar paths,
so `ForcedProcs.0` or `ValueRange.1` can be edited safely without changing the
collection's shape. Adding/removing entries still requires a future whole-block
replacement operation with its own round-trip validation.

## H5 — `StackedUpgrades.*.DamageType=DT_ANY` means Cold has no elemental identity

**Result: FALSE.** The selected handler class/path defines which status is being
edited. In the live Player/Tenno Cold handler, the nested `DamageType` belongs to
an upgrade entry such as `AVATAR_CRIT_DAMAGE_VULNERABILITY`; it is the filter for
what incoming attack families receive that vulnerability. `DT_ANY` means all
damage types qualify for the nested upgrade. It does not turn Cold into an
any-element proc.

The guided UI now distinguishes these concepts globally:

- `DamageOverTime.Type` is labelled **DOT damage type**;
- `AttackData.Type` is labelled **Attack damage type**;
- `RadialDamage.Type` is labelled **Radial damage type**;
- a `DamageType` inside an `*Upgrades` path is labelled **Upgrade damage filter**.

This is path-driven interpretation of the verified structural IR, not a
Cold-specific metadata exception.

## H6 — the empty Player/Tenno Corrosive panel indicates a decode failure

**Result: FALSE.** A read-only probe of the current cache parsed
`/Lotus/Types/Player/InjuryHandlers/TennoCorrosiveDamageProc` with 16 guided
fields, including `StackedUpgrades.0.DamageType`. Ground, specialized, VIP, and
Player/Tenno Corrosive handlers all parsed successfully. The failure was in UI
selection identity: the list retained only display strings, cleared the current
view, and then performed a second name lookup. The list now retains exact
`CatalogItem` objects and batch mode targets their exact paths.

## H7 — every named Heat/Cold variant must receive a duplicate patch

**Result: FALSE.** Current-cache own-text and parent-chain inspection separates
inherited gameplay from deliberately owned overrides:

- `TriangleFreezeDamageProc -> CorpusFreezeDamageProc -> BaseFreezeDamageProc`;
  the Corpus intermediary owns nothing and Triangle owns only model-specific
  `ProcEffects`, so Base Cold gameplay changes flow through automatically.
- `TriangleFireDamageProc -> CorpusFireDamageProc -> BaseFireDamageProc`;
  the same rule applies to Base Heat gameplay changes.
- `NokkoVIPFireDamageProc -> BaseFireDamageProc`; Nokko owns only
  `ProcInjuryType=ANY`, so Base Heat DOT, armor reduction, duration, and
  consolidation remain inherited.
- `NokkoVIPFreezeDamageProc -> BaseFreezeDamageProc`; Nokko deliberately owns
  `MaxStacks=3`, `Duration=5`, `ReworkMaxStacks=3`, and `PostFrozenStacks=2`.
  Those owned Cold values do not follow a Base edit, while the remaining Cold
  behavior still inherits.

No automatic Triangle/Nokko synchronization or duplicate patch generation is
needed for a Base Heat edit. Those children already receive the change through
normal inheritance. Copying the full composed handler into every child would
freeze inherited defaults and destroy useful inheritance.

`Triangle*DamageProc` is referenced by the Man-in-the-Wall Triangle/Slinky
damage controllers and supplies rig-specific effects attached to
`GAME_C1_HEAD1`; it is a special-unit override, not a second global elemental
rule. `NokkoVIP*DamageProc` is referenced by Nokko Colony VIP Arachnoid, Plant,
and Corpus damage controllers; it is likewise a special VIP override. Both are
now presented under Specialized Enemy Overrides with those consumer names.

## H8 — Cold's handler cannot deserialize a `DamageOverTime` payload

**Result: FALSE; STATIC SUPPORT VERIFIED, LIVE DAMAGE QA PENDING.** Exact-build
native type registration identifies `LotusFreezeHandler` at RVA `0xA0DF0` with
constructor `0x1C0B7E0`. The extracted reflection schema for that concrete class
contains the complete common damage-proc fields `DamageOverTime`,
`DOTPercentOfBaseDamage`, `ConsolidateDamageOverTime`, and
`UseHighestDamageOverTime`. They occupy the same inherited slots as the fields
on `LotusFireDamageProc`. Cold therefore can deserialize the native common DOT
payload; its stock metadata simply does not populate one.

The editor now has a dedicated **Add/Edit DOT** builder. It preserves existing
DOT blocks and can create the common native payload for Cold or another status:
damage type, fraction of the triggering hit, observed consolidation mode, and
the optional highest-damage-basis switch. It does not use a Lua polling loop.
Newly converting a non-DOT status remains marked as requiring an in-game damage
test because static schema and dispatch evidence cannot substitute for live
damage acceptance on every target class.

## H9 — Void and Tau are absent from the cache

**Result: FALSE.** Their internal handler names differ from the player-facing
names. Void is the `Radiant` family (`BaseEnemyRadiantDamageProc`) and its weapon
component is `DT_RADIANT`. Tau is the `Sentient` family
(`BaseSentientDamageProc`) and its component is `DT_SENTIENT`. The editor now
displays these as **Void** and **Tau**, while retaining the internal identifiers
in Advanced paths and generated patches. `DT_VOID` was not found as a live
weapon damage-component field and is therefore not emitted by the composer.

## H10 — adding a new weapon element can use a nested `q|` assignment

**Result: FALSE.** The bootstrapper resolves an existing query node before it
assigns a value; it cannot create an absent nested `DT_*` key. The cache supports
multi-element profiles natively, but there are two encodings:

- `UseNewFormat=0`: component values are fractions multiplied by `Amount`;
- `UseNewFormat=1`: component values are absolute damage and sum to `Amount`.

The weapon composer presents both as absolute damage. On the first real change,
it emits `UseNewFormat=1`, all supported component fields, and the recalculated
total inside a fully parsed and reserialized copy of the containing top-level
block. Unrelated crit, status, forced-proc, animation, and fire-mode fields are
preserved. Whole-block and old nested edits are conflict-checked, and composed
weapon edits are refused in batch mode.

## H11 — the Status Behavior crash is caused by a bad handler

**Result: FALSE.** A WPF runtime harness reproduced the error before metadata
selection completed. `DataGrid.RowHeight` is a `double`; `RowHeight="Auto"`
compiled but threw `XamlParseException` when the window opened. Removing that
invalid value fixed the dialog. The regression harness now opens Viral, renders
all fields, toggles Advanced paths, and closes without an unhandled exception.

## H12 — fire rate is unavailable because it is not decoded metadata

**Result: FALSE.** The decoded firing-state objects expose scalar `fireRate`
values inside `state:*Behavior` records. The field is stored as rounds per minute;
for example, the deterministic fixture's `fireRate=300` is 5 shots per second.
The composer now associates each firing state with its exact surrounding behavior
index, exposes RPM plus the `RPM / 60` conversion, and keeps reload, charge, and
burst timing state-specific.

Changing 300 to 360 produced 6 shots per second and emitted `fireRate=360` inside
the same preserved `Behaviors` block as the selected damage profile. A read-only
current-cache probe found two firing states on Aeolak and round-tripped a changed
rate alongside Magnetic plus Viral without discarding unrelated metadata. This is
generic firing-state traversal, not an Aeolak or weapon-name special case.

## H13 — weapon status chance is one weapon-wide field

**Result: FALSE.** Normal status chance is the `ProcChance` scalar owned by each
exact `AttackData`, `AlternateAttackData`, radial, or DOT attack profile. The
visual editor now shows that selected profile's chance as a percentage and writes
the converted decimal back to the same parsed node. Forced proc collections are
separate and remain preserved.

A deterministic status-only change from 25% to 40% emitted `ProcChance=0.4`
while retaining the original `DT_IMPACT=0.6`, `Amount=100`, and legacy damage
encoding; it did not add `UseNewFormat=1`. Non-negative values above 100% are
accepted rather than capped artificially, while negative and malformed values
are refused. The current-cache Aeolak probe independently found and changed a
profile-owned status chance alongside its two damage profiles and two firing
states.

## H14 — Cold `MaxStacks=9` means only nine total Cold procs

**Result: FALSE for the visual/gameplay interpretation.** The ordinary Cold
handler combines one initial application with nine accepted repeated stacks.
Its visual cap is therefore ten total procs. `RepeatFreezeModifier=0.05` adds a
5% freeze/slow modifier for each of those repeated stacks. The handler does not
expose a separate editable initial slow in this record, so the editor does not
invent one and does not claim that full freeze occurs because an exposed sum
reaches 100%. It identifies reaching the stack cap as the full-freeze trigger.

The same presentation pipeline successfully built metrics and effect chains for
all 90 status records in the current cache. Base Impact, Puncture, Slash, Heat,
Cold, Electricity, Toxin, Blast, Radiation, Gas, Magnetic, Viral, Corrosive,
Void, and Tau families receive explicit scalable vector iconography; specialized
handlers retain their exact inherited/override scope.

## UI changes

- a restrained semantic palette distinguishes cache/general, gameplay, file,
  equipment, and server actions without returning to unrelated arbitrary colors;
- a wider category panel and grouped field/preview workspace occupy the main row;
- field details, actions, and patch controls occupy a bottom strip instead of
  compressing the field grid in a permanent right sidebar;
- rows are sorted into meaningful sections;
- status handlers are named by status plus scope;
- the guided status window starts with a plain-language behavior, DOT, duration,
  stack, and inheritance summary;
- each status is presented as an ordered effect chain; Cold, for example, is
  split into Slow, Freeze buildup, Critical vulnerability, lifetime/stacks, and
  its independent DOT payload state;
- the status window now uses the same visual task shell as the weapon editor:
  element-colored metric cards, distinct code-native vector icons, clickable
  effect cards, group-specific clean forms, and a separate raw Advanced page;
- Cold shows ten total procs to full freeze, five-percent repeated buildup, and
  the separate Overguard cap instead of leaving `MaxStacks=9` unexplained;
- Cold-specific fields such as the Overguard stack cap, repeated-freeze modifier,
  and frozen-state upgrade block are exposed instead of being hidden as generic internals;
- a dedicated status DOT builder can edit an existing payload or create the
  reflected native payload for a status that ships without one;
- every editable status value shows its explanation in the same row;
- exact metadata paths and patch operations are hidden by default but remain
  available through an explicit Advanced toggle;
- known status enums use dropdowns and numerical values retain keyboard stepping;
  mouse step buttons appear only for the active or hovered value;
- the dedicated weapon composer groups exact direct, alternate, radial, and DOT
  profiles, displays absolute damage, and supports multiple simultaneous elements;
- the weapon window is now a visual, task-based editor with clickable Overview
  stat cards, active element chips, Damage & Elements, per-state Fire Controls,
  and an Advanced metadata view;
- profile-owned Status & Procs displays `ProcChance` as a percentage, converts it
  to the native decimal, and keeps status-only edits isolated from damage encoding;
- `fireRate` is editable as native RPM with a live shots-per-second conversion;
  reload, charge, and burst values remain attached to the exact firing state;
- status effect-chain cards and section icons are clickable and take the user to
  the settings responsible for that effect;
- a minimum explanation-column width prevents the plain-language help from
  collapsing into unreadable vertical text;
- task-navigation, stat-card, effect-card, section, and explanation styles are
  shared application resources now used by weapon, guided-status, and DOT views;
- active and inactive selection colors are explicitly dark-themed, eliminating
  native white selection rows in the field grid and patch preview workflow;
- the toolbar is split into selection and action rows instead of one compressed strip;
- field descriptions and exact patch paths remain visible;
- invalid damage enums, invalid probabilities, malformed numeric values, and
  unsafe nested batch edits are refused before save.

## Verification gates

- WPF Release build with warnings as errors: **PASS, 0 warnings / 0 errors**.
- Deterministic Core self-test (90/90), including all-family visual status models,
  explicit Cold cap/buildup derivation, status effect-chain presentation,
  Cold DOT creation, two independent fire modes, forced-set
  parsing, per-state fire-rate round trips, non-merging Toxin help, absent native
  switch synthesis, validation, and Railjack separation:
  **PASS**.
- WPF runtime probes: **PASS** for category-tree child selection, status-dialog
  open/render/clickable effect filtering/Advanced/close, Cold DOT-builder
  defaults/rendering, and weapon-dialog Overview, absolute-component, element,
  and Fire Controls rendering. Visual PNG inspection passed after correcting two
  detected column-width regressions.
- Read-only current-cache decode: **PASS**, 462,759 types, 25,377 catalog items,
  52 finer categories after subdivision, with no duplicate-count increase.
- Current-cache status probe: **PASS**, all 90 distinct status handlers produced
  complete visual models; normal-enemy Heat exposed 23 editable fields and 22
  handlers across all scopes exposed the optional highest-DOT-basis control.
- Current-cache Player/Tenno Corrosive probe: **PASS**, 16 guided fields and the
  nested upgrade damage filter are independently addressable.
- Current-cache weapon probe: **PASS**, Aeolak exposed 21 independently
  addressable damage/status scalar fields across two damage profiles and two
  firing states; the whole-block composer emitted explicit Magnetic plus Viral
  fields and a changed native fire rate.
- Live in-game acceptance of a newly authored weapon patch: **pending user test**.
