# Reusable metadata option hover help — 2026-08-29

## Goal

Explain dropdown choices inside the editor without building one-off help for a
single DOT field or inventing meanings for hundreds of decoded native enums.

## H1 — status-specific tooltips are sufficient

**Result: FALSE.** The same problem affects operations, damage/proc types,
rarity and FusionLimit tiers, upgrade stats, and slot polarities. Help therefore
belongs to the shared metadata-value/dropdown component.

## H2 — every decoded enum can be safely translated into a gameplay claim

**Result: FALSE.** Known values use evidence-constrained labels and explanations.
Unknown values keep their exact native token, receive a mechanically friendly
label, and append the existing field description. The fallback never claims a
gameplay effect that was not verified.

## Implemented coverage

- status proc storage, DOT consolidation, and highest-basis modes;
- damage types and forced proc types;
- upgrade operation types and decoded upgrade-stat fallbacks;
- rarity and client FusionLimit tiers;
- mod-slot polarities;
- shared boolean, numeric, and text controls show their field explanation on hover;
- standalone DOT, mod-effect, mod-identity, and weapon damage-type dropdowns
  reuse the same option-help model.

## Verification gates

- **PASS:** deterministic Core suite, 93/93, including known-value and unknown-enum fallback assertions;
- **PASS:** visual status/DOT editor binding and option-description assertions;
- **PASS:** standalone mod-effect, rarity/fusion, weapon damage, and polarity selection tests;
- **PASS:** current cache, 90 status models plus 288 decoded UpgradeType values;
- **PASS:** Release build with 0 warnings and 0 errors;
- **PASS:** published executable launch smoke test and RAR integrity test.

Published executable SHA-256:
`5E6D8E2F24AE86587541189C3B25EE400A3C730A80602072B86584B10D095C5D`.

Published archive SHA-256:
`3E0D827CEF332B21357C6B5D16E46F3280620963D5D0F039FFEB88035EB0D524`.
