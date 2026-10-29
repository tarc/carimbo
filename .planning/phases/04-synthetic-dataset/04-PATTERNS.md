# Phase 4: Synthetic Dataset - Pattern Map

**Mapped:** 2026-10-22
**Files analyzed:** 14 new/modified
**Analogs found:** 13 / 14 (all analog paths verified git-tracked via `git ls-files`)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `python/src/carimbo_datagen/quota.py` (new) | config/utility | transform | `spec.py` `_Profile` / `_profiles()` | role-match |
| `python/src/carimbo_datagen/dataset_spec.py` (new) | service | transform | `spec.py` `build_case_spec` | exact |
| `python/src/carimbo_datagen/spec.py` (modify, additive) | model | transform | itself (lines 62-221) | exact |
| `python/src/carimbo_datagen/nfe_xml.py` (modify, additive) | service | transform | itself (`_icms`, `_totals`) | exact |
| `python/src/carimbo_datagen/scan.py` (new) | service | file-I/O, batch | `danfe.py` `render_danfe` (+ RESEARCH Code Examples 1-3) | partial |
| `python/src/carimbo_datagen/cli.py` (modify: dataset build/check) | controller (CLI) | batch, file-I/O | itself `build_dataset`/`build`/`check` | exact |
| `justfile` (`dataset`, `dataset-check`, add to `check`) | config | - | `datagen`/`datagen-check` (lines 47-53, 93) | exact |
| `.github/workflows/ci.yml` | config | - | `just datagen-check` step (line 46) | exact |
| `python/pyproject.toml` (+ `uv.lock`) | config | - | existing exact pins (pypdfium2/zxing-cpp in dev group) | exact |
| `dotnet/tests/Carimbo.Validation.Tests/GroundTruthGateTests.cs` (generalize) | test | batch | itself | exact |
| `python/tests/test_dataset_*.py` (new: ground truth + jsonschema, manifest invariants, no-text-layer, text-equals-XML, size, leak) | test | batch | `python/tests/test_ground_truth.py`, `test_danfe_labels.py` | role-match |
| `data/dataset/*` (generated) | generated data | - | `data/skeleton/*` | exact |
| `AGENTS.md`, `docs/DECISIONS.md` (D-40), `just docs-check` list | docs | - | existing D-39 entry / docs-check list | exact |
| `scan.py` image-twin pipeline | - | - | no repo analog (see below) | none |

## Pattern Assignments

### `python/src/carimbo_datagen/cli.py` - dataset build/check (controller, batch)

**Analog:** same file, lines 1-197. Add a parallel `build_dataset_v2`/`dataset-build`/`dataset-check` pair (or a `--dataset` selector). Do not change skeleton behavior (`datagen-check` guards it).

**Imports/constants** (lines 12-44, 64-68): `from __future__ import annotations`, `typer`, `Annotated`, constants `DATASET`, `DATASET_VERSION`, `AS_OF_DATE`, `DEFAULT_DIR`, `MANIFEST_NAME`, `_GENERATOR_PACKAGES` tuple read with `importlib.metadata.version`. Extend the tuple with `pillow`, `numpy`, `reportlab`, `pypdfium2`, `zxing-cpp`.

**Atomic write + sha helpers** (lines 71-86):
```python
def _write_atomic(path: Path, data: bytes) -> None:
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)
```

**Manifest assembly** (lines 129-140): sorted keys, no timestamps, `ensure_ascii=False`, trailing newline:
```python
text = json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
_write_atomic(out_dir / MANIFEST_NAME, text.encode("utf-8"))
```
Per-case entry shape (lines 117-128): `case_id, xml, xml_sha256, pdf, pdf_sha256, pages, tags, expected`. Add `split, base_case, rendering, severity, barcode_readable`; top level add `ci_subset`, quota-table hash, `barcode_decode_dpi`, degradation parameters. Keep `expected` spec-derived via `_expected(spec)` (lines 89-102), never read back from XML. Note `_expected` needs ST-aware `invoice_total` (spec change).

**check pattern** (lines 168-197): regenerate into `tempfile.TemporaryDirectory`, compare every file by name and bytes, report missing/extra/differing, `typer.Exit(1)`. Reuse verbatim for the dataset dir.

---

### `python/src/carimbo_datagen/dataset_spec.py` (service, transform)

**Analog:** `spec.py` `build_case_spec` (lines 377-439). Per-case RNG and Faker seeding:
```python
rng = random.Random(f"{master_seed}:{case_id}")
fake = Faker("pt_BR")
fake.seed_instance(rng.getrandbits(64))
issuer = _company(fake, rng, alphanumeric=profile.alphanumeric_issuer)
recipient = _recipient(fake, rng, profile.recipient_tax_id_kind)
while recipient.tax_id == issuer.tax_id or recipient.name == issuer.name:
    recipient = _recipient(...)
...
spec = CaseSpec(..., crt=profile.crt, ...)
installments = _installments(profile, issue_datetime.date(), spec.invoice_total)
return replace(spec, installments=installments)
```
Reuse `_company`, `_recipient`, `_item`, `_state_registration`, `make_access_key` (ids.py). Write a NEW function driven by a quota row; do not edit `build_case_spec` or `_profiles()`. Use a new master seed. Degradation seed: `sha256(f"{seed}:{case_id}:scan")` into `numpy.random.default_rng`.

