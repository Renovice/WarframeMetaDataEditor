#!/usr/bin/env python3
"""Physical Riven trait eligibility (Impact/Puncture/Slash) from real Rivens listed on warframe.market.

Subcommands:
  build    fetch (cached, rate-limited, read-only GETs) and write riven_market_evidence.json
  compare  compare an evidence file with PE+ riven_unrollables (+ errata) and decoded rules; prints Markdown

Decision per (weapon, trait), after placeholder/typo cleaning:
  n    = cleaned listings for the weapon (warframe.market returns at most 500 newest per weapon)
  seen = cleaned listings carrying the trait as + or -
  n >= --min-n and seen == 0   -> unrollable
  seen >= --min-seen           -> rollable
  n >= --min-n and P(X <= seen | n, rate) < --typo-p -> unrollable (1-2 sightings are typos/legacy Rivens;
                                  rate = the weapon's own rollable-physical listing rate, else --typical-rate)
  otherwise                    -> unknown (omitted)
"""
import argparse, datetime as dt, json, os, re, sys, time, urllib.parse, urllib.request

API_V1 = "https://api.warframe.market/v1"
API_V2 = "https://api.warframe.market/v2"
PHYSICAL = {  # warframe.market attribute slug -> game UpgradeEntries tag
    "impact_damage": "WeaponImpactDamageMod",
    "puncture_damage": "WeaponArmorPiercingDamageMod",
    "slash_damage": "WeaponSlashDamageMod",
}
UA = {"User-Agent": "RENOVICE riven-market-evidence (read-only research)", "Accept": "application/json"}


def now_iso():
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat()


def http_json(url, retries, backoff):
    last = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=30) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            last = e
            if e.code not in (429, 500, 502, 503, 504):
                break
        except Exception as e:  # network hiccup
            last = e
        wait = backoff * (attempt + 1)
        print(f"  retry {attempt + 1}/{retries} in {wait}s: {str(last)[:80]}", file=sys.stderr, flush=True)
        time.sleep(wait)
    raise RuntimeError(f"GET failed: {url}: {last}")


def cached(path, max_age_days, fetch):
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            d = json.load(f)
        age = dt.datetime.now(dt.timezone.utc) - dt.datetime.fromisoformat(d["fetchedAt"])
        if max_age_days is None or age.total_seconds() <= max_age_days * 86400:
            return d, False
    d = fetch()
    d["fetchedAt"] = now_iso()
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(d, f)
    os.replace(tmp, path)
    return d, True


def riven_weapons(export_path):
    with open(export_path, encoding="utf-8") as f:
        rc = json.load(f)["rivenContracts"]
    out = {}
    for riven_type, c in rc.items():
        for key in ("compatibleItems", "sentinelCompatibleItems"):
            for w in c.get(key, []):
                out.setdefault(w, set()).add(riven_type.rsplit("/", 1)[-1])
    return {w: sorted(t) for w, t in sorted(out.items())}


def map_slugs(weapons, market_weapons, pe_plus):
    """Exact gameRef first; then exact English name via PE+ ExportWeapons + dict.en (e.g. Plague Zaw strikes,
    Vinquibus (Melee)). Returns ({path: slug}, {path: reason})."""
    by_ref = {m["gameRef"]: m["slug"] for m in market_weapons}
    by_name = {}
    for m in market_weapons:
        by_name.setdefault(m["i18n"]["en"]["name"].strip().lower(), []).append(m["slug"])
    ew = dic = None
    if pe_plus:
        with open(os.path.join(pe_plus, "ExportWeapons.json"), encoding="utf-8") as f:
            ew = json.load(f)
        with open(os.path.join(pe_plus, "dict.en.json"), encoding="utf-8") as f:
            dic = json.load(f)
    slugs, unmapped = {}, {}
    for w in weapons:
        if w in by_ref:
            slugs[w] = by_ref[w]; continue
        name = None
        if ew is not None and w in ew:
            key = ew[w].get("name")
            name = dic.get(key, key) if key else None
        cands = by_name.get(name.strip().lower(), []) if name else []
        if len(cands) == 1:
            slugs[w] = cands[0]
            continue
        # DE files some Rivens under a shared parent type (e.g. QuadShotgunBase = Hek/Vaykor Hek/Kuva Hek,
        # DarkDaggerBase = Dark Dagger/Rakta Dark Dagger): the market lists them under a child weapon.
        child_slugs = sorted({by_ref[c] for c, v in (ew or {}).items() if v.get("parentName") == w and c in by_ref})
        if len(child_slugs) == 1:
            slugs[w] = child_slugs[0]
        else:
            unmapped[w] = ("no warframe.market weapon with this gameRef" +
                           (f"; name '{name}' matched {len(cands)} slugs" if name else "; not in PE+ ExportWeapons (no name)"))
    return slugs, unmapped


