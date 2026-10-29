# Phase 4: Synthetic Dataset - Research

**Researched:** 2026-10-22
**Domain:** Seeded synthetic NF-e dataset generation (XML + DANFE PDF + image-only scanned twins), manifest design, CI proof of ground truth and byte-identical regeneration
**Confidence:** HIGH (every load-bearing number below was measured in this session on the real generator, renderer and validators)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (locked earlier, not re-discussed)
- Each case draws from its own `random.Random` derived from `(master_seed, case_id)`. Party names carry the `SINTETICA` marker, identifiers come from our own generators (`carimbo_datagen/ids.py`), and totals use the validators' formulas (vNF = vProd − vDesc + vST + vFrete + vSeg + vOutro + vIPI, taxes rounded half-up). All data is synthetic (DECISIONS D-14, DATA-07).
- Degraded scans use Pillow 12.3.0, numpy 2.5.3 (`default_rng(seed)`) and pypdfium2 5.13.0. reportlab 5.0.1 `Canvas(invariant=1)` wraps the rasters into image-only PDFs, never Pillow's PDF writer. zxing-cpp 3.1.1 labels barcode readability. Versions per `.claude/CLAUDE.md` § Technology Stack.
- The XML→`Invoice` mapping is `docs/DANFE-MAPPING.md`, implemented once in `NfeXmlMapper`. The ground-truth gate (Phase 2 D-17, `GroundTruthGateTests`) is reused at scale.
- Validators cover Simples CSOSN 101/102/103/300/400/500/900 and Normal CST 00/20/40/41/50/60/90. CST 10/30/70 and 51 produce the `TAX_CODE_UNSUPPORTED` *warning*, never an error. Mixed regime families on one invoice are an error (`REGIME_CODE_MISMATCH`).
- `data/skeleton/` stays byte-for-byte as it is. Published skeleton runs (`evals/results/`) and the decision cases (`data/decision/`) are keyed to its bytes, so the new dataset lives in its own directory with its own seed and version.

#### Size and mix
- **D-01:** **Keep ≥150 cases** as DATA-02 states. Generation costs nothing. The live-run budget for full-dataset runs (about US$0.03 per case on Haiku 4.5, so about US$4.5+ per run) is a Phase 5/6 question, not a reason to shrink the dataset.
- **D-02:** **The D-26 review found nothing to cut in Phase 4.** It is offline generation with no spend. The cuts DECISIONS D-26 calls for land in Phases 5 and 6. Record this in the phase (and in the DECISIONS entry for the dataset) so the D-26 obligation is visibly discharged for Phase 4.
- **D-03:** **The mix is a fixed, committed quota table.** Exact counts per stratum come from axes such as regime × length (single page / multi-page / many items) × rendering × recipient id kind × issuer CNPJ kind, so coverage targets hold by construction: ≥10% alphanumeric CNPJ, both regimes, multi-page, each rendering variant. Phase 5 then gets slices with a known n. Within a stratum, per-case details such as item count within a band, freight or installments come from the case's seeded RNG. — **Reversibility:** costly — the quota table determines case ids and bytes; changing it is a new dataset version, and Phase 6 cache fixtures key on the bytes.
- **D-04:** **The generator produces four kinds of deliberately hard traits:**
  - **ST and deferral codes:** CST 10/30/70/51 items in Regime Normal cases. Their ground truth carries a `TAX_CODE_UNSUPPORTED` warning, which DATA-04 allows (zero errors, warnings permitted). They also feed the decision agent's warning path later.
  - **Long names and descriptions** that wrap on the DANFE (see D-05).
  - **Confusable values:** near-identical items (same description, different quantity or price), totals with many digits, issuer and recipient in the same UF with similar names. These are bait for transpositions and party swaps in the Phase 5 taxonomy.
  - **Mixed per-item tax codes:** several CSTs (or several CSOSNs) of one regime on one invoice. Never both families on one invoice, which would be the `REGIME_CODE_MISMATCH` error.
  - Per-item discounts are wanted too, but only if the item arithmetic the validators check (qty × unit_price vs total) and the DANFE item row agree on how a per-item discount shows. If they don't, leave per-item discounts out and keep the invoice-level discount from the skeleton.
