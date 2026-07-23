
## 2026-07-22 - Sequential bit-decoder built + validated; MID value bytes are coded above the field layer

Hypothesis: MID decompresses to a clean bit-packed quantized reflection field-stream that the schema can be walked over (bool 1b, float 1+nbits, string/path pool-index, container 30-bit mask), so the 12 anchors decode bit-aligned.

Finding: Mixed / conflicting evidence. The DECODER is correct (offline/bitdecode.py, round-trip self-validation EXACT: encode->decode reproduces bool/string/path/container/vector/struct with tail=0, no overrun). But the anchors do NOT align against MID: the MID per-record value PAYLOAD is coded/compressed above the field layer, not a walkable bit-packed stream.

Evidence:
- offline/bitdecode.py roundTripSelfValidation.roundTripExact = true (bool=1b, string/path=12b poolidx bsr(50251>>4)+1, container=30b mask, vector=3xfloat all stay in sync).
- Per-record MID payload zero-byte fraction = 0.008 (random baseline 0.004); a bit-packed defaulted stream would be HIGH. Payloads size-capped 0..40 bytes, per-record entropy ~4.1 (near max for the length). Concatenated payload entropy 7.993 b/B ~ maximal.
- Value-fragment entropy 5.76 = delta/dict-coded NAME text (e.g. RelokFIS,-GLAPistol), not values.
- Region layout: HEADER[0:323675] POOL[..1372093] (plaintext Field=value intern) MID[..21094036] (coded values) TAIL[..52710200] (length-prefixed reflection type/string table).
- Only 2 of 12 exact anchors have a schema class (GLAPistol->WeaponTraceFireBehavior prec 0.246, AHBoltace->LotusFightingComboMeleeImpactBehavior prec 0.23); both overrun (GLAPistol 191/192b, AHBoltace 32/32b for 48 fields).

Reason: entropy + zero-byte-fraction of the payload match coded/signature data, not a bit-packed field stream. Corrects an earlier hasty read: individual short records have LOW entropy (4.1) but that is length-limited randomness, not structure (zero-frac proves it).

Field types decode-aligned (exact width, validated): bool, string, path, container(30b header), vector, struct(nested).
Field types width-UNRESOLVED offline: float(quant tag,nbits pending), int(accessor slot width), enum(mode+ordinal width).

Next Step: to reach values, either (a) reverse the MID payload codec (dict/back-ref token layer above the fields) or confirm payload is a content signature and reconstruct values from POOL+TAIL; (b) if a live dump becomes available, the walker + quant_table.json calibrate float (tag,nbits) directly. The bit-decoder is ready to consume ANY plain stream once produced.

## 2026-07-22 - Float (tag,nbits) calibration by joint bit-alignment vs 2560 dumps

Hypothesis: Requiring the signed-magnitude dequant to reproduce ALL known dump values of a
field name (joint over many instances) uniquely pins each float field's (quant-tag, nbits).
Finding: False (0 fields uniquely locked offline).
Evidence: offline/field_quant_map.json (per-field bands for all 2798 float field names, 176 with
observed dump values); offline/_all_field_values.json. Independently reconfirms
offline/VERDICT_quant_offline.json blockers A (field->tag binding) and B (nbits) = runtime-only.
Reason: THREE independent obstructions, all empirically demonstrated:
  (1) TAG DEGENERACY - kind==2 dequant depends only on (hi,nbits); 2..4 tags share each |hi|
      ({17,42,43,60}=1.0; {65,66,106,127}=180.0; etc.) so the specific tag is unrecoverable in
      principle, only the |hi|-class.
  (2) NEAR-LOSSLESS VALUES - designer floats survive at nbits 16..24 (368/415 floaty fields need
      min-nbits>=16); nbits resolves to a BAND, never a point.
  (3) RAW-F32 fields - 9-11 fields (fireRate max=1500, traceDistance max=1000) exceed every tag
      hi(<=360) => kind=0 raw 32-bit float, no quant tag applies.
  Low-distinct-count fields (1-2 values) admit nbits 5..24 (false "coarse" positives).
GLAPistol anchors: reloadTime -> tags{71,85,122,123} x nbits[23,24]; OmegaAttenuation ->
  {22,71,85,122,123,147} x [21,24]; WhizChance -> 13 tags x [19,24]; fireRate -> raw_f32. 0/4 locked.
Next Step: The only closer is the documented minimal runtime capture - hook read dequant
  0x1406095b0 for ONE deserialization and dump [ser+0x198] (kind,nbits,lo,hi) per field. Game is
  CLOSED per constraints, so offline calibration is provably at its ceiling.