---

### `python/src/carimbo_datagen/spec.py` (additive edits)

**Tax tables to extend** (lines 62-72):
```python
_ICMS_RATE = {"00": Decimal("18.00"), "20": Decimal("12.00")}
_ICMS_REDUCTION = {"20": Decimal("33.33")}
_CREDIT_RATE = {"101": Decimal("3.10")}
_ICMS_GROUP = {"00": "ICMS00", "20": "ICMS20", "101": "ICMSSN101", "102": "ICMSSN102", "400": "ICMSSN102"}
```
Add CST 10/30/40/41/50/51/60/70/90 and CSOSN 103/300/500/900 groups; note `icms_base` (line 135-141) returns zero when code not in `_ICMS_RATE`, so taxed codes (90, 900, and 10/51/70 per RESEARCH) must be added to `_ICMS_RATE`.

**Totals** (lines 217-220): `invoice_total = products_total - discount + freight + ipi_total`. Add default-zero `st_total`/`st_base_total` properties on `ItemSpec`/`CaseSpec` and include in vNF. All new fields default zero so skeleton bytes do not change.

---

### `python/src/carimbo_datagen/nfe_xml.py` (additive edits)

**ICMS writer to extend** (lines 120-135):
```python
def _icms(imposto, item):
    group = _add(_add(imposto, "ICMS"), item.icms_group)
    _add(group, "orig", "0")
    if item.icms_group in ("ICMS00", "ICMS20"):
        _add(group, "CST", item.tax_code); _add(group, "modBC", _MOD_BC_VALUE)
        ...
        return
    _add(group, "CSOSN", item.tax_code)
```
Add branches for ICMS10/30/40/51/60/70/90 and ICMSSN500/900; child order per NF-e XSD (RESEARCH Code Example 4; verify with the XSD unit test).

**Totals** (lines 179-198): `values` dict keyed by `_ICMS_TOT_ORDER` (lines 25-45); add `"vBCST"` and `"vST"` entries. Keep text short-word (<=20 chars per token) for DANFE wrapping (RESEARCH Pitfall 3).

---

### `python/src/carimbo_datagen/danfe.py` / `scan.py` (service, file-I/O)

**Text renderer analog** (`danfe.py` lines 1-22): `render_danfe(xml: bytes, out_path: Path) -> int` returns page count; fixed creation date for determinism. `scan.py` follows the same "pure function of bytes in, file out" shape. Use RESEARCH Code Examples 1-3 (pdfium raster, Pillow degrade, JPEG q50 optimize, reportlab `invariant=1`, `rl_config.useA85 = 0`, `is_image_only`, `barcode_readable` at fixed 200 DPI). There is no in-repo analog for rasterize/degrade/reportlab; the only in-repo pdfium usage is in tests:

```python
pdf = pdfium.PdfDocument(str(...))
try:
    return tuple(_collapse(page.get_textpage().get_text_range()) for page in pdf)
finally:
    pdf.close()
```
(`python/tests/test_danfe_labels.py` lines 53-59). Carry over the `# pyright: ignore[reportArgumentType]` on `render(scale=dpi/72)`.

---

### `justfile` and `.github/workflows/ci.yml`

**Analog** (`justfile` 47-53, check line 93; `ci.yml` line 46):
```
# Regenerate the seeded synthetic dataset in data/skeleton.
datagen:
    uv run --project python carimbo-datagen build

# Fail unless a regeneration from the seed is byte-identical to data/skeleton.
datagen-check:
    uv run --project python carimbo-datagen check
...
check: dotnet-check py-check schema-check datagen-check decision-cases-check e2e docs-check secrets-check
```
CI: `- run: just datagen-check` after `just py-check` (ci.yml lines 44-46). Add `dataset`, `dataset-check` likewise; if over ~2-3 minutes, move to a path-filtered job (D-13). Also extend `docs-check` D-18..D-39 list to D-40.

---

### `GroundTruthGateTests.cs` (test, batch) - generalize

