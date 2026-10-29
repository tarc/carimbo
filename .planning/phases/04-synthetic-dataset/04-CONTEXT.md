# Phase 4: Synthetic Dataset - Context

**Gathered:** 2026-10-22
**Status:** Ready for planning

<domain>
## Phase Boundary

The seeded generator grows from the 3 skeleton cases to a new, versioned dataset of at least 150 cases. The cases are varied and deliberately hard: single and multi-page invoices, many line items, both tax regimes with mixed per-item codes, ST and deferral codes, alphanumeric CNPJs and CPF recipients. About 40% of the cases are image-only twins of clean invoices: a clean raster, rotation, blur and low DPI at two severities each, and a damaged barcode. CI proves that every ground-truth record validates against the exported schema and passes the .NET validators with zero errors, and that a regeneration is byte-identical to the committed files. The versioned manifest records each case's tags, split (dev/test), hashes, base case, seed and generator version, and a fixed stratified CI subset of about 20 cases.

Not in this phase: graders, slice statistics, confidence intervals or the failure taxonomy (Phase 5); CI gating thresholds, cache fixtures or any live run over the dataset (Phase 6); barcode-first decoding at intake (still deferred). Phase 4 makes no paid model call.

Numbering: D-01 to D-18 below are local to this phase. Project decisions are written as "DECISIONS D-xx" (`docs/DECISIONS.md`).

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked earlier, not re-discussed)
- Each case draws from its own `random.Random` derived from `(master_seed, case_id)`. Party names carry the `SINTETICA` marker, identifiers come from our own generators (`carimbo_datagen/ids.py`), and totals use the validators' formulas (vNF = vProd − vDesc + vST + vFrete + vSeg + vOutro + vIPI, taxes rounded half-up). All data is synthetic (DECISIONS D-14, DATA-07).
- Degraded scans use Pillow 12.3.0, numpy 2.5.3 (`default_rng(seed)`) and pypdfium2 5.13.0. reportlab 5.0.1 `Canvas(invariant=1)` wraps the rasters into image-only PDFs, never Pillow's PDF writer. zxing-cpp 3.1.1 labels barcode readability. Versions per `.claude/CLAUDE.md` § Technology Stack.
- The XML→`Invoice` mapping is `docs/DANFE-MAPPING.md`, implemented once in `NfeXmlMapper`. The ground-truth gate (Phase 2 D-17, `GroundTruthGateTests`) is reused at scale.
- Validators cover Simples CSOSN 101/102/103/300/400/500/900 and Normal CST 00/20/40/41/50/60/90. CST 10/30/70 and 51 produce the `TAX_CODE_UNSUPPORTED` *warning*, never an error. Mixed regime families on one invoice are an error (`REGIME_CODE_MISMATCH`).
- `data/skeleton/` stays byte-for-byte as it is. Published skeleton runs (`evals/results/`) and the decision cases (`data/decision/`) are keyed to its bytes, so the new dataset lives in its own directory with its own seed and version.

### Size and mix
- **D-01:** **Keep ≥150 cases** as DATA-02 states. Generation costs nothing. The live-run budget for full-dataset runs (about US$0.03 per case on Haiku 4.5, so about US$4.5+ per run) is a Phase 5/6 question, not a reason to shrink the dataset.
- **D-02:** **The D-26 review found nothing to cut in Phase 4.** It is offline generation with no spend. The cuts DECISIONS D-26 calls for land in Phases 5 and 6. Record this in the phase (and in the DECISIONS entry for the dataset) so the D-26 obligation is visibly discharged for Phase 4.
- **D-03:** **The mix is a fixed, committed quota table.** Exact counts per stratum come from axes such as regime × length (single page / multi-page / many items) × rendering × recipient id kind × issuer CNPJ kind, so coverage targets hold by construction: ≥10% alphanumeric CNPJ, both regimes, multi-page, each rendering variant. Phase 5 then gets slices with a known n. Within a stratum, per-case details such as item count within a band, freight or installments come from the case's seeded RNG. — **Reversibility:** costly — the quota table determines case ids and bytes; changing it is a new dataset version, and Phase 6 cache fixtures key on the bytes.
- **D-04:** **The generator produces four kinds of deliberately hard traits:**
  - **ST and deferral codes:** CST 10/30/70/51 items in Regime Normal cases. Their ground truth carries a `TAX_CODE_UNSUPPORTED` warning, which DATA-04 allows (zero errors, warnings permitted). They also feed the decision agent's warning path later.
  - **Long names and descriptions** that wrap on the DANFE (see D-05).
  - **Confusable values:** near-identical items (same description, different quantity or price), totals with many digits, issuer and recipient in the same UF with similar names. These are bait for transpositions and party swaps in the Phase 5 taxonomy.
  - **Mixed per-item tax codes:** several CSTs (or several CSOSNs) of one regime on one invoice. Never both families on one invoice, which would be the `REGIME_CODE_MISMATCH` error.
  - Per-item discounts are wanted too, but only if the item arithmetic the validators check (qty × unit_price vs total) and the DANFE item row agree on how a per-item discount shows. If they don't, leave per-item discounts out and keep the invoice-level discount from the skeleton.
    - *Determined 2026-10-29 (user decision during plan-phase): the DANFE prints no per-item discount or freight, so the D-04 condition does not hold; the dataset omits per-item discount and freight and keeps the invoice-level values.*
    - *Superseded 2026-10-29 (user decision during plan-phase): SEFAZ rejections 535/537 require ICMSTot vDesc/vFrete to equal the item sums, so the invoice-level discount and freight are always carried by per-item values; the D-04 condition holds (vProd stays gross, the DANFE row prints the gross total). The dataset spreads discount and freight over items summing exactly to the header; per-item values are XML-only and ungraded.*