def binom_cdf(k, n, p):
    """P(X <= k) for X ~ Binomial(n, p)."""
    import math
    return sum(math.comb(n, i) * p ** i * (1 - p) ** (n - i) for i in range(k + 1))


def clean(listings, percent_attrs):
    """Drop placeholder/typo listings: any percent-unit attribute with |value| <= 1.0 (e.g. 'impact 1.0, puncture 1.0')."""
    keep = []
    for x in listings:
        if any(a[0] in percent_attrs and abs(a[1]) <= 1.0 for a in x["attrs"]):
            continue
        keep.append(x)
    return keep


def cmd_build(a):
    os.makedirs(a.cache, exist_ok=True)
    weapons = riven_weapons(a.export)
    fetch_v2 = lambda ep: (lambda: {"data": http_json(f"{API_V2}/{ep}", a.retries, a.backoff)["data"]})
    mw, _ = cached(os.path.join(a.cache, "_market_weapons.json"), a.max_age_days, fetch_v2("riven/weapons"))
    ma, _ = cached(os.path.join(a.cache, "_market_attributes.json"), a.max_age_days, fetch_v2("riven/attributes"))
    percent_attrs = {x["slug"] for x in ma["data"] if x.get("unit") == "percent"}
    for s, tag in PHYSICAL.items():  # guard against upstream renames
        ref = next((x.get("gameRef") for x in ma["data"] if x["slug"] == s), None)
        if ref != tag:
            sys.exit(f"warframe.market attribute {s} maps to {ref}, expected {tag}")
    slugs, unmapped = map_slugs(weapons, mw["data"], a.pe_plus)
    print(f"weapons {len(weapons)}, mapped {len(slugs)}, unmapped {len(unmapped)}", flush=True)
    result, failed = {}, {}
    for i, (w, slug) in enumerate(sorted(slugs.items())):
        path = os.path.join(a.cache, re.sub(r"[^a-z0-9_]+", "_", slug) + ".json")

        def fetch(slug=slug):
            url = f"{API_V1}/auctions/search?" + urllib.parse.urlencode({"type": "riven", "weapon_url_name": slug})
            d = http_json(url, a.retries, a.backoff)
            time.sleep(a.delay)
            return {"slug": slug, "url": url, "listings": [
                {"id": x["id"], "created": x["created"][:10],
                 "attrs": [[t["url_name"], t["value"], t["positive"]] for t in x["item"]["attributes"]]}
                for x in d["payload"]["auctions"]]}
        try:
            d, fetched = cached(path, a.max_age_days, fetch)
        except RuntimeError as e:
            failed[w] = str(e); print(f"[{i + 1}/{len(slugs)}] {slug}: FAILED", flush=True); continue
        lst = clean(d["listings"], percent_attrs)
        n = len(lst)
        rec = {"n": n, "rollable": [], "unrollable": []}
        seen_by = {tag: sum(1 for x in lst if any(t[0] == attr for t in x["attrs"])) for attr, tag in PHYSICAL.items()}
        own = [s / n for s in seen_by.values() if n and s >= a.min_seen]
        rate = sum(own) / len(own) if own else a.typical_rate  # how often a rollable physical trait is listed
        for tag, seen in seen_by.items():
            if seen >= a.min_seen:
                rec["rollable"].append(tag)
            elif n >= a.min_n and seen == 0:
                rec["unrollable"].append(tag)
            elif n >= a.min_n and binom_cdf(seen, n, rate) < a.typo_p:
                # 1-2 sightings where a rollable trait would appear far more often: seller typos/legacy.
                rec["unrollable"].append(tag)
        rec["slug"] = slug
        rec["fetchedAt"] = d["fetchedAt"]
        result[w] = rec
        print(f"[{i + 1}/{len(slugs)}] {slug}: n={n} {'fetched' if fetched else 'cache'}", flush=True)
    out = {
        "generatedAt": now_iso(),
        "rule": {"minListingsForUnrollable": a.min_n, "minSeenForRollable": a.min_seen,
                 "unrollable": "n >= minListingsForUnrollable and (seen == 0 or P(X <= seen | n, rate) < typoP)",
                 "rate": "mean listing rate of the weapon's own rollable physical traits, else typicalRate",
                 "typicalRate": a.typical_rate, "typoP": a.typo_p,
                 "rollable": "seen >= minSeenForRollable", "otherwise": "unknown (omitted)",
                 "cleaning": "drop listings with any percent-unit attribute |value| <= 1.0",
                 "seen": "cleaned listings carrying the trait as positive or negative",
                 "source": f"{API_V1}/auctions/search?type=riven&weapon_url_name=<slug> (max 500 newest listings)"},
        "export": os.path.abspath(a.export),
        "unmapped": unmapped,
        "failed": failed,
        "weapons": result,
    }
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)
    cov = sum(1 for r in result.values() if r["n"] >= a.min_n)
    print(f"wrote {a.out}: {len(result)} weapons, n>={a.min_n}: {cov}, unmapped {len(unmapped)}, failed {len(failed)}")


