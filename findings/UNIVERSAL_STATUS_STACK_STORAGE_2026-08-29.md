# Universal status proc storage — 2026-08-29

## Goal

Expose native proc storage, DOT consolidation, and highest-basis choices on
every status handler without adding element-specific preset buttons.

## H1 — `UseHighestDamageOverTime` provides independent source ownership

**Result: FALSE.** That switch participates in selecting a maximum damage basis
inside a compatible shared/consolidated DOT path. It does not turn one shared
Heat record into separate records for different hits or sources.

## H2 — the native `StackStyle` enum can express the requested behavior

**Result: TRUE.** Exact native enum/branch review identified three storage modes:

1. `OneActiveInstance` — one shared record;
2. `OneInstancePerInstigator` — one record for each source;
3. `OneInstancePerHit` — one independent record for every proc.

The editor therefore presents those three values plus **Original / inherited**.
The inherited choice is an editor sentinel and emits no metadata field.

## H3 — only Heat should expose these controls

**Result: FALSE.** `StackStyle`, `ConsolidateDamageOverTime`, and
`UseHighestDamageOverTime` belong to the common status-proc behavior. The guided
editor synthesizes reversible controls for every status handler, including
records that inherit or omit the fields and statuses without a DOT yet.

## H4 — a special Heat preset button is required

**Result: FALSE.** A user can select any element/status, configure its general
DOT payload and the three behavior controls, and save an ordinary patch. The
editor does not generate an element-specific preset or automatically choose a
storage mode. For example, choosing `OneInstancePerHit` on a selected handler
simply emits:

```text
/<the selected status-handler path>
    StackStyle=OneInstancePerHit
```

Other fields are emitted only when the user explicitly selects them. No patch
is automatically installed or deployed.

## Verification gates

- **PASS:** deterministic Core suite, 92/92, including universal optional-field synthesis and invalid-text rejection;
- **PASS:** all reflected storage, consolidation, and highest-basis values;
- **PASS:** current-cache validation, 90/90 catalogued status handlers across four receiver scopes;
- **PASS:** rendered WPF status, DOT, weapon, and category-tree smoke tests;
- **PASS:** Release build, 0 warnings and 0 errors;
- **PASS:** self-contained executable launch smoke test and RAR integrity test.

Published executable SHA-256:
`5E6D8E2F24AE86587541189C3B25EE400A3C730A80602072B86584B10D095C5D`.

Published archive SHA-256:
`3E0D827CEF332B21357C6B5D16E46F3280620963D5D0F039FFEB88035EB0D524`.
