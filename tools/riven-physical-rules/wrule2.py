"""Effective primary attack per Riven-compatible weapon, from decoded client types.
Step 1 (mode=list): emit projectile type paths that must be decoded (first fire behaviour's projectileType).
Step 2 (mode=eval): evaluate candidate rules against PE+ riven_unrollables and write weapon_rule.json."""
import json, os, re, sys, collections
H = os.path.dirname(os.path.abspath(__file__)); os.chdir(H)
S = r"C:/Users/Bartek/OneDrive/Dokumenter/OpenWF Server 23.09.2026/SpaceNinjaServer"
U = json.load(open(S + "/node_modules/warframe-public-export-plus/supplementals/riven_unrollables.json"))
EW = json.load(open(S + "/node_modules/warframe-public-export-plus/ExportWeapons.json", encoding='utf-8'))
rc = json.load(open(S + "/static/generated/openwf-metadata/current-public-export.json", encoding='utf-8'))['rivenContracts']
TAG = {'DT_IMPACT': 'WeaponImpactDamageMod', 'DT_PUNCTURE': 'WeaponArmorPiercingDamageMod', 'DT_SLASH': 'WeaponSlashDamageMod'}
PHYS = list(TAG)
compat = {}
for rt, c in rc.items():
    for w in c['compatibleItems'] + c.get('sentinelCompatibleItems', []): compat.setdefault(w, []).append(rt.split('/')[-1])
fn = lambda p, d='wdec': os.path.join(d, p.lstrip('/').replace('/', '__') + '.txt')

def load(p, d='wdec'):
    f = fn(p, d)
    if not os.path.exists(f): return None
    t = open(f, encoding='utf-8', errors='replace').read()
    return None if t.lstrip('\ufeff').startswith('[error]') else t.splitlines()

def inheritance(lines):
    out = []
    for l in lines[1:]:
        if l.startswith('['): break
        out.append(l.strip())
    return out

def attack_block(lines, start_pat, key='AttackData={'):
    """First `key` block at/after the first line matching start_pat (regex); returns dict of direct scalar children."""
    i0 = 0
    if start_pat:
        for i, l in enumerate(lines):
            if re.match(start_pat, l.strip()): i0 = i; break
        else: return None
    for i in range(i0, len(lines)):
        if lines[i].strip() == key:
            depth = 1; j = i + 1; ad = {}
            while j < len(lines) and depth:
                t = lines[j].strip()
                if t.endswith('={') or t == '{': depth += 1
                elif t in ('}', '},'): depth -= 1
                elif depth == 1:
                    m = re.match(r'^([A-Za-z_]+)=(.*)$', t)
                    if m: ad[m.group(1)] = m.group(2)
                j += 1
            return ad
    return None

def first_projectile(lines):
    """projectileType of the first fire behaviour (before the first impact behaviour)."""
    for l in lines:
        s = l.strip()
        if re.match(r'^impact[0-9]*:Type=', s): return None
        m = re.match(r'^projectileType=(.*)$', s)
        if m:
            v = m.group(1)
            return None if v in ('', '""') else v
    return None

def proj_candidates(w, lines, v):
    if v.startswith('/'): return [v]
    dirs = [w.rsplit('/', 1)[0]] + [a.rsplit('/', 1)[0] for a in inheritance(lines)[1:]]
    return [d + '/' + v for d in dict.fromkeys(dirs)]

mode = sys.argv[1] if len(sys.argv) > 1 else 'eval'
W = {}
need = []
for w in sorted(compat):
    lines = load(w)
    if lines is None: W[w] = dict(err='weapon decode missing'); continue
    pv = first_projectile(lines)
    rec = dict(impact=attack_block(lines, r'^impact[0-9]*:Type='), proj=pv, projPath=None, projAttack=None)
    if pv:
        for c in proj_candidates(w, lines, pv):
            pl = load(c, 'pdec')
            if pl is not None:
                rec['projPath'] = c; rec['projAttack'] = attack_block(pl, None); break
        else:
            for c in proj_candidates(w, lines, pv):
                if not os.path.exists(fn(c, 'pdec')):
                    need.append(c); break
    W[w] = rec
if mode == 'list':
    tried = set(os.listdir('pdec')) if os.path.isdir('pdec') else set()
    todo = [p for p in dict.fromkeys(need) if os.path.basename(fn(p, 'pdec')) not in tried]
    open('plist.txt', 'w').write('\n'.join(todo) + '\n'); print('projectile candidates to decode', len(todo)); sys.exit()