- **D-05:** **Ground truth always equals what is printed.** The generator caps text lengths so that names, descriptions and other graded strings wrap on the DANFE but are never truncated. A datagen test checks that each clean text-layer PDF contains every graded string from its XML, which keeps the DANFE-MAPPING D-05 mapping exact (PITFALLS #4).

#### Degraded and unreadable variants
- **D-06:** **Image variants are paired twins.** Each image-only case re-renders a clean base invoice: same XML ground truth, its own `case_id`, and a `base_case` field in the manifest. Phase 5 can then compare one invoice across renderings and separate the effect of degradation from content. — **Reversibility:** costly — the manifest's twin structure is what Phase 5 slicing and the split rule (D-14) rely on.
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

#### Commit vs regenerate (DATA-06)
- **D-11:** **Commit everything:** all XML, all PDFs (text and image) and the manifest. The image DPI and JPEG quality are tuned so the whole dataset stays ≤ ~20 MB. The committed bytes are the dataset: Phase 6 cache fixtures key on PDF SHA-256, so a reviewer replays the committed files and never needs to regenerate on their own platform. — **Reversibility:** costly — once Phase 6 records cache fixtures, any byte change in a PDF invalidates its fixtures and is a new dataset version.
- **D-12:** **Image realism comes before fitting the cap.** The base scan DPI stays realistic (e.g. ≥150 DPI for `image_clean`), so `mild` doesn't become unrealistically bad. If the full set still exceeds ~20 MB, fall back to DATA-06's alternative:
  - commit the XML, the text-layer PDFs, the manifest and the CI-subset image PDFs
  - check the other image PDFs against manifest hashes
  - record a new DECISIONS entry

  Don't trim image twins, and don't raise the cap.
- **D-13:** **The regeneration check is byte-identical, on CI Linux x64.** A `-check` recipe, like `datagen-check`, regenerates the whole dataset and requires identical bytes for every file. Determinism is promised only for the pinned CI platform and the author's WSL (both Linux x64). Other platforms use the committed files. The check runs on every PR in `just check`. If it takes more than about 2 to 3 minutes in CI, move it to a path-filtered CI job (generator sources, `python/uv.lock`, `data/`) and take it out of the default `just check`, recording the move.

#### Splits and CI subset (DATA-05)
- **D-14:** **`split` is `dev` or `test`:** about 1/3 dev for prompt iteration and 2/3 test for published numbers. Splits are assigned per base invoice and stratified by the quota-table strata, so every stratum appears in both. **Twins always share their base invoice's split**, so no content leaks from dev to test. Phases 5 and 6 report test (and full) separately from dev. — **Reversibility:** costly — split membership defines what published numbers mean; reassigning it after results are published invalidates the comparison.
- **D-15:** **The fixed CI subset comes from the `dev` split.** The PR gate is what developers iterate against, so test stays untouched by gating loops.
- **D-16:** **The CI subset has about 20 cases, at least one per key stratum.** A deterministic rule picks them:
  - both regimes, multi-page, many items
  - alphanumeric CNPJ, CPF recipient, an ST/warning case
  - `image_clean`, each degradation type, `barcode_damaged`

  The membership is committed in the manifest. Recording it live costs about US$0.70 on Haiku 4.5, within the US$1 run cap. Phase 6 still calibrates the thresholds. The dev split (about 50 cases) has to contain every stratum the subset needs, which D-14's stratified assignment guarantees.

#### Manifest and gate
- **D-17:** **The manifest grows the DATA-05 fields** on top of the skeleton manifest's shape (`dataset`, `dataset_version`, `master_seed`, `as_of_date`, `generator` versions, per case `xml`/`pdf` with SHA-256, `pages`, `tags`, `expected`):
  - per case: `split`, `base_case`, `rendering`, `severity`, `barcode_readable`
  - top level: a `ci_subset` list
  - the quota table, or its hash

  Any byte change to any file is a new `dataset_version`. — **Reversibility:** costly — the eval runner, graders (Phase 5) and `compare`'s version check read these fields.
- **D-18:** **The DATA-04 gate runs over every case:** the .NET `GroundTruthGateTests` (mapping plus zero validator `error` findings, warnings allowed) and the Python JSON-Schema validation of the mapped ground truth, both in CI. Twins share an XML, so the gate effectively covers every distinct invoice, but it iterates over the manifest so nothing is skipped.

### Claude's Discretion

- The dataset directory name and id scheme (e.g. `data/dataset/` with `nfe-0001…`), the new master seed, the `dataset_version` label and the `as_of_date`.
- The exact quota-table axes and counts, within D-03, D-04 and D-10 and the ≥10% alphanumeric-CNPJ floor.
- Degradation parameters per severity, the base scan DPI and JPEG quality (within D-12), the barcode damage method, and tag names.
- How the generator is structured: extending `carimbo_datagen` (`spec.py` profiles → quota-driven specs, a new raster/degrade module), the CLI shape (`build`/`check` with a dataset selector or a separate command), and the recipe names (`just dataset`, `just dataset-check`).
- How `GroundTruthGateTests` and `python/tests/test_ground_truth.py` are generalized from the hard-coded `data/skeleton` path to iterate over a manifest.
- The deterministic rule for split assignment and CI-subset selection.

### Deferred Ideas (OUT OF SCOPE)

- Live-run budget for full-dataset runs (~US$4.5+ per Haiku run, more on Sonnet, against US$1 per run and US$5 per phase): a Phase 5/6 decision, likely where the D-26 cuts land.
- A combined "realistic scan" degradation (rotation + blur + JPEG at low DPI): not chosen. Two levels per single type only.
- Allowing DANFE truncation and grading against the printed form: rejected for now (D-05).
- Barcode-first access key at intake: still deferred, as in Phase 2.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| DATA-02 | Dataset has ≥150 cases covering single/multi-page, many line items, several tax regimes, and ≥10% alphanumeric-CNPJ cases | Quota-table strawman (90 text bases + 64 twins = 154). Page count is driven by item count **and** description wrapping, so strata must be asserted against the rendered page count (Pitfall 2). New ICMS groups for CST 10/30/51/70/90 and CSOSN 103/300/500/900 render and map correctly (Pattern 3). |
| DATA-03 | Degraded (rotation, blur, low DPI) and barcode-unreadable variants are image-only PDFs (no text layer) | Measured pipeline: pdfium raster (200 DPI gray) → Pillow degrade → JPEG → reportlab `invariant=1`. Image-only proof: zero pdfium text objects and zero text characters (Code Examples 1 and 3). Barcode readability measured with zxing-cpp at a **fixed decode DPI** (Pitfall 4). |
| DATA-04 | Every ground-truth record validates against the exported schema and passes the .NET validators (checked in CI) | Generalize `GroundTruthGateTests` (hard-coded to `data/skeleton`) to read a manifest path; add a Python `jsonschema` Draft 2020-12 pass over `invoice_from_xml` output per manifest case. Validator rules the new tax codes must satisfy are quoted below (Pattern 3). |
| DATA-05 | A versioned manifest records per-case tags, split, content hashes, seed and generator version, and defines a fixed stratified CI subset | Manifest v2 shape (Code Examples 6), deterministic hash-ordered split and subset rule (Pattern 5), invariants that tests must assert (Validation Architecture). |
| DATA-06 | Dataset committed when ≤ ~20 MB; otherwise manifest + CI subset committed and regeneration is a new dataset version | Measured per-rendering sizes give a projected 15 to 17 MB for the full set (committed); levers and a hard byte-budget test if it drifts toward 20 MB (Pitfall 5, Assumption A1). |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

Extracted from `./CLAUDE.md` (imports `AGENTS.md`), `./.claude/CLAUDE.md` and the user's global `~/.claude/CLAUDE.md`. The planner must verify compliance; none of the recommendations below contradict them.

- English for code, docs and commit messages. **Pin exact versions; no floating ranges** (`AGENTS.md` Conventions).
- Generated files are never hand-edited. The new dataset directory joins that list in `AGENTS.md`; regenerate with a recipe.
- `data/skeleton/` stays byte-for-byte (CONTEXT carried-forward). `just datagen-check` is the regression guard for any shared-code refactor.
- Wire format: snake_case, string enums, money as `^-?[0-9]+\.[0-9]{2}$`. The Domain stays pure.
- All data is synthetic (DECISIONS D-14). Never add real taxpayer data. Party names carry `SINTETICA`.
- Phase 4 makes **no paid model call**. Live recipes stay `just skeleton`, `just decisions`, probes only.
- What not to use (`AGENTS.md` and `.claude/CLAUDE.md`): Pillow `save(format="PDF")` for datasets, text-layer PDFs for degraded scans, `JsonSerializerDefaults.Web`, `FluentAssertions` 8.x (use xUnit `Assert`), `ZXing.Net.Bindings.SkiaSharp` next to `PDFtoImage`, `httpx` 0.28.1.
- Decision records go in `docs/DECISIONS.md` as **superseding entries**. Highest existing id is D-39 (`docs/DECISIONS.md:867`), so the next id is **D-40**. `just docs-check` hard-codes the list `D-18 ... D-39` and must be extended when D-40 lands.
- Commands run through `devenv shell -- <cmd>`; the shell already has `uv`, `just`, `dotnet` on PATH in this checkout. Ad-hoc scratch work must not leave `.devenv/` or `result` links in repo roots (global CLAUDE.md footprint rule).
- Commit trailer and PR body attribution per the session's attribution reminder.
- GSD workflow: repo edits go through a GSD command (this phase is that command).

## Summary

The generator is small (~1,000 lines), fast and already byte-reproducible; Phase 4 widens it rather than replacing it. Everything the CONTEXT decisions ask for is feasible with the pinned stack, and the measured numbers resolve the biggest open risk (DATA-06 size): a 154-case dataset of 90 text-layer bases plus 64 image twins projects to roughly 15 to 17 MB (an extrapolation, assumption A1) when the twins are built as **200 DPI grayscale JPEG q50 (optimized) wrapped by reportlab with ASCII85 turned off**. The default reportlab behaviour (ASCII85 around the JPEG) inflates every image PDF by about 25%, which alone would push the set to about 20 MB. Single-page rendering costs per page: clean 239 KB, rotated 261 KB, blur(2.4) 106 KB, 110 DPI 98 KB, 72 DPI 49 KB.

Four findings the CONTEXT does not anticipate and the planner must design around: (1) the existing eval runner pairs every `*.pdf` with a **same-stem `.xml`** and the grader loads `{case_id}.xml`, so twins need **their own byte-identical XML copy** (about 10 KB each) rather than a shared XML; (2) the Code 128 barcode printed by BrazilFiscalReport is **only decodable at about 200 DPI or higher** (not at 150, even on the clean text PDF), so `barcode_readable` must be measured at one fixed decode DPI and `image_clean` must be 200 DPI for `barcode_damaged` to be a meaningful contrast; (3) description wrapping, not just item count, drives page count (20 items with 120-character descriptions already spill to 2 pages) and a single token longer than the column is **broken mid-token**, so graded text must be built from short words; (4) the validators treat CST 10/30/51/70 as `TAX_CODE_UNSUPPORTED` warnings, but vNF still includes `totals.icms_st_amount`, so `CaseSpec.invoice_total` (which hard-codes ST = 0) must learn about ST.

**Primary recommendation:** Build a thin tracer first (one Normal base with an ST item, one `rot_mild` twin, manifest v2 shape, the generalized .NET gate and a Python jsonschema gate, `dataset-check` byte-identical on CI). Then widen to the quota table. Freeze the degradation parameters and JPEG quality **before the first commit** of image PDFs, because every re-tune adds another ~15 MB to git history and invalidates Phase 6 cache fixtures.

## Architectural Responsibility Map

This phase is an offline build pipeline, not a multi-tier app; the "tiers" are pipeline stages and CI gates.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Case specification (quota → seeded `CaseSpec`, totals arithmetic) | Python `carimbo_datagen.spec` | — | Owns the per-case RNG and the validators' formulas (CONTEXT carried-forward). |
| XML ground truth (ICMS group writers, ST totals) | Python `carimbo_datagen.nfe_xml` | — | Single writer; mapping rules live in `docs/DANFE-MAPPING.md`. |
| Text-layer DANFE PDF | BrazilFiscalReport 1.2.0 via `danfe.render_danfe` | — | Existing deterministic renderer. |
| Image-only twins (raster, degrade, JPEG, PDF wrap) | New Python module (e.g. `carimbo_datagen/scan.py`) | pypdfium2, Pillow, numpy, reportlab | Pure function of (base PDF bytes, rendering, severity, seed). |
| Barcode readability flag | zxing-cpp in Python datagen | — | Same engine as the .NET `ZXingCpp`; flag is a manifest fact, not a validator. |
| Manifest, split, CI subset | Python `carimbo_datagen.cli` (dataset build) | — | Deterministic function of the quota table and seed. |
| Ground-truth validity gate | .NET `GroundTruthGateTests` (mapper + validators) | Python `jsonschema` pass | DATA-04 names both. The .NET side is the authority for validators (Pitfall 17: no circular oracle). |
| Byte-identical regeneration proof | `carimbo-datagen` check command in `just check` / CI | — | Same pattern as `datagen-check`. |
| Size budget and manifest invariants | Python pytest (offline, cheap) | — | Reads committed files only. |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| BrazilFiscalReport | 1.2.0 | XML → DANFE (text layer + Code 128) | Already the renderer; reads whatever ICMS group is present (verified, Pattern 3). `[VERIFIED: python/pyproject.toml, uv.lock]` |
| Pillow | 12.3.0 | Rotate, blur, JPEG encode | Already locked (transitive of fpdf2); becomes a direct pin. `[VERIFIED: python/uv.lock:731-733]` `version = "12.3.0"` |
| pypdfium2 | 5.13.0 | Rasterize DANFE page(s); text-layer assertions | Already pinned, but **dev-group only** (`python/pyproject.toml:23` `"pypdfium2==5.13.0",`): move to runtime `dependencies`. |
| zxing-cpp | 3.1.1 | Barcode readability flag | Already pinned in the dev group (`python/pyproject.toml:24` `"zxing-cpp==3.1.1",`): move to runtime. |
| reportlab | 5.0.1 | Image-only PDF wrapper (`Canvas(invariant=1)`) | Locked by CONTEXT; byte-identical across two fresh processes (run this session). **New dependency.** |
| numpy | 2.5.3 | Seeded `default_rng` for degradation geometry and optional scan noise | Locked by CONTEXT; requires Python ≥3.12 (matches project). **New dependency.** |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| jsonschema | 4.26.0 (existing) | DATA-04 Python-side schema validation | Reuse `carimbo_evals.grader.load_schema_validator` over `schema/invoice.schema.json` (`DEFAULT_SCHEMA_PATH = Path("schema/invoice.schema.json")`, `grader.py:33`). |
| lxml | 6.1.3 (existing) + nfelib 3.0.0 (existing) | Optional XSD conformance test of the new ICMS group writers | Only as a unit test of the writers (Pitfall 9); not as a dataset-wide gate. |
| typer | 0.27.2 (existing) | CLI | Add a `dataset` build/check pair next to `build`/`check`. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| JPEG gray q50 optimized (recommended) | 8-bit gray Flate | Flate is smaller for the *clean* page (190 KB vs 239 KB at 200 DPI) but blows up with noise and is not what real scanners emit; JPEG shrinks with blur. Keep JPEG. |
| Twin XML copies (recommended) | One shared XML + runner change | Saves ~0.6 MB but breaks `discover_cases` (same-stem pairing) and the grader's `{case_id}.xml` lookup (Pitfall 1). |
| 200 DPI image_clean | 150 DPI | 150 DPI saves about 25% but the barcode is unreadable even on the clean text PDF at 150 DPI, so `barcode_damaged` would have no clean contrast. |

**Installation (after the supply-chain checkpoint, see audit):**
```bash
# python/pyproject.toml [project].dependencies gains (exact pins, no ranges):
#   "pillow==12.3.0", "numpy==2.5.3", "reportlab==5.0.1", "pypdfium2==5.13.0", "zxing-cpp==3.1.1"
# and pypdfium2/zxing-cpp leave the dev group. Then:
uv lock --project python && uv sync --project python --locked
```

**Version verification (run this session):**
- `numpy 2.5.3`: PyPI JSON `requires_python >=3.12`, uploaded 2026-09-06; latest on the index is 2.5.4 (we pin the CONTEXT/STACK version on purpose). `[VERIFIED: pypi.org JSON API]`
- `reportlab 5.0.1`: `requires_python <4,>=3.9`, uploaded 2026-08-20; latest is 5.1.0 (pin 5.0.1 per CONTEXT). `[VERIFIED: pypi.org JSON API]`
- Pillow, pypdfium2, zxing-cpp: read from `python/uv.lock` / `python/pyproject.toml`; the installed pdfium is `153.0.7999.0` (`pdfium.PDFIUM_INFO`) and Pillow's JPEG backend is libjpeg-turbo 6.2 (`PIL.features`). `[VERIFIED: run in project venv]`

## Package Legitimacy Audit

Seam run: `gsd-tools query package-legitimacy check --ecosystem pypi numpy reportlab pillow pypdfium2 zxing-cpp`.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| numpy | PyPI | first release 2006-12-02 (152 releases) | unknown to seam | none reported by seam | SUS (`too-new`, `unknown-downloads`, `no-repository`) | Flagged. The seam's `publishedAt` is the **latest** release date (2026-10-10), not the package's age; the verdict is a false positive for a 20-year-old package. Planner still adds a `checkpoint:human-verify` before the lock change, following the Phase 1 precedent (`01-01-PLAN.md` supply-chain checkpoint). |
| reportlab | PyPI | first releases 2.0 (2000s); latest 2026-10-22 | unknown to seam | https://www.reportlab.com/ | SUS (`too-new`, `unknown-downloads`) | Flagged, same reasoning. New dependency: checkpoint required. |
| pillow | PyPI | long-established | unknown to seam | github.com/python-pillow/Pillow | SUS (`too-new`, `unknown-downloads`) | Already in `uv.lock` at 12.3.0 (transitive). Only becomes a direct pin: covered by the same checkpoint. |
| pypdfium2 | PyPI | long-established | unknown to seam | — | SUS (seam) | Already locked and in use by tests. Move dev → runtime only. |
| zxing-cpp | PyPI | long-established | unknown to seam | — | SUS (`unknown-downloads`) | Already locked and in use by tests. Move dev → runtime only. |

**Packages removed due to SLOP verdict:** none.
**Packages flagged SUS:** numpy, reportlab (new), plus the three already-locked packages. The planner inserts one `checkpoint:human-verify` before `uv lock` that shows the lock diff (expected new entries: numpy, reportlab, charset-normalizer; Pillow stays 12.3.0). All five names are the project's locked choices (`CONTEXT.md` carried-forward; spiked in `.claude/CLAUDE.md` § Technology Stack) and were executed in this session (`uv run --with numpy==2.5.3 --with reportlab==5.0.1`), so they are `[VERIFIED: executed]`, but the seam verdict is reported as returned. `npm`/`postinstall` checks do not apply (PyPI).

## Architecture Patterns

### System Architecture Diagram

```
 quota table (committed code, hashed)          master_seed (new, own dataset)
          |                                              |
          v                                              v
  [Case planner] --(case_id, stratum, traits)--> random.Random(f"{seed}:{case_id}") per base
          |                                              |
          v                                              v
  CaseSpec (items, parties, ST/deferral, totals incl. vST)  ----> expected{} block (from spec, never re-read from XML)
          |
          v
  build_nfe_xml(spec) ---> {case_id}.xml ------------------------------+
          |                                                            |
          v                                                            |
  render_danfe(xml) ---> {base}.pdf (text layer, Code 128)             |
          |                                                            |
          +--> for each twin (base, rendering, severity):              |
                pdfium raster @ native DPI (gray)                      |
                -> Pillow degrade (rotate | blur | low-DPI re-raster | barcode occlusion)
                -> JPEG q50 optimize -> reportlab invariant=1 (useA85=0) -> {twin}.pdf
                copy base XML bytes -> {twin}.xml  --------------------+
          |
          v
  per case: sha256(xml), sha256(pdf), pages (from renderer / pdfium), barcode_readable (zxing @ fixed DPI), tags
          |
          v
  split + CI subset (deterministic, hash-ordered, stratified) -> manifest.json (sorted keys, no timestamps)

 CI gates over the committed directory:
   dataset-check : regenerate to temp dir, compare bytes of every file     (byte identity)
   .NET gate     : NfeXmlMapper.Load + InvoiceValidator, zero error findings (per manifest case)
   Python gate   : invoice_from_xml -> jsonschema Draft 2020-12 + manifest invariants + size budget
```

### Recommended Project Structure
```
python/src/carimbo_datagen/
├── spec.py            # existing: keep skeleton profiles untouched; add tax tables, ST fields (additive)
├── ids.py             # existing
├── nfe_xml.py         # existing: add ICMS10/30/40/51/60/70/90 and SN500/SN900 writers (additive)
├── danfe.py           # existing text renderer
├── quota.py           # NEW: the committed quota table, strata, split + CI-subset rules
├── dataset_spec.py    # NEW: quota row -> seeded CaseSpec (own RNG; does not alter build_case_spec)
├── scan.py            # NEW: raster, degrade, JPEG, reportlab wrap, barcode flag
└── cli.py             # existing skeleton build/check + new dataset build/check
data/dataset/          # NEW (name is Claude's discretion): nfe-0001.xml/.pdf ... manifest.json
python/tests/          # test_dataset_*.py (manifest invariants, no-text-layer, text-equals-XML, size, determinism)
dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs   # generalize path source
```

### Pattern 1: Image-only twin of a base invoice (verified end to end)
**What:** Re-render the base PDF with pdfium, degrade the raster, JPEG-encode it, and wrap it with reportlab. The page is a single image object and no text objects.
**When to use:** every non-`text` rendering.
**Example:** see Code Examples 1 and 2.
Key verified facts:
- reportlab emits the JPEG inside `/Filter [ /ASCII85Decode /DCTDecode ]` by default; `rl_config.useA85` defaults to `1`. Set `rl_config.useA85 = 0` before creating the canvas: ~25% smaller, still byte-deterministic.
- `Canvas(invariant=1)` gives `/CreationDate (D:19800101000000+00'00')` and `/Author (anonymous)`, `/Title (untitled)`; no case id or key reaches the metadata.
- Two fresh processes produced identical SHA-256 for rotate + blur + noise + JPEG + reportlab (run this session on this machine).

### Pattern 2: Rendering catalogue and measured behaviour (200 DPI base)
Measured on skeleton cases (single page `case-001`; two pages `case-003`), JPEG gray q50 optimized, A85 off:

| Rendering | Single-page size | 2-page size | Barcode readable at 200 DPI |
|-----------|------------------|-------------|------------------------------|
| text (original) | ~5 KB | ~15 KB | yes |
| image_clean (200 DPI) | 239 KB | 686 KB | yes |
| rotation 3.5° (bicubic, white fill) | 261 KB | 736 KB | yes (1.0° and 3.5° both) |
| blur sigma 2.4 | 106 KB | 272 KB | **no** |
| low DPI 110 (re-raster) | 98 KB | 271 KB | **no** |
| low DPI 72 (re-raster) | 49 KB | 132 KB | **no** |
| barcode_damaged (3 pale bands over the bars) | ≈ image_clean | ≈ image_clean | **no** (digits under the code stay legible) |

(Sizes for blur 2.4, 110, 72, clean and rotation are measured at q50; mild blur and barcode_damaged are estimated from q60 runs and the same ratios.) So natural unreadable-barcode slices come from blur and low DPI, and rotation alone does not kill the barcode: both facts are exactly what D-09's second flag records.

### Pattern 3: ICMS groups for the new tax codes (renderer, mapper and validators verified)
BrazilFiscalReport reads `orig`, `CST`/`CSOSN`, `vBC`, `pICMS`, `vICMS` from whichever child of `<ICMS>` exists (`danfe.py` product loop: `extract_text(el_imp_ICMS, "vBC")` etc.), so groups render without renderer changes. Verified by rendering CST 10, 30, 51 and 70 items: the table printed `010`, `030`, `051`, `070` and the Python mapper returned the group's own vBC/pICMS/vICMS (CST 30 returned `0.00`s because the group has none).

The .NET validators classify by the code without the origin digit. `[VERIFIED: dotnet/src/Carimbo.Validation/ArithmeticRules.cs:233-257]`:
```csharp
"00" or "20" or "90" => IcmsTreatment.Taxed,
"40" or "41" or "50" or "60" => IcmsTreatment.NotTaxed,
_ => IcmsTreatment.Unsupported,
...
"900" => IcmsTreatment.Taxed,
"101" or "102" or "103" or "300" or "400" or "500" => IcmsTreatment.NotTaxed,
_ => IcmsTreatment.Unsupported,
```
Implications for the generator (all arithmetic rules read only the DANFE-visible columns and totals):

| Code | XML group | Writer must satisfy |
|------|-----------|---------------------|
| CST 00 / 20 / 90 (Taxed) | ICMS00 / ICMS20 / ICMS90 | `vICMS` = round(vBC × pICMS / 100) within 0.01 (`TAX_ARITH_ICMS`) |
| CST 40 / 41 / 50 / 60 (NotTaxed) | ICMS40 (40, 41, 50) / ICMS60 | `vICMS` absent or `0.00` (`TAX_NOT_TAXED_AMOUNT`); omit `vICMSDeson` (the mapping doc assumes it is zero) |
| CST 10 / 30 / 70 / 51 | ICMS10 / ICMS30 / ICMS70 / ICMS51 | warning `TAX_CODE_UNSUPPORTED` only; but the item's `vBC`/`vICMS` still feed `TOTAL_SUM_ICMS_BASE` / `TOTAL_SUM_ICMS`, and the **header** `vBCST`/`vST` feed vNF |
| CSOSN 101/102/103/300/400/500 | ICMSSN101, ICMSSN102 (102, 103, 300, 400), ICMSSN500 | `vICMS` absent → 0.00 |
| CSOSN 900 | ICMSSN900 | taxed: `vICMS` = round(vBC × pICMS / 100) |
| CSOSN 201/202/203 | — | not in the CONTEXT list; would also warn. Skip. |

Also quoted (the totals rule the ST items must respect): `vNF = products - discount + ICMS ST + freight + insurance + other expenses + IPI` (`ArithmeticRules.cs`, `AppendTotals`). `CaseSpec.invoice_total` is currently `self.products_total - self.discount + self.freight + self.ipi_total` with the docstring "(ST, seg, outro are 0)" (`spec.py:217-220`): add `st_total` and `st_base_total` and include them in vNF and in `_ICMS_TOT_ORDER` values (`vBCST`, `vST`). Keep every new field default-zero so the three skeleton specs, and therefore skeleton bytes, do not change.

The "mixed per-item tax codes" trait (D-04) is just a list of codes of one regime family cycled or drawn per item; `REGIME_CODE_MISMATCH` fires only when 3-digit and 4-digit codes mix (`MixesRegimeCodeLengths`). **Per-item discount:** the skeleton already writes per-item `vDesc`/`vFrete` inside `prod` (first item of case-002) and the DANFE prints neither per item (`docs/DANFE-MAPPING.md`: "Per-item discount (BrazilFiscalReport has no discount column)"). `vProd` stays gross, so qty × unit_price = total is unaffected and the D-04 condition holds: distribute the invoice discount/freight over a random subset of items, with `Σ item = header` exactly.

### Pattern 4: Per-case seeding
Keep the established scheme: `random.Random(f"{master_seed}:{case_id}")` plus `fake.seed_instance(rng.getrandbits(64))` (`spec.py:381-383`). Use a **new master seed** (Claude's discretion) so the dataset is independent of the skeleton. For degradation, derive a stable integer from `sha256(f"{seed}:{case_id}:scan")` and feed `numpy.random.default_rng`. Never use a module-level RNG: adding a case must not change any other case (PITFALLS 6).

### Pattern 5: Deterministic stratified split and CI subset
- Assign `split` **per base** (D-14); twins inherit it. Order the bases of each stratum by `sha256(f"{seed}:split:{base_id}")` and give the first `ceil(n/3)` to `dev`. Strata with n < 2 are a quota-table error (test asserts every stratum has both splits).
- Choose the twin bases (see Open Question 1) with an explicit dev/test count per rendering in the quota table, so the dev split is guaranteed to contain every rendering the CI subset needs.
- CI subset rule: iterate a committed ordered list of "required cells" (both regimes, multi-page, many items, alphanumeric CNPJ, CPF recipient, ST warning, `image_clean`, each degradation type at both severities, `barcode_damaged`); for each cell pick the dev case with the smallest `sha256(f"{seed}:ci:{case_id}:{cell}")` not already chosen. Prefer single-page bases for image cells to bound live cost. A test asserts membership ⊆ dev and that every required cell is covered.

### Pattern 6: Quota-table strawman (recommendation; the planner and executor finalize counts)
Bases (text-layer, 90): regime (Simples / Normal, 45 each) × length class, with overlay traits drawn inside the cells.

| Length class | Items | Pages | Simples | Normal | Total |
|--------------|-------|-------|---------|--------|-------|
| short | 1 to 8 | 1 | 14 | 14 | 28 |
| medium | 9 to 25 | 1 | 14 | 14 | 28 |
| many_items | 26 to 45 (short words) | 1 | 7 | 7 | 14 |
| multi_page | 46 to 80, or 18 to 30 with long descriptions | ≥2 | 10 | 10 | 20 |

Overlay traits (counts are targets): alphanumeric issuer CNPJ about 24 (27%), alphanumeric recipient CNPJ a few, CPF recipient about 18, ST/deferral (CST 10/30/51/70, Normal only) about 14, mixed per-item codes about 30, long text about 18, confusable items about 18, CRT 4 (MEI) 3 to 4 (optional). Twins: 8 curve bases × 8 renderings (`image_clean`, rotation mild/severe, blur mild/severe, low_dpi mild/severe, `barcode_damaged`) = 64, giving 154 cases. Splits per cell: 14 → 5 dev, 7 → 2 dev, 10 → 3 dev (30 dev bases, exactly 1/3); the 8 curve bases are split 3 dev / 5 test, so dev holds 30 + 24 = 54 cases (35%) and every rendering has dev members. CI subset (about 20, all dev): 12 text bases (each regime, short, medium, many_items, multi_page, alphanumeric, CPF, ST warning, plus fillers) + 8 twins (`image_clean`, rotation, blur, low_dpi at both severities where one case per cell is affordable, `barcode_damaged`).

Invariants to assert, whatever the final counts: total ≥150; alphanumeric CNPJ ≥10% over all cases **and** over distinct bases; each rendering n ≥7; every cell has both splits; twin XML bytes equal the base XML bytes; `ci_subset` ⊆ dev.

### Anti-Patterns to Avoid
- **Re-tuning degradation after the first commit.** Each image-PDF regeneration is ~15 MB of new git objects and a new `dataset_version`; tune on a scratch directory first.
- **Editing `build_case_spec` or `_profiles()` for the dataset.** The skeleton bytes are guarded by `datagen-check`; add new functions instead.
- **Deriving `expected` from the XML.** Keep `_expected(spec)` spec-derived so the independent Python/.NET readers stay a real oracle (existing design, `cli.py`).
- **Predicting page count from item count.** Record what the renderer produced and assert the stratum against it.
- **Per-pixel noise at high sigma on every twin.** Cheap in bytes at q50 (measured: sigma 4 and 8 stayed within 5% of the noiseless rotated size), but it is untuned realism; keep it small or skip it.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Image-only PDF writer | Raw PDF object writer, or Pillow `save(format="PDF")` | reportlab `Canvas(invariant=1)` with `rl_config.useA85 = 0` | Pillow's writer embeds timestamps (verified in STACK spikes; AGENTS forbids it). reportlab is deterministic. |
| Text-layer proof | Regex over PDF bytes | pypdfium2 `page.get_objects()` + `get_textpage().get_text_range()` | Streams are compressed; pdfium is the same engine a provider uses. Measured: text PDF has 167 text objects, twin has 0 text objects and 1 image object. |
| Barcode decode | Own Code 128 reader | zxing-cpp `read_barcodes` | Same engine as the .NET `ZXingCpp`; handles alphanumeric keys (case-003 decodes at 200 DPI). |
| CNPJ/CPF/key check digits | New algorithm | `carimbo_datagen.ids` | Already tested against `data/vectors/validator-vectors.json`. |
| XML → Invoice mapping | A third mapper | `NfeXmlMapper` (.NET) and `carimbo_evals.ground_truth.invoice_from_xml` (Python) | The gate must reuse the existing oracles (D-18). |
| JSON-Schema validation | Custom checks | `carimbo_evals.grader.load_schema_validator` (Draft 2020-12) | Already used by the grader and its self-tests. |
| Stratified assignment | Random shuffles with global RNG | `sha256`-ordered selection per stratum | Stable when a case is added; no global state. |

**Key insight:** every hard part (layout, barcode, check digits, mapping, validators, schema) already exists and is tested. The new work is data design (quota table), three additive code paths (ICMS groups with ST totals, scan pipeline, manifest/split) and generalizing two tests from a hard-coded path.

## Common Pitfalls

### Pitfall 1: Twins break the runner and grader pairing
**What goes wrong:** D-06 says twins share the base's XML. The runner does `discover_cases`: `pdfs = sorted(cases_dir.glob("*.pdf"), key=lambda p: p.stem)` then raises `PDF without a matching XML` unless `p.with_suffix(".xml").is_file()` (`runner.py:115-121`), and the grader loads `load_ground_truth(cases_dir / f"{case_id}.xml")` (`grader.py:385`). The .NET gate also reads `manifest cases[].xml`.
**Why it happens:** the pairing is by filename stem; a twin with no XML of its own is an error before any model call.
**How to avoid:** write **one XML per case**; a twin's XML is a byte copy of its base's XML (`xml_sha256` equal, recorded as such). Costs ~10 KB per twin (~0.6 MB total). Phase 5 can later key on `base_case`. Do not change the runner in this phase.
**Warning signs:** `ValueError: PDF without a matching XML`.

### Pitfall 2: Page count is not item count
**What goes wrong:** a `multi_page` stratum defined as "≥46 items" misses invoices with long descriptions, and a `many_items` single-page stratum can spill. Measured on the real renderer (case-001 cloned to N items): short descriptions give 1 page up to 45 items, 2 pages at 50 to 100, 3 pages at 140; 120-character descriptions give 2 pages already at 20 items, 3 at 45, 5 at 100, 9 at 200.
**How to avoid:** the generator records `pages` from `render_danfe`'s return value and asserts it against the stratum's page band (`single_page` ⇒ 1, `multi_page` ⇒ ≥2) before writing; a pytest asserts it again from the committed PDFs with pdfium. Cap many-items at about 80 so the live extraction output stays well under `InvoiceExtractor.MaxTokens = 16000` (`dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:67`; the 50-item skeleton case produced a 1-attempt success at ~US$0.046 on Haiku).
**Warning signs:** a tag/page-count mismatch in a manifest invariants test.

### Pitfall 3: A long token is broken mid-token on the DANFE
**What goes wrong:** a 52-character description token and a 40-character product code were printed split across lines (`...CABECACHATAROSCAGROS SA5MMX40MM`). The XML ground truth is the unbroken string, so the printed form and ground truth differ in whitespace, which silently violates D-05.
**How to avoid:** build descriptions from words ≤ ~20 characters and product codes ≤ ~12; the D-05 datagen test compares **with all whitespace removed** from both sides (the normalized text contained the stripped string in both probes) and still asserts every graded string (names, descriptions, codes, NCM, unit, natOp, quantities and amounts in pt-BR format, access key without spaces) is present in the page text. Names of 60 characters (issuer, recipient), natOp of 60 and descriptions of 120 characters all printed intact (word-wrapped).
**Warning signs:** the text-equals-XML test fails only for a long-code or long-description case.

### Pitfall 4: `barcode_readable` depends on the decode resolution
**What goes wrong:** measured with zxing-cpp on the committed skeleton PDFs rendered by pdfium: the **text** PDF decodes at 200 and 300 DPI but **not at 150**; a 200 DPI `image_clean` twin decodes at 200 but, for the two-page case, **not** at 300 (JPEG upscaled), while the single-page one decodes at both. A flag with no stated decode DPI is meaningless.
**How to avoid:** define the flag once as "zxing-cpp finds a Code 128 equal to the access key in page 1 rendered by pdfium at **200 DPI**" (the scan DPI), record `barcode_decode_dpi: 200` in the manifest generator block, and compute it from the **final PDF bytes**, not from the in-memory image. Build `image_clean` at 200 DPI (not 150) so it reads as readable and `barcode_damaged` is a real contrast. Expect the `text` rendering to be 100% readable (verify across all 90 bases, since the key mixes letters for alphanumeric CNPJs).
**Warning signs:** `image_clean` twins with `barcode_readable: false`.

### Pitfall 5: ASCII85 inflation and size drift
**What goes wrong:** reportlab's default wraps JPEG in ASCII85 (+25%): single-page `image_clean` is 297 KB with A85 off at q60 non-optimized vs ~380 KB with it on. Left on, the full set approaches or crosses 20 MB.
**How to avoid:** `rl_config.useA85 = 0`; q50 with `optimize=True`; add a pytest asserting total committed dataset bytes ≤ 20,000,000 (decimal MB, a hard line per D-12) and a lower tripwire (e.g. warn at 18 MB) so drift is visible before the cap.
**Warning signs:** `du -sb data/dataset` above 18 MB.

### Pitfall 6: Byte determinism is Linux x64 only
**What goes wrong:** pdfium rasterization, libjpeg-turbo and Pillow's resampling are bit-identical across two fresh processes here, but not proven across CPU architectures or Pillow/pdfium builds.
**How to avoid:** D-13 already scopes the promise to Linux x64. Keep the check in CI as the arbiter, and have the first CI run of `dataset-check` pass **before** any image PDF is committed as the freeze point. Record `pypdfium2`, `pillow`, `numpy`, `reportlab`, `zxing-cpp` versions (and optionally `pdfium.PDFIUM_INFO`, `PIL.features.version("jpg")`) in the manifest generator block. No `arm64` runner.
**Warning signs:** CI `dataset-check` red while local is green.

### Pitfall 7: Mixed regime families and CRT handling
**What goes wrong:** the mapper and BrazilFiscalReport choose CSOSN for CRT 1 and 4, CST otherwise. A CRT-3 invoice with a CSOSN group (or the reverse) maps to a missing-element exception.
**How to avoid:** one regime family per invoice by construction (Simples/MEI → CSOSN codes, Normal → CST codes), enforced in the spec builder, not only by the validator. CRT 4 (MEI) is a cheap optional extra regime (CSOSN 102/103/400 only); CRT 2 behaves like Normal for the code column.

### Pitfall 8: Confusable and "many digits" values must still round-trip
**What goes wrong:** near-identical items (same description, different quantity/price) and totals with 10+ digits are the intended bait, but any value over C# `decimal` precision or over the schema `Money` pattern breaks the gate.
**How to avoid:** keep vNF < 10^13 (XSD `TDec_1302` has 13 integer digits), keep quantities/prices at ≤ 4 decimals, compute with `Decimal` and `ROUND_HALF_UP` exactly as `spec.py` does. The gate (`ITEM_ARITH` within 0.01, totals sums within `min(0.01 × n, 1.00)`) will catch a slip.

### Pitfall 9: The skeleton XML is not XSD-valid today (CEP)
**What goes wrong:** `nfe_xml.py`'s docstring says XSD validation "is DATA-04 in Phase 4", but D-18 defines DATA-04 as JSON Schema + .NET validators. Probed this session: with nfelib's `nfe_v4.00.xsd` (validating the `NFe` element, a dummy `Signature` appended), all three skeleton XMLs fail **only** on the hyphenated CEP (`'90533-628' is not accepted by the pattern '[0-9]{8}'`). A hand-written ICMS51 with the wrong child order also failed (`vBC ... Expected is ( pRedBC )`), so XSD validation does catch element-order mistakes in new groups.
**How to avoid:** do not make XSD a dataset-wide gate (changing the CEP format would change skeleton bytes if the shared `_party` is edited; a dataset-only flag is needed). Instead add a small unit test that renders each new ICMS group through the XSD with a fixed fixture. Mark the stale docstring for correction in the plan.

### Pitfall 10: Leakage channels in the new PDFs
**What goes wrong:** PITFALLS #5. reportlab metadata is clean (`Author (anonymous)`, `Title (untitled)`, fixed 1980 dates); the BrazilFiscalReport text PDF has `/Title (DANFE)` and a fixed 2026-01-01 creation date. The file name `nfe-0042.pdf` and the runner payload are Phase 3/6 boundary concerns.
**How to avoid:** a leak test greps every twin's bytes for the case id, base id and access key (twins should contain none; text PDFs legitimately contain the key). Case ids must be neutral (`nfe-0042`), never encode the rendering (`nfe-0042-blur`), and `base_case` lives only in the manifest.

### Pitfall 11: Git history and cache-fixture churn
**What goes wrong:** ~15 MB of JPEG PDFs do not delta-compress. Re-generating twice costs ~30 MB of history, and Phase 6 cache fixtures key on PDF SHA-256.
**How to avoid:** tune parameters in a scratch directory (outside the repo, per the footprint rule), freeze, commit once. Treat any later byte change as a new `dataset_version` (D-17).

### Pitfall 12: Time budget of the regeneration check
**What goes wrong:** D-13 moves the check to a path-filtered job above ~2 to 3 minutes. Measured unit costs on this machine: DANFE render 0.1 s (5 items) to 1.0 s (100 items); raster 22 ms/page; rotate 75 ms; blur 27 ms; JPEG 11 ms; reportlab 15 ms; zxing decode 52 ms. Projection for 90 bases + 64 twins + 154 flag decodes is well under 2 minutes single-process on a 20-core machine; a 2-core GitHub runner is slower (~2 to 3x), so it must be measured on the first CI run, not assumed.
**How to avoid:** make generation order-independent per case so a `ProcessPoolExecutor` can be added if the CI measurement exceeds the budget (results are sorted by case id before writing). Do not parallelize speculatively.

## Code Examples

All snippets were executed in a scratch environment this session (`uv run --project python --with numpy==2.5.3 --with reportlab==5.0.1`), except where noted.

### 1. Image-only PDF from degraded pages (deterministic)
```python
# Source: run in this session; reportlab config verified by inspecting the /Filter in the output
import io
from reportlab import rl_config
rl_config.useA85 = 0                      # default 1 wraps the JPEG in ASCII85 (+25% bytes)
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas
from PIL import Image

A4_PT = (595.2756, 841.8898)

def write_image_only_pdf(pages: list[Image.Image], path, quality: int = 50) -> None:
    c = canvas.Canvas(str(path), pagesize=A4_PT, invariant=1, pageCompression=1)
    for page in pages:                    # mode "L" pages
        buf = io.BytesIO()
        page.save(buf, "JPEG", quality=quality, optimize=True)
        buf.seek(0)
        c.drawImage(ImageReader(buf), 0, 0, width=A4_PT[0], height=A4_PT[1])
        c.showPage()
    c.save()
```

### 2. Raster and degradations (Pillow/pdfium; parameters are strawman, tune once by eye)
```python
# Source: run in this session
import pypdfium2 as pdfium
from PIL import Image, ImageFilter, ImageDraw

def raster(pdf_path, dpi: int) -> list[Image.Image]:
    pdf = pdfium.PdfDocument(str(pdf_path))
    try:
        # pypdfium2 types scale as int but documents a float (existing tests carry a pyright ignore)
        return [p.render(scale=dpi / 72).to_pil().convert("L") for p in pdf]  # pyright: ignore[reportArgumentType]
    finally:
        pdf.close()

def rotate(im, deg):  return im.rotate(deg, resample=Image.Resampling.BICUBIC, fillcolor=255)
def blur(im, sigma):  return im.filter(ImageFilter.GaussianBlur(sigma))
# low DPI = raster(pdf, 110) / raster(pdf, 72) directly (fewer pixels embedded at the same page size)
# barcode_damaged: locate the Code 128 with zxingcpp on a 300 DPI render, scale its box to the scan DPI,
# then draw 2 to 3 pale vertical bands (gray 215..245) across the bars, leaving the printed key digits intact.
```
Visually checked at 200 DPI: blur sigma 2.4 and 72 DPI both keep header fields and the key legible while the 5 pt labels degrade: a reasonable "severe but human-readable". Mild levels (strawman): rotation 1.0°, blur 1.2, 110 DPI; severe: 3.5°, 2.4, 72 DPI. Record them in the manifest generator block.

### 3. Proofs used by tests
```python
# Source: run in this session
import pypdfium2 as pdfium, pypdfium2.raw as raw, zxingcpp

def is_image_only(path) -> bool:
    pdf = pdfium.PdfDocument(str(path))
    try:
        for page in pdf:
            kinds = [o.type for o in page.get_objects()]
            if raw.FPDF_PAGEOBJ_TEXT in kinds or page.get_textpage().get_text_range():
                return False
        return True
    finally:
        pdf.close()
# measured: text PDF -> 167 text objects / 2570 chars; twin -> 0 text objects, 1 image object, 0 chars

def barcode_readable(path, access_key: str, dpi: int = 200) -> bool:
    pdf = pdfium.PdfDocument(str(path))
    try:
        image = pdf[0].render(scale=dpi / 72).to_pil()  # pyright: ignore[reportArgumentType]
    finally:
        pdf.close()
    return any(
        r.text == access_key and r.format == zxingcpp.BarcodeFormat.Code128
        for r in zxingcpp.read_barcodes(image)
    )
```

### 4. ICMS group writers (shape only; element order per the NF-e 4.00 XSD, to be confirmed by the XSD unit test)
```python
# Source: structure tested through BrazilFiscalReport and invoice_from_xml in this session;
# order of ICMS51 children must be re-checked against nfe_v4.00.xsd (a first attempt failed on pRedBC/vBC order)
ICMS10 = "orig CST modBC vBC pICMS vICMS modBCST pMVAST vBCST pICMSST vICMSST"
ICMS30 = "orig CST modBCST pMVAST vBCST pICMSST vICMSST"
ICMS70 = "orig CST modBC vBC pICMS vICMS modBCST vBCST pICMSST vICMSST"
ICMS40 = "orig CST"          # CST 40, 41, 50; omit vICMSDeson
ICMS60 = "orig CST"          # omit vBCSTRet / vICMSSTRet
ICMSSN500 = "orig CSOSN"
ICMSSN900 = "orig CSOSN modBC vBC pICMS vICMS"
```

### 5. Skeleton-compatible, additive spec fields
```python
# Source: derived from spec.py:217-220 (read this session); new fields default to zero so skeleton bytes are unchanged
@property
def invoice_total(self) -> Decimal:
    return self.products_total - self.discount + self.st_total + self.freight + self.ipi_total
```

### 6. Manifest v2 (additive to the skeleton shape, D-17)
```json
{
  "dataset": "dataset", "dataset_version": "dataset-001", "as_of_date": "2026-10-01",
  "master_seed": 0, "python": "3.12",
  "generator": { "brazilfiscalreport": "1.2.0", "carimbo": "0.1.0", "faker": "40.40.0", "fpdf2": "2.8.9",
                 "lxml": "6.1.3", "python-barcode": "0.16.1", "pillow": "12.3.0", "numpy": "2.5.3",
                 "pypdfium2": "5.13.0", "reportlab": "5.0.1", "zxing-cpp": "3.1.1",
                 "barcode_decode_dpi": 200, "scan_dpi": 200, "jpeg_quality": 50,
                 "degradation": {"rotation": {"mild": 1.0, "severe": 3.5}, "blur": {"mild": 1.2, "severe": 2.4},
                                 "low_dpi": {"mild": 110, "severe": 72}} },
  "quota_sha256": "…", "ci_subset": ["nfe-0003", "…"],
  "cases": [ { "case_id": "nfe-0091", "base_case": "nfe-0007", "rendering": "rot", "severity": "mild",
               "split": "dev", "xml": "nfe-0091.xml", "xml_sha256": "…", "pdf": "nfe-0091.pdf",
               "pdf_sha256": "…", "pages": 1, "barcode_readable": true, "tags": ["…"], "expected": { } } ]
}
```
Keep the existing keys (`dataset`, `dataset_version`, `as_of_date`, `master_seed`, `generator`, `cases[].case_id/xml/xml_sha256/pdf/pdf_sha256/pages/tags/expected`): `carimbo_evals.grader._load_manifest` reads `dataset` and `dataset_version` (`grader.py:359-369`) and `runner.read_as_of_date` reads `as_of_date`. For a base (text) case use `base_case: null` or its own id, `rendering: "text"`, `severity: null`; pick one convention and test it. Skeleton `generator` values are all version strings, so putting numeric DPI/quality there is a shape change: prefer a sibling `scan` block for non-version parameters.

### 7. Generalizing the .NET gate
```csharp
// Source: GroundTruthGateTests.cs reads RepoFiles.PathOf("data", "skeleton", ...) at lines 43, 61, 77 (read this session)
// Make the dataset directory a parameter of one shared helper, keep the existing skeleton class as-is, and add
// a Dataset class whose TheoryData comes from data/dataset/manifest.json. Keep: no Error findings at as_of_date,
// the expected-block assertions, and the wire round-trip with Assert.Empty(again.PatternViolations()).
```
`dotnet test --project tests/Carimbo.Validation.Tests -- --filter-class Carimbo.Validation.Tests.GroundTruthGateTests` runs the current 12 gate tests in under 1 s of test time (about 6 s wall, measured), so ~154 × 3 theory cases stay cheap.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Three hand-written profiles (`_profiles()`) | Quota table → seeded specs | this phase | Known n per slice for Phase 5 |
| Shared XML per twin (CONTEXT D-06 wording) | One byte-identical XML copy per case | this research | No runner/grader change needed |
| Pillow PDF writer for scans | pdfium raster → JPEG → reportlab `invariant=1` | Phase 1 spike | Byte-deterministic image-only PDFs |
| ST/deferral as "unsupported" tax logic | Generated with `TAX_CODE_UNSUPPORTED` warning allowed | D-04 | Exercises the warning path the decision agent later reads |

**Deprecated/outdated:**
- The `nfe_xml.py` module docstring claim that XSD validation is DATA-04: superseded by D-18 (JSON Schema + .NET validators). Correct it in the plan.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The 15 to 17 MB projection holds for the full 154 cases (extrapolated from single-page/two-page measurements and an assumed mix of 8 single-page curve bases plus one or two multi-page) | Summary, Pitfall 5 | The set exceeds ~20 MB: D-12 fallback applies (commit XML, text PDFs, manifest, CI-subset image PDFs only) and a DECISIONS entry is needed |
| A2 | libjpeg-turbo/Pillow/pdfium output is bit-identical between this WSL machine and the GitHub `ubuntu-latest` x64 runner | Pitfall 6 | `dataset-check` fails in CI only; the committed bytes would have to be regenerated on the CI platform or the fallback applied |
| A3 | A 2-core CI runner regenerates the dataset in under ~3 minutes | Pitfall 12 | Move the check to a path-filtered job (D-13 already allows it) |
| A4 | reportlab is untyped and pyright 1.1.414 in `standard` mode accepts the imports without stubs | Standard Stack | A few `# pyright: ignore` comments or a `py.typed` workaround (not tested here) |
| A5 | The strawman degradation parameters (1.0°/3.5°, blur 1.2/2.4, 110/72 DPI) give "mild" and "severe but human-readable" | Pattern 2 | Retune once on a scratch directory before first commit |
| A6 | NF-e XSD element order for ICMS10/30/70/51 as listed in Code Example 4 (a first hand-written ICMS51 failed order validation) | Pattern 3 | Only the optional XSD unit test would reveal it; the renderer, mapper and validators do not care about order |
| A7 | Eight "curve" bases (each with all eight renderings) are preferable to spreading twins over more bases | Open Question 1 | Narrow content diversity in image slices; revisit in the quota table |

## Open Questions (RESOLVED)

1. **How are twins distributed over bases?**
   - What we know: 8 renderings × about 8 each = 64 twins (154 total). D-06 wants one invoice compared across renderings; D-10 says "drawn from a stratified subset of the bases".
   - What's unclear: 8 bases × all 8 renderings (full degradation curves, n=8 of the *same* invoices per slice, narrow content) versus about 16 bases × about 4 renderings (broader content, partial curves).
   - Recommendation: 8 curve bases, all single page except at most one two-page base, covering both regimes, one CPF recipient, two alphanumeric issuers and one ST/warning invoice; 3 of the 8 in `dev` so every rendering has dev members for the CI subset. Put the choice into the quota table and a DECISIONS D-40 entry.
   - RESOLVED: eight curve bases, each with all eight renderings (64 twins, 154 cases), all single page (validate_quota admits curve slots only in single-page cells, so no two-page curve base), three dev and five test (nfe-0001, nfe-0003, nfe-0019, nfe-0020, nfe-0048, nfe-0049, nfe-0064, nfe-0065), covering both regimes, one CPF recipient, two alphanumeric issuers, one ST/warning and one mixed-codes invoice — plan 04-07 Task 1 (recorded in D-40 by plan 04-08 Task 1).

2. **Per-slice sample sizes for Phase 5.**
   - What we know: 8 per rendering slice is what the CONTEXT target implies; Phase 5 will report intervals.
   - What's unclear: whether 8 is enough to be useful; PITFALLS 13 warns about tiny strata.
   - Recommendation: keep 8 (budget and size), say so in the manifest README/DECISIONS, and let paired comparison (twins) carry the degradation story.
   - RESOLVED: n = 8 per rendering slice, kept; the paired twins carry the degradation story — plan 04-07 Task 1 (quota table) and plan 04-08 Task 1 (stated in D-40).

3. **Dataset directory and id scheme.** Discretion: `data/dataset/` with `nfe-0001...` is recommended (neutral, no rendering in the name, sorted order = manifest order).
   - RESOLVED: `data/dataset/` with ids `nfe-0001` upward (bases in quota order, then twins), dataset "dataset", version "dataset-001", master seed 20261022, as_of_date 2026-10-01 — plan 04-04 Task 1.

4. **Manifest numeric parameters.** Where should DPI/quality/severity parameters live (`generator` versus a `scan` block)? Recommend a sibling `scan` block so `generator` stays a map of version strings.
   - RESOLVED: inside the manifest's `generator` block, as a nested `generator.scan` object filled from `scan.scan_parameters()`, because CONTEXT D-08 records the parameters "in the manifest's generator block" (the sibling block recommended above was not taken; `generator` keeps its package-version strings next to the nested object) — plan 04-04 Task 1 (`scan_parameters()` itself: plan 04-03 Task 2).

5. **Should CRT 4 (MEI) be added as a third regime?** Cheap (CSOSN 102/103/400, renderer and mapper already handle CRT 4) and strengthens "several tax regimes", but not required by D-04. Recommend 3 to 4 bases, tagged `mei`.
   - RESOLVED: yes, 4 MEI bases (CRT 4, codes only from CSOSN 102, 103 and 400, tag `mei`), two each in simples-short and simples-medium — plan 04-05 Task 2 (the CRT 4 writer support: plan 04-01 Task 2).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| uv | all Python recipes | yes | 0.12.11 | — |
| Python 3.12 (project venv) | generator | yes | 3.12 (`python/.venv`) | — |
| just | recipes | yes | 1.58.0 | — |
| .NET SDK | gate tests | yes | 10.0.400 (`global.json` pins the SDK) | — |
| devenv | shell | yes | 2.4.0 | uv/dotnet already on PATH |
| numpy 2.5.3, reportlab 5.0.1 | scan pipeline | not in lock; installable (ran via `uv run --with`) | 2.5.3 / 5.0.1 | none: locked by CONTEXT |
| pypdfium2, zxing-cpp, Pillow | scan + flag | yes (lock) | 5.13.0 / 3.1.1 / 12.3.0 | — |
| Network to PyPI | `uv lock` | yes (PyPI JSON reachable) | — | — |
| Docker / Postgres / Jaeger | — | not needed | — | Phase 4 makes no model or service call |

**Missing dependencies with no fallback:** none.
**Missing dependencies with fallback:** none.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | pytest 9.1.1 (+ pytest-asyncio 1.4.0) for Python; xUnit v3 4.0.1 on Microsoft.Testing.Platform for .NET |
| Config file | `python/pyproject.toml` `[tool.pytest.ini_options]` (default selection excludes `e2e` and `live`); `global.json` |
| Quick run command | `uv run --project python pytest python/tests/test_dataset_*.py -q` (offline, committed files only) |
| Full suite command | `just check` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| DATA-02 | ≥150 cases; ≥10% alphanumeric CNPJ over **all cases and over distinct bases**; both regimes; single, many-items and multi-page present; each rendering n ≥ 7; tags consistent with rendered `pages` | unit (manifest) | `uv run --project python pytest python/tests/test_dataset_manifest.py -q` | ❌ Wave 0 |
| DATA-02 | ST/deferral (CST 10/30/51/70), mixed per-item codes, CSOSN 103/300/500/900 appear and map; arithmetic holds | unit | `pytest python/tests/test_dataset_spec.py -q` | ❌ Wave 0 |
| DATA-03 | Every non-`text` PDF: zero text objects, zero text chars, ≥1 image object per page | unit (pdfium) | `pytest python/tests/test_dataset_scans.py::test_image_renderings_have_no_text_layer -q` | ❌ Wave 0 |
| DATA-03 | `barcode_damaged` flag false at the fixed DPI, `image_clean` true, text true; flag recomputed from committed bytes equals manifest | unit | `pytest python/tests/test_dataset_scans.py -q` | ❌ Wave 0 |
| DATA-03 | D-05: every graded string of the XML is present in each text PDF's whitespace-stripped text | unit | `pytest python/tests/test_dataset_text_equals_xml.py -q` | ❌ Wave 0 |
| DATA-04 | .NET: mapper + validators, zero `Error` findings at `as_of_date`, expected block, wire round trip, for every manifest case | .NET xUnit theory | `cd dotnet && dotnet test --project tests/Carimbo.Validation.Tests -- --filter-class Carimbo.Validation.Tests.DatasetGroundTruthGateTests` | ❌ Wave 0 |
| DATA-04 | Python: `invoice_from_xml` → `jsonschema` Draft 2020-12 over `schema/invoice.schema.json`, and strict Pydantic validation, for every manifest case | unit | `pytest python/tests/test_dataset_ground_truth.py -q` | ❌ Wave 0 |
| DATA-05 | Manifest hashes match files; `split` ∈ {dev, test}; twins share base split and XML bytes; every stratum has both splits; dev ≈ 1/3 of bases; `ci_subset` ⊆ dev, ≈20 entries, covers every required cell; quota hash matches code | unit | `pytest python/tests/test_dataset_manifest.py -q` | ❌ Wave 0 |
| DATA-05 | Manifest has no machine-specific values (paths, hostnames, timestamps) | unit | same file | ❌ Wave 0 |
| DATA-06 | Total committed dataset bytes ≤ 20,000,000 (tripwire at 18 MB) | unit | `pytest python/tests/test_dataset_manifest.py::test_dataset_size_budget -q` | ❌ Wave 0 |
| DATA-06 | Byte-identical regeneration of every file, including image PDFs, on CI Linux x64 | CLI check | `just dataset-check` | ❌ Wave 0 |
| (regression) | Skeleton bytes unchanged after the shared-code edits | CLI check | `just datagen-check` | ✅ |
| (regression) | Leak check: twins contain no case id, base id or access key in their bytes; metadata is neutral | unit | `pytest python/tests/test_dataset_scans.py::test_no_identifier_leakage -q` | ❌ Wave 0 |
| (synthetic) | `SINTETICA` marker on every party; identifiers valid and absent from nfelib samples; no repeats across cases (generalize `test_synthetic_only.py::_all_specs`) | unit | `pytest python/tests/test_synthetic_only.py -q` | ✅ (extend) |

Order-independence test (determinism): build a **subset in a different order and in a separate process**, and assert each case's bytes equal the full-build bytes (existing skeleton precedent: `test_case_spec_is_independent_of_build_order`, `test_fresh_process_rendering_matches_this_process`).

### Sampling Rate
- **Per task commit:** the quick command for the module touched, plus `just datagen-check` whenever `spec.py`/`nfe_xml.py`/`danfe.py` change.
- **Per wave merge:** `just py-check` and the .NET gate class.
- **Phase gate:** `just check` green (includes `dataset-check`), and one green CI run on the PR before any image PDF is committed as final, then `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `python/tests/test_dataset_manifest.py`: counts, strata, split, subset, hashes, size budget
- [ ] `python/tests/test_dataset_spec.py`: tax-code arithmetic and trait coverage from specs (no rendering)
- [ ] `python/tests/test_dataset_scans.py`: no text layer, barcode flags, leak check
- [ ] `python/tests/test_dataset_text_equals_xml.py`: D-05 graded-string presence
- [ ] `python/tests/test_dataset_ground_truth.py`: jsonschema + Pydantic over the manifest
- [ ] `dotnet/tests/Carimbo.Validation.Tests/DatasetGroundTruthGateTests.cs` (or a generalized `GroundTruthGateTests`)
- [ ] `justfile` recipes `dataset`, `dataset-check` and `check` wiring; `.github/workflows/ci.yml` step; `AGENTS.md` layout, command table and generated-files list; `docs/DECISIONS.md` D-40 and `docs-check` id list
- [ ] Framework install: none (pytest and xUnit exist); `uv lock` for the new pins after the supply-chain checkpoint

## Security Domain

`security_enforcement` is enabled (absent/`true` in `.planning/config.json`, ASVS level 1). This phase has no network surface, no authentication and no secrets; it reads and writes local files.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | yes (generated XML/JSON parsed by the gate) | Existing `NfeXmlMapper.Load` prohibits DTDs and sets no resolver (`XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }`); Python reader uses `xml.etree` on generator-written files; `jsonschema` for ground truth |
| V6 Cryptography | no (SHA-256 is an integrity hash, via `hashlib`) | — |
| V10 Malicious code / supply chain | yes | Exact pins + `uv.lock` hashes, human supply-chain checkpoint for numpy and reportlab, `just secrets-check` unchanged |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Typosquatted or compromised PyPI package | Tampering | Exact pins, lock diff review at a checkpoint, only packages named by the locked stack |
| Real taxpayer data entering the dataset | Information disclosure | Generated identifiers only; `SINTETICA` marker; absence-from-nfelib-samples tests extended to the dataset (DATA-07 / D-14) |
| Answer leakage through PDF metadata, file names or hidden text | Information disclosure (eval integrity) | Neutral ids, reportlab `invariant=1` metadata, leak-grep test (Pitfall 10) |
| Decompression/XML entity abuse in the gate | DoS / Tampering | DTD prohibited; inputs are generator output only |
| Provider key exposure | Information disclosure | Phase makes no live call; no key in any new recipe; `secrets-check` stays in `just check` |

## Sources

### Primary (HIGH confidence): executed or read in this session
- Repo files read in full or in part: `python/src/carimbo_datagen/{spec,nfe_xml,danfe,cli,ids}.py`, `python/pyproject.toml`, `python/uv.lock` (Pillow entry), `justfile`, `.github/workflows/ci.yml`, `dotnet/src/Carimbo.GroundTruth/NfeXmlMapper.cs`, `dotnet/src/Carimbo.Validation/{ArithmeticRules,KeyAndDateRules,Findings}.cs`, `dotnet/src/Carimbo.Domain/Invoice.cs`, `dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs`, `python/src/carimbo_evals/{runner,grader,ground_truth}.py`, `python/tests/{test_datagen,test_ground_truth,test_danfe_labels,test_synthetic_only}.py`, `docs/DANFE-MAPPING.md`, `data/skeleton/manifest.json`.
- `brazilfiscalreport/danfe/danfe.py` (installed 1.2.0): product loop and column list.
- Probes run against the real stack (scratch scripts under the session scratchpad, nothing written to the repo): page-capacity sweep, long-text wrap, ST/deferral group rendering and mapping, image-only pipeline sizes/timing/determinism (two fresh processes), barcode decode matrix, reportlab ASCII85 filter, pdfium text/image object counts, XSD validation with nfelib 3.0.0 `nfe_v4.00.xsd`, `dotnet test` gate timing, `carimbo-datagen check` timing (1.2 s for 3 cases).
- PyPI JSON API for numpy 2.5.3 and reportlab 5.0.1.

### Secondary (MEDIUM confidence)
- `.claude/CLAUDE.md` § Technology Stack and `.planning/research/PITFALLS.md` (#5, #6, #13, #21): project research, consistent with the probes.

### Tertiary (LOW confidence)
- None used.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH, all five libraries are pinned by the project and executed here.
- Architecture: HIGH, extends existing patterns; the twin-XML-copy and fixed-decode-DPI findings come from reading the runner and measuring.
- Pitfalls: HIGH for the measured ones (1 to 5, 9, 10); MEDIUM for 6 and 12 (CI-platform behaviour is only provable in CI).
- Size projection: MEDIUM (extrapolated; guarded by a byte-budget test and the D-12 fallback).

**Research date:** 2026-10-22
**Valid until:** 2026-11-21 (pinned versions; re-verify only if a pin changes)
