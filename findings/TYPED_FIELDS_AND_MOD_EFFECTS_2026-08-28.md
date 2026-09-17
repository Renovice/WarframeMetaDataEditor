# Typed fields and reusable native mod effects — 2026-08-28

## Scope

This change replaces raw string editing wherever the decoded cache proves a
stronger field type, repairs the category selector's platform-theme leak, and
adds a general native `Upgrades` entry composer for mods. The mechanism is
cache-driven and does not special-case one selected mod in the decoder.

## H1 — enum choices need to be hard-coded per script or item

**Result: FALSE.** A read-only pass over all decoded own-text indexes uppercase
enum tokens by their real field key. The editor narrows them by the current
prefix family (`QA_`, `AP_`, `DT_`, `AVATAR_`, `WEAPON_`, and so on). Known
semantic fields add small validated lists where appropriate. On the current
cache, the live verifier finds all five `FusionLimit` `QA_*` values and both
`AVATAR_ABILITY_RANGE` and `AVATAR_DAMAGE_TAKEN` in the global `UpgradeType`
family.

Numerical values use invariant-culture decimal stepping. Integer values step by
1; decimal values retain a natural precision step up to four decimal places.
Only explicit known boolean keys render as toggles; an arbitrary numerical 0 or
1 is not guessed to be a boolean.

## H2 — the bright category highlight is caused by bad color constants

**Result: FALSE.** WPF's native `MenuItem` control template continued to use
platform selection chrome after foreground/background setters were applied. The
selector now uses a bounded dark popup and hierarchical `TreeView` with explicit
active and inactive selection brushes. This removes the native white surface
instead of trying another color on the same leaking template.

## H3 — Stretch can carry Fire resistance as ordinary metadata

**Result: TRUE, structurally.** Current-cache evidence:

```text
Stretch
  UpgradeType=AVATAR_ABILITY_RANGE
  OperationType=STACKING_MULTIPLY
  DamageType=DT_ANY

Flame Repellent
  UpgradeType=AVATAR_DAMAGE_TAKEN
  OperationType=MULTIPLY
  Value=0.89999998
  DamageType=DT_FIRE
```

The composer preserves Stretch's complete existing `Upgrades` collection and
appends the verified Flame Repellent entry. It emits a normal top-level
collection override. The same dialog supports custom decoded upgrade types,
operations, damage types, values, and localization keys.

## H4 — the new effect is guaranteed to rewrite the card description

**Result: NOT YET PROVEN IN GAME.** The original hypothesis that descriptions
were one fixed top-level string was incomplete. `LocalizeDescTag` is empty on
both inspected mods; each `Upgrades` entry carries its own `LocTag`. Current
multi-effect evidence confirms this design: Superior Defenses contains two
upgrade entries and two separate description localization keys. Therefore the
Fire-resistance preset can carry Flame Repellent's verified per-effect key,
which is structurally expected to add a visible line. The UI exposes this as an
explicit checkbox: including the description reuses that translated key;
excluding it emits `OverrideLocalization=0` and an empty `LocTag` while keeping
the gameplay effect.

That newly combined card has not yet been rendered in game. The editor labels
this boundary explicitly. A custom effect with no valid localization key may
work mechanically without a new description line.

## Safety rules

- Existing upgrade entries are preserved before compaction; one new validated
  native entry is appended.
- The editor rejects malformed enum tokens, unknown operations/damage types,
  non-numeric values, and non-absolute localization keys.
- A full added-effect list cannot be saved alongside separately changed nested
  rows for the same `Upgrades` collection; that ambiguity is refused.
- No Lua hook, per-frame runner, inventory edit, or server-definition mutation
  is introduced by the effect composer.

## Verification

- Release WPF build with warnings as errors: 0 warnings, 0 errors.
- Deterministic core/dev suite: 74 passed, including original-entry preservation,
  exact Fire-resistance shape, optional localization, and malformed-token refusal.
- Read-only current-cache verifier: 90 distinct status handlers, 2,278 mods across eight
  mod categories, complete `QA_*` discovery, verified current Flame Repellent
  shape, and structurally valid Stretch-plus-resistance composition.