def eff(rec, use_proj=True):
    if use_proj and rec.get('projAttack'): return rec['projAttack'], 'projectile'
    return rec.get('impact'), 'impact'

def normalise(ad):
    if ad is None: return None
    ad = dict(ad)
    ad.setdefault('Type', 'DT_PHYSICAL')  # decoder omits default-valued fields
    if ad['Type'] == 'DT_PHYSICAL':
        present = {p: float(ad[p]) for p in PHYS if p in ad}
        miss = [p for p in PHYS if p not in ad]
        s = sum(present.values())
        if miss and s <= 1.0001:
            for p in miss: ad[p] = str((1 - s) / len(miss)); ad['_filled'] = ad.get('_filled', '') + p + ' '
    return ad

def rollable(ad, thr=0.25, strict=True, basis='phys'):
    if not ad: return None
    ad = normalise(ad)
    t = ad.get('Type')
    if t in TAG: return {t}
    if t == 'DT_PHYSICAL':
        v = {p: float(ad.get(p, 0) or 0) for p in PHYS}
        tot = sum(v.values())
        if basis == 'all':
            tot = sum(float(x) for k, x in ad.items() if k.startswith('DT_') and re.match(r'^-?[0-9.eE+-]+$', x))
        tot = tot or 1
        return {p for p, x in v.items() if ((x / tot > thr) if strict else (x / tot >= thr))}
    return set()

def score(thr, strict, use_proj=True, basis='phys'):
    ag = dis = 0; mism = []
    for w, rec in W.items():
        if w not in U or 'err' in rec: continue
        ad, src = eff(rec, use_proj)
        r = rollable(ad, thr, strict, basis)
        if r is None: continue
        for p in PHYS:
            pu = p not in r; lu = TAG[p] in U[w]
            if pu == lu: ag += 1
            else: dis += 1; nad = normalise(ad); mism.append((w, p, pu, lu, src, nad.get('Type'), {k: nad.get(k) for k in PHYS}, nad.get('_filled')))
    return ag, dis, mism

print('weapons', len(W), 'decode errors', sum('err' in r for r in W.values()),
      'with projectile', sum(1 for r in W.values() if r.get('proj')), 'projectile resolved', sum(1 for r in W.values() if r.get('projAttack')))
print('effective Type:', collections.Counter((normalise(eff(r)[0]) or {}).get('Type') for r in W.values() if 'err' not in r))
for use_proj in (False, True):
    for basis in ('phys', 'all'):
        for thr in (0.0, 0.2, 0.25, 0.3, 0.3333):
            for strict in (True, False):
                a, d, _ = score(thr, strict, use_proj, basis)
                print(f'  proj={use_proj!s:5} basis={basis:4} thr={thr:<6} {">" if strict else ">="}: agree {a} disagree {d}')
a, d, mism = score(0.25, True)
print('\nMISMATCHES (effective attack, share of physical > 0.25):', d)
for m in mism: print('  ', m[0], m[1], 'pred_unrollable', m[2], 'listed_unrollable', m[3], m[4], m[5], m[6])
unres = [w for w, r in W.items() if r.get('proj') and not r.get('projAttack')]
print('\nprojectile not resolved:', len(unres), [ (w.split('/')[-1], W[w]['proj']) for w in unres][:20])
out = {}
for w, r in W.items():
    if 'err' in r: out[w] = r; continue
    ad, src = eff(r)
    ro = rollable(ad)
    out[w] = dict(source=src, projPath=r.get('projPath'), attack={k: v for k, v in (normalise(ad) or {}).items() if k in ('Type', 'Amount', '_filled') or k in PHYS},
                  rollablePhysical=sorted(TAG[p] for p in (ro or [])), unrollablePhysical=sorted(TAG[p] for p in PHYS if ro is not None and p not in ro),
                  peUnrollables=U.get(w), inPE=w in U)
json.dump(out, open('weapon_rule.json', 'w'), indent=1)
print('\nABSENT-from-PE weapons, rule prediction:')
for w, o in out.items():
    if w in U or 'err' in o: continue
    print('  ', w.split('/')[-1], compat[w][0], o['source'], o['attack'], '-> unrollable', [t[6:-3] for t in o['unrollablePhysical']])