def cmd_compare(a):
    with open(a.evidence, encoding="utf-8") as f:
        ev = json.load(f)
    weapons = riven_weapons(a.export)
    unroll = {}
    if a.pe_plus:
        with open(os.path.join(a.pe_plus, "supplementals", "riven_unrollables.json"), encoding="utf-8") as f:
            unroll = json.load(f)
    errata = set(a.errata or [])
    with open(a.export, encoding="utf-8") as f:
        phys_rules = json.load(f).get("rivenPhysicalRules", {})
    decoded = {}
    if a.decoded:
        with open(a.decoded, encoding="utf-8") as f:
            decoded = {w: set(o["unrollablePhysical"]) for w, o in json.load(f).items() if "unrollablePhysical" in o}
    tags = list(PHYSICAL.values())
    short = lambda t: t.replace("Weapon", "").replace("DamageMod", "").replace("ArmorPiercing", "Puncture")

    def pe_view(w):  # server order: errata -> riven_unrollables -> rivenPhysicalRules -> unrestricted
        if w in errata: return set(), "errata"
        if w in unroll: return set(unroll[w]), "riven_unrollables"
        if w in phys_rules: return set(phys_rules[w]), "rivenPhysicalRules"
        return set(), "absent (unrestricted)"

    rows_pe, rows_dec = [], []
    stats = {"decided": 0, "rollable": 0, "unrollable": 0}
    for w, r in ev["weapons"].items():
        for t in tags:
            m = "rollable" if t in r["rollable"] else "unrollable" if t in r["unrollable"] else None
            if not m: continue
            stats["decided"] += 1; stats[m] += 1
            pu, src = pe_view(w)
            if (t in pu) != (m == "unrollable"):
                rows_pe.append((w, r.get("slug"), t, m, src, r["n"]))
            if w in decoded and (t in decoded[w]) != (m == "unrollable"):
                rows_dec.append((w, r.get("slug"), t, m, r["n"]))
    cov = sum(1 for r in ev["weapons"].values() if r["n"] >= ev["rule"]["minListingsForUnrollable"])
    full = sum(1 for r in ev["weapons"].values() if len(r["rollable"]) + len(r["unrollable"]) == 3)
    p = print
    p(f"# Market evidence comparison\n\nEvidence: generated {ev['generatedAt']}; rule {json.dumps({k: ev['rule'][k] for k in ('minListingsForUnrollable', 'minSeenForRollable')})}\n")
    p(f"- Riven-compatible weapons: {len(weapons)}; mapped and fetched: {len(ev['weapons'])}; unmapped: {len(ev['unmapped'])}; failed: {len(ev['failed'])}")
    p(f"- Weapons with n >= {ev['rule']['minListingsForUnrollable']}: {cov}; weapons with all 3 physical traits decided: {full}")
    p(f"- Decided (weapon, trait) pairs: {stats['decided']} (rollable {stats['rollable']}, unrollable {stats['unrollable']})\n")
    if ev["unmapped"]:
        p("## Unmapped weapons\n"); [p(f"- `{w}`: {why}") for w, why in ev["unmapped"].items()]; p()
    p(f"## Market vs server source (errata -> PE+ riven_unrollables -> rivenPhysicalRules -> unrestricted): {len(rows_pe)} contradictions\n")
    p("| weapon | slug | trait | market | server source says | n |\n|---|---|---|---|---|---|")
    for w, s, t, m, src, n in sorted(rows_pe, key=lambda x: (x[4], x[1] or "")):
        p(f"| `{w.rsplit('/', 1)[-1]}` | {s} | {short(t)} | {m} | {'unrollable' if m == 'rollable' else 'rollable'} ({src}) | {n} |")
    if decoded:
        p(f"\n## Market vs decoded rule (weapon_rule.json): {len(rows_dec)} contradictions\n")
        p("| weapon | slug | trait | market | decoded says | n |\n|---|---|---|---|---|---|")
        for w, s, t, m, n in sorted(rows_dec, key=lambda x: x[1] or ""):
            p(f"| `{w.rsplit('/', 1)[-1]}` | {s} | {short(t)} | {m} | {'unrollable' if m == 'rollable' else 'rollable'} | {n} |")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sp = ap.add_subparsers(dest="cmd", required=True)
    b = sp.add_parser("build", help="fetch listings and write the evidence JSON")
    b.add_argument("--export", required=True, help="SpaceNinjaServer static/generated/openwf-metadata/current-public-export.json")
    b.add_argument("--cache", required=True, help="cache directory for raw listings (reused between runs)")
    b.add_argument("--out", required=True, help="output riven_market_evidence.json")
    b.add_argument("--pe-plus", help="node_modules/warframe-public-export-plus dir (name-based slug fallback)")
    b.add_argument("--max-age-days", type=float, default=14, help="refetch cache entries older than this (default 14)")
    b.add_argument("--delay", type=float, default=4.0, help="seconds between fetches (default 4)")
    b.add_argument("--retries", type=int, default=6)
    b.add_argument("--backoff", type=float, default=30.0, help="seconds x attempt on 429/5xx (default 30)")
    b.add_argument("--min-n", type=int, default=100)
    b.add_argument("--typical-rate", type=float, default=0.121,
                   help="listing rate of a rollable physical trait when the weapon has none to measure (default 0.121)")
    b.add_argument("--typo-p", type=float, default=0.01,
                   help="treat 1-2 sightings as typos when that few is this unlikely for a rollable trait (default 0.01)")
    b.add_argument("--min-seen", type=int, default=3)
    c = sp.add_parser("compare", help="compare evidence with PE+ riven_unrollables/errata/decoded rules (Markdown to stdout)")
    c.add_argument("--evidence", required=True)
    c.add_argument("--export", required=True)
    c.add_argument("--pe-plus", help="node_modules/warframe-public-export-plus dir (for supplementals/riven_unrollables.json)")
    c.add_argument("--errata", nargs="*", default=[], help="weapon paths the server treats as unrestricted despite riven_unrollables")
    c.add_argument("--decoded", help="weapon_rule.json from tools/riven-physical-rules")
    a = ap.parse_args()
    {"build": cmd_build, "compare": cmd_compare}[a.cmd](a)


if __name__ == "__main__":
    main()