**Analog:** same file. Hard-coded `data/skeleton` appears at lines 43, 61, 77, 89, 99, 107 via `RepoFiles.PathOf("data", "skeleton", ...)`. Pattern to keep (lines 18-30, 39-54, 106-116): `TheoryData<string> CaseIds` built from manifest `cases[].case_id`; `AsOfDate()` from manifest; assert zero `FindingSeverity.Error` findings:
```csharp
var errors = new InvoiceValidator(new ValidationOptions())
    .Validate(invoice, AsOfDate())
    .Where(finding => finding.Severity == FindingSeverity.Error).ToArray();
```
Plan: parametrize the manifest directory (skeleton and dataset), either a second test class reusing a shared helper or a `(dir, caseId)` TheoryData. Replace the skeleton-specific facts (lines 33-37, 86-104, "three cases" and exact date) with dataset-specific counts. Keep the `expected` block comparison (lines 56-71) and wire round-trip with `PatternViolations()` (73-84). Use xUnit `Assert` only.

---

### Python tests (test, batch)

**Analog 1:** `python/tests/test_ground_truth.py` for per-case manifest-driven checks:
```python
SKELETON = Path(__file__).resolve().parents[2] / "data" / "skeleton"
@pytest.mark.parametrize("case_id", CASE_IDS)
def test_mapped_invoice_validates_strictly_against_the_generated_model(case_id):
    invoice = invoice_from_xml(SKELETON / f"{case_id}.xml")
    Invoice.model_validate_json(json.dumps(invoice), strict=True)
```
and manifest vs `_expected(build_case_spec(...))` (lines 57-61). For DATA-04 add a jsonschema pass using `carimbo_evals.grader.load_schema_validator(Path("schema/invoice.schema.json"))` (grader.py line 118-121, Draft202012). Parametrize ids from the dataset manifest, not a constant.

**Analog 2:** `python/tests/test_danfe_labels.py` for the D-05 text-equals-XML test: `_collapse` (NFC + whitespace collapse, lines 48-50), `_page_texts` via pdfium (53-59), `_xml_root` / `_xml_text` with `_NS` (62-70), manifest load at module level (43-45). Per RESEARCH Pitfall 3 compare with all whitespace removed.

New tests to write using RESEARCH Code Example 3: no text layer on every non-`text` PDF; `barcode_readable` flag equals recomputed value; page count vs stratum; twin XML bytes == base XML bytes; `ci_subset` subset of dev; both splits per stratum; size budget <= 20,000,000 bytes; leak test (case id, base id, access key absent from twin bytes); alphanumeric CNPJ >= 10%; each rendering n >= 7; total >= 150.

---

### `python/pyproject.toml`

Exact pins, no ranges; move `pypdfium2==5.13.0` and `zxing-cpp==3.1.1` (currently dev group, lines ~23-24) to runtime dependencies; add `pillow==12.3.0`, `numpy==2.5.3`, `reportlab==5.0.1`. Console script already `carimbo-datagen = "carimbo_datagen.cli:app"` (line 28). Insert a `checkpoint:human-verify` before `uv lock` (package audit SUS flags, Phase 1 precedent).

## Shared Patterns

### Per-case determinism
**Source:** `spec.py` lines 381-383. `random.Random(f"{master_seed}:{case_id}")`, no module-level RNG; adding a case must not alter others.

### Atomic writes + sorted-key JSON, no timestamps
**Source:** `cli.py` lines 75-79, 138-139. Apply to all generator outputs.

### Generated-file discipline
**Source:** `AGENTS.md` Conventions; new `data/dataset/*` joins the never-hand-edited list with a `-check` recipe in `just check`.

### Spec-derived `expected` (independent oracle)
**Source:** `cli.py` `_expected` (89-102) and `test_ground_truth.py` line 57-61.

### Skeleton untouchable
Every shared-code edit must leave `just datagen-check` green (default-zero new fields).

## No Analog Found

| File | Role | Data Flow | Reason |
|---|---|---|---|
| `python/src/carimbo_datagen/scan.py` (raster, Pillow degrade, reportlab wrap, zxing flag) | service | file-I/O | No raster/degrade/PDF-wrap code exists; only test-side pdfium text extraction. Use RESEARCH Code Examples 1-3 (executed in research). |
| Split / CI-subset rule in `quota.py` | utility | transform | No hash-ordered stratified selection exists; use RESEARCH Pattern 5 (`sha256`-ordered). |

## Cross-cutting findings for the planner

- Runner pairing (`runner.py` lines 115-121 `discover_cases`; `grader.py` line 385 `{case_id}.xml`) requires one XML per case, so twins get byte-copy XMLs (RESEARCH Pitfall 1).
- Skeleton pytest modules hard-code `data/skeleton`; copy their structure rather than editing them, to keep skeleton coverage.
- `nfe_xml.py` docstring claims XSD validation is DATA-04; correct per RESEARCH Pitfall 9.

## Metadata

**Analog search scope:** `python/src/carimbo_datagen`, `python/tests`, `python/src/carimbo_evals`, `dotnet/tests/Carimbo.Validation.Tests`, `justfile`, `.github/workflows/ci.yml`
**Files scanned:** about 12 read in full or in part
**Pattern extraction date:** 2026-10-22
