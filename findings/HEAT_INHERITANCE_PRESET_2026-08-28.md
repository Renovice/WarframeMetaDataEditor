# Heat inheritance metadata preset — 2026-08-28

> **Superseded on 2026-08-29.** This file preserves the earlier hypothesis for
> audit history, but its proposed `UseHighestDamageOverTime=1` preset does not
> provide per-source Heat damage ownership. Exact enum/branch review identified
> `StackStyle` as the relevant general metadata route. The editor no longer has
> a one-off Heat preset: use the universal per-status controls documented in
> `UNIVERSAL_STATUS_STACK_STORAGE_2026-08-29.md`.

## H1 — the editor should generate a Lua or native binary hook

**Result: FALSE.** Exact-build native analysis found the requested rule already
implemented behind the reflected boolean `UseHighestDamageOverTime`.

## H2 — enabling the field would stop new Heat stacks

**Result: FALSE.** The base proc merge routine uses the field only when choosing
whether to update the consolidated damage basis. The Fire subclass calls that
routine and then increments its separate stack counter. The patch does not set
or replace `ConsolidateDamageOverTime`.

## H3 — the patch belongs on `TennoFireDamageProc`

**Result: FALSE.** `TennoFireDamageProc` is used by player/Tenno damage
controllers and already sets the field to `1`. Ordinary enemy damage controllers
point to faction handlers inheriting `BaseFireDamageProc`, whose effective
native default is false. The preset therefore targets the enemy base record:

```text
/Lotus/Types/Enemies/BaseInjuryHandlers/BaseFireDamageProc
    UseHighestDamageOverTime=1
```

## Gates

- Core deterministic output test: PASS.
- Negative assertion that the patch does not write
  `ConsolidateDamageOverTime`: PASS.
- Metadata Editor self-test: incorporated into the broader status/weapon suite; all tests PASS.
- WPF Release build: 0 warnings, 0 errors.
- Static native/metadata ownership: PASS.
- Exact-build live damage acceptance: pending user gameplay test.