- **D-05:** **Ground truth always equals what is printed.** The generator caps text lengths so that names, descriptions and other graded strings wrap on the DANFE but are never truncated. A datagen test checks that each clean text-layer PDF contains every graded string from its XML, which keeps the DANFE-MAPPING D-05 mapping exact (PITFALLS #4).

### Degraded and unreadable variants
- **D-06:** **Image variants are paired twins.** Each image-only case re-renders a clean base invoice: same XML ground truth, its own `case_id`, and a `base_case` field in the manifest. Phase 5 can then compare one invoice across renderings and separate the effect of degradation from content. — **Reversibility:** costly — the manifest's twin structure is what Phase 5 slicing and the split rule (D-14) rely on.
  - *Amended 2026-10-22 (user sign-off during plan-phase): twins carry a byte-identical copy of the base XML as `{twin_id}.xml` (equal `xml_sha256`), because the runner pairs each PDF with a same-stem XML; the ground truth is the same.*
- **D-07:** **The renderings of a base invoice:**
  - `text` (the original BrazilFiscalReport PDF with its text layer)
  - `image_clean` (rasterized at a normal scan DPI, undamaged, image-only)
  - rotation, blur and low DPI, each at `mild` and `severe`
  - `barcode_damaged`

  Every image rendering is an image-only PDF with no text layer (DATA-03), and a datagen test asserts that no text layer exists. `image_clean` separates "lost the text layer" from "the image got damaged".
- **D-08:** **Two severity levels per degradation type.** `Severe` is hard but still readable by a human. The parameters are tuned once by eye, then recorded in code and in the manifest's generator block. The result is a degradation curve (text → clean image → mild → severe) rather than a single point.
- **D-09:** **Barcode-unreadable has two parts:**
  - The `barcode_damaged` rendering damages only the barcode region (occlusion or smudge) and leaves the printed access-key digits legible.
  - Separately, **every** case gets a `barcode_readable` flag from zxing-cpp, decoded from the rendered page. A general degradation that happens to kill the barcode is recorded too, and the count of targeted variants still comes from the quota table.

  Tags distinguish the two (e.g. `barcode_damaged` vs `barcode_unreadable`).
- **D-10:** **About 40% of cases are image-only:** roughly 90 distinct text-layer invoices plus about 60 image twins, drawn from a stratified subset of the bases so each image rendering slice gets about 7 to 9 cases. Twins count toward the ≥150. The exact counts belong to the quota table (D-03).

### Commit vs regenerate (DATA-06)
- **D-11:** **Commit everything:** all XML, all PDFs (text and image) and the manifest. The image DPI and JPEG quality are tuned so the whole dataset stays ≤ ~20 MB. The committed bytes are the dataset: Phase 6 cache fixtures key on PDF SHA-256, so a reviewer replays the committed files and never needs to regenerate on their own platform. — **Reversibility:** costly — once Phase 6 records cache fixtures, any byte change in a PDF invalidates its fixtures and is a new dataset version.
- **D-12:** **Image realism comes before fitting the cap.** The base scan DPI stays realistic (e.g. ≥150 DPI for `image_clean`), so `mild` doesn't become unrealistically bad. If the full set still exceeds ~20 MB, fall back to DATA-06's alternative:
  - commit the XML, the text-layer PDFs, the manifest and the CI-subset image PDFs
  - check the other image PDFs against manifest hashes
  - record a new DECISIONS entry

  Don't trim image twins, and don't raise the cap.
- **D-13:** **The regeneration check is byte-identical, on CI Linux x64.** A `-check` recipe, like `datagen-check`, regenerates the whole dataset and requires identical bytes for every file. Determinism is promised only for the pinned CI platform and the author's WSL (both Linux x64). Other platforms use the committed files. The check runs on every PR in `just check`. If it takes more than about 2 to 3 minutes in CI, move it to a path-filtered CI job (generator sources, `python/uv.lock`, `data/`) and take it out of the default `just check`, recording the move.

### Splits and CI subset (DATA-05)
- **D-14:** **`split` is `dev` or `test`:** about 1/3 dev for prompt iteration and 2/3 test for published numbers. Splits are assigned per base invoice and stratified by the quota-table strata, so every stratum appears in both. **Twins always share their base invoice's split**, so no content leaks from dev to test. Phases 5 and 6 report test (and full) separately from dev. — **Reversibility:** costly — split membership defines what published numbers mean; reassigning it after results are published invalidates the comparison.
- **D-15:** **The fixed CI subset comes from the `dev` split.** The PR gate is what developers iterate against, so test stays untouched by gating loops.
- **D-16:** **The CI subset has about 20 cases, at least one per key stratum.** A deterministic rule picks them:
  - both regimes, multi-page, many items
  - alphanumeric CNPJ, CPF recipient, an ST/warning case
  - `image_clean`, each degradation type, `barcode_damaged`

  The membership is committed in the manifest. Recording it live costs about US$0.70 on Haiku 4.5, within the US$1 run cap. Phase 6 still calibrates the thresholds. The dev split (about 50 cases) has to contain every stratum the subset needs, which D-14's stratified assignment guarantees.

### Manifest and gate
- **D-17:** **The manifest grows the DATA-05 fields** on top of the skeleton manifest's shape (`dataset`, `dataset_version`, `master_seed`, `as_of_date`, `generator` versions, per case `xml`/`pdf` with SHA-256, `pages`, `tags`, `expected`):
  - per case: `split`, `base_case`, `rendering`, `severity`, `barcode_readable`
  - top level: a `ci_subset` list
  - the quota table, or its hash

  Any byte change to any file is a new `dataset_version`. — **Reversibility:** costly — the eval runner, graders (Phase 5) and `compare`'s version check read these fields.
  - *Qualified 2026-10-29 (user decision during plan-phase): before dataset-001 first merges to main, regenerations on the phase branch keep the label dataset-001; no eval run may be recorded against dataset-001 until plan 04-07 commits the final bytes. After that merge, any byte change to any file is a new dataset_version.*
- **D-18:** **The DATA-04 gate runs over every case:** the .NET `GroundTruthGateTests` (mapping plus zero validator `error` findings, warnings allowed) and the Python JSON-Schema validation of the mapped ground truth, both in CI. Twins share an XML, so the gate effectively covers every distinct invoice, but it iterates over the manifest so nothing is skipped.
  - *Amended 2026-10-22 (user sign-off during plan-phase): twins carry a byte-identical copy of the base XML as `{twin_id}.xml` (equal `xml_sha256`), because the runner pairs each PDF with a same-stem XML; the ground truth is the same.*

### Claude's Discretion
- The dataset directory name and id scheme (e.g. `data/dataset/` with `nfe-0001…`), the new master seed, the `dataset_version` label and the `as_of_date`.
- The exact quota-table axes and counts, within D-03, D-04 and D-10 and the ≥10% alphanumeric-CNPJ floor.
- Degradation parameters per severity, the base scan DPI and JPEG quality (within D-12), the barcode damage method, and tag names.
- How the generator is structured: extending `carimbo_datagen` (`spec.py` profiles → quota-driven specs, a new raster/degrade module), the CLI shape (`build`/`check` with a dataset selector or a separate command), and the recipe names (`just dataset`, `just dataset-check`).
- How `GroundTruthGateTests` and `python/tests/test_ground_truth.py` are generalized from the hard-coded `data/skeleton` path to iterate over a manifest.
- The deterministic rule for split assignment and CI-subset selection.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Scope and requirements
- `.planning/ROADMAP.md` § Phase 4: the goal, the four success criteria, and the Planning Notes for Phase 4 (renderer fidelity against reference samples; byte reproducibility decides DATA-06)
- `.planning/REQUIREMENTS.md`: DATA-02 to DATA-06 (lines ~41-45). DATA-01 and DATA-07 are already complete.
- `.planning/PROJECT.md`: core value (measured quality), and the D-26 note that Phases 4 to 6 are scoped down when discussed

### Decisions
- `docs/DECISIONS.md` D-14 (synthetic, seeded, versioned), D-26 (the scope-down obligation; D-02 here discharges it for Phase 4), D-20 (committed per-case scores and replay fixtures, which is why the committed bytes matter), D-22 (Invoice v2 target), D-23 and D-25 (validators and the parse boundary), D-34 (the decision cases derive from skeleton bytes, so the skeleton stays untouched)
- `docs/DANFE-MAPPING.md`: the XML→`Invoice` field mapping that ground truth must follow; D-05 keeps printed text equal to the XML

### Stack research
- `.claude/CLAUDE.md` § Technology Stack: Data generation table (nfelib, BrazilFiscalReport, reportlab `invariant=1`, Pillow, numpy, pypdfium2, zxing-cpp, Faker), "What NOT to Use" (Pillow PDF writer, text-layer degraded scans), Stack Patterns by Variant (rasterized PDFs, platform drift), and Gaps (renderer fidelity, NF-e layout version)
- `.planning/research/PITFALLS.md`: Pitfall 4 (XML is a superset of the DANFE, truncation) and the datagen determinism pitfalls
- `.planning/research/STACK.md`: version pins and the determinism spikes behind them

### Prior phase artifacts
- `.planning/phases/01-walking-skeleton/01-CONTEXT.md`: D-05 to D-07 (skeleton cases committed, byte-identical regeneration in CI)
- `.planning/phases/02-validated-extraction/02-CONTEXT.md`: D-01 to D-08 (target fields, regimes and tax rules, tolerances), D-16 and D-17 (skeleton rework, ground-truth gate)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `python/src/carimbo_datagen/spec.py`: `CaseSpec`, `ItemSpec`, `PartySpec`, `InstallmentSpec`, the per-case RNG, `_profiles()` (three fixed profiles), tax tables (`_ICMS_RATE`, `_ICMS_GROUP` cover CST 00/20 and CSOSN 101/102/400 only today), the `_GOODS` catalogue and the name builders with the SINTETICA marker. The quota table replaces the fixed profiles, and the tax tables grow to the remaining CST/CSOSN codes plus ST/51.
- `python/src/carimbo_datagen/ids.py`: CNPJ (numeric and alphanumeric), CPF and access-key generators with check digits.
- `python/src/carimbo_datagen/nfe_xml.py`: `build_nfe_xml(spec)` and per-group writers (`_icms`, `_ipi`, `_items`, `_totals`, `_cobr`). The ST and deferral ICMS groups need new writers.
- `python/src/carimbo_datagen/danfe.py`: `render_danfe(xml, out_path)` on BrazilFiscalReport, producing the `text` rendering.
- `python/src/carimbo_datagen/cli.py`: `build_dataset`, `build` and `check` (fresh regeneration plus a byte comparison), atomic writes, manifest assembly with generator versions. This is the pattern for the new dataset's build/check.
- `dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs` and `dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs`: the gate, currently hard-coded to `data/skeleton` and three case ids.
- `python/tests/test_ground_truth.py`, `test_datagen.py`, `test_danfe_labels.py`, `test_synthetic_only.py`: Python-side ground-truth, determinism, label and synthetic-only checks to generalize.

### Established Patterns
- Generated artifacts are committed with a `-check` recipe in `just check` (`datagen-check`, `schema-check`, `decision-cases-check`) and listed as never hand-edited in `AGENTS.md`. The new dataset joins that list.
- Every dependency is pinned exactly in `python/pyproject.toml` with `uv.lock` committed. Pillow, numpy, pypdfium2, reportlab and zxing-cpp aren't dependencies yet.
- Manifest `generator` block records library versions, and `expected` holds a small summary per case.

### Integration Points
- `justfile`: new build and check recipes, with the check added to `check`. `.github/workflows/ci.yml` runs the same recipes.
- `AGENTS.md`: the layout (`data/<dataset>/`), the commands table and the generated-files list.
- `python/src/carimbo_evals` (`cli.py` `--cases-dir`, `grader._load_manifest`): the runner and grader already read a manifest from a cases dir. The new manifest fields should stay compatible, or be read by Phase 5.

</code_context>

<specifics>
## Specific Ideas

- Each image twin is a measurement design choice: text → clean image → mild → severe on the *same* invoice gives a degradation curve that content variance can't confound.
- The dev/test holdout and the dev-only CI subset exist so published numbers are a real holdout, in line with the core value of measured quality.
- Two different questions are recorded apart: "barcode deliberately damaged" (a quota) and "barcode actually unreadable" (measured by zxing-cpp).

</specifics>

<deferred>
## Deferred Ideas

- Live-run budget for full-dataset runs (~US$4.5+ per Haiku run, more on Sonnet, against US$1 per run and US$5 per phase): a Phase 5/6 decision, likely where the D-26 cuts land.
- A combined "realistic scan" degradation (rotation + blur + JPEG at low DPI): not chosen. Two levels per single type only.
- Allowing DANFE truncation and grading against the printed form: rejected for now (D-05).
- Barcode-first access key at intake: still deferred, as in Phase 2.

</deferred>

---

*Phase: 04-synthetic-dataset*
*Context gathered: 2026-10-22*
