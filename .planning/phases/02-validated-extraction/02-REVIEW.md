---
phase: 02-validated-extraction
reviewed: 2026-10-08T00:00:00Z
depth: standard
files_reviewed: 10
files_reviewed_list:
  - docs/DANFE-MAPPING.md
  - dotnet/src/Carimbo.Domain/Invoice.cs
  - dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs
  - dotnet/tests/Carimbo.Api.Tests/EvalEndpointTests.cs
  - dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs
  - python/src/carimbo_models/generated.py
  - python/tests/test_danfe_labels.py
  - python/tests/test_e2e_fake.py
  - schema/invoice.model.schema.json
  - schema/invoice.schema.json
findings:
  critical: 0
  warning: 2
  info: 3
  total: 5
status: issues_found
---

# Phase 2: Code Review Report (incremental, plan 02-13)

**Reviewed:** 2026-10-08
**Depth:** standard
**Files Reviewed:** 10
**Status:** issues_found

## Summary

Incremental re-review of the delta 28162c7..HEAD: prompt extract-003, printed-label schema descriptions, mapping doc, new `python/tests/test_danfe_labels.py` and the SHA-256 prompt pins.

The claims were checked against the artifacts, not only the diff:

- The committed PDFs print exactly the quoted totals labels (`BASE DE CÁLCULO DO ICMS ST`, `VALOR DO ICMS ST`, `VALOR DO IPI`), the `CSOSN`/`CST` header follows CRT, and the stub prints `DESTINATARIO: <name> - <address>`.
- The mapping-doc IE claim is correct: digits on case-001 and case-002, `ISENTO` on case-003.
- BrazilFiscalReport's `danfe.py` lines 889-892 confirm that the notice replaces the name unless `tpAmb == "1"`.
- `pytest tests/test_danfe_labels.py` gives 28 passed. The Extraction.Tests project gives 148 passed.
- The prompt, the `Invoice.cs` descriptions, the generated schema and `generated.py` are consistent. The generated files carry only description changes. No hand-edit was suggested.
- The prompt-version bump reaches `EvalEndpointTests` and `test_e2e_fake.py`.

No bugs or security defects were found in the delta. The remaining issues concern test strength and robustness of the new recipient-name rule.

## Warnings

### WR-01: Two of the totals-label assertions cannot fail, so the "labels stay honest" guard is weaker than advertised

**File:** `python/tests/test_danfe_labels.py:79-82`
**Issue:** `test_every_totals_label_is_printed` uses substring containment on the collapsed page text.
- `"VALOR DO ICMS"` is a substring of `"VALOR DO ICMS ST"`, and `"BASE DE CÁLCULO DO ICMS"` is a substring of `"BASE DE CÁLCULO DO ICMS ST"`. Both pass as long as the ST labels are printed, even if the plain ICMS boxes were renamed or dropped.
- `"DESCONTO"` is also a bare word that could match elsewhere on the page (for example an items-table header).
- The module docstring says the tests keep the quotes honest when the renderer changes. For those three fields they would not notice a change.

**Fix:** Assert the label together with its neighbour, so the match is positional. For example, check the ordered sequence of the CÁLCULO DO IMPOSTO block:
```python
block = page[page.index("CÁLCULO DO IMPOSTO"):page.index("TRANSPORTADOR")]
cursor = 0
for field, label in _TOTALS_LABELS.items():
    pos = block.find(label + " ", cursor)   # trailing space/value boundary
    assert pos >= 0, f"{case_id}: {field} ({label}) missing or out of order"
    cursor = pos + len(label)
```
Alternatively match with a regex of the form `rf"(?<![A-Z]){re.escape(label)} [\d.,]+"`, which requires the label to be followed by its value.

### WR-02: The recipient-name rule ("up to the ` - ` that starts the address") is ambiguous for names containing ` - `, and no test covers it

**File:** `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:55`, `python/tests/test_danfe_labels.py:94`
**Issue:** The prompt, the `Recipient.Name` schema description and the mapping doc all define the name as the stub text between `DESTINATARIO:` and the first ` - `. A legal name that itself contains ` - ` (common in real razão social, for example `ACME - COMERCIO LTDA`) is truncated by that rule, and the model is told to do exactly that. The current synthetic names (Faker `pt_BR` plus `SINTETICA`) never contain ` - `, so the skeleton cannot expose it.

The Python test builds the expected string as `f"DESTINATARIO: {recipient} - "`. It would pass for such a name, so it does not detect the ambiguity either. The grader would then mark a faithful extraction wrong, or reward the truncated one.

**Fix:** Either of these:
- Constrain the datagen name pool so names never contain ` - `, and assert that in `test_danfe_labels.py`: `assert " - " not in recipient`. Record the limitation in `docs/DANFE-MAPPING.md` under the recipient-name assumption.
- Reword the prompt to anchor on the address end instead, for example "the name is everything between `DESTINATARIO:` and the street address that precedes `, CNPJ:` or `, CPF:`". This is robust but needs a new prompt version and pinned hash.

## Info

### IN-01: The label table is duplicated in C# and Python with no cross-check

**File:** `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs:118-131`, `python/tests/test_danfe_labels.py:29-41`
**Issue:** The C# test proves that the prompt quotes the labels in the table. The Python test proves that the PDFs, the schema descriptions and the mapping doc quote the labels in its own copy of the table. Nothing ties the two tables together. A label edited in only one of them leaves both suites green while the prompt and the PDFs disagree.
**Fix:** Have one test read the other's source of truth. For example, the Python test could parse `field (LABEL)` pairs out of the prompt text, which is exposed via the schema-export tool or the eval endpoint. Alternatively, accept the duplication and say so in a comment on both tables.

### IN-02: The prompt pin does not enforce "text change implies version bump"

**File:** `dotnet/tests/Carimbo.Extraction.Tests/ExtractorTests.cs:152-165`
**Issue:** The test asserts the version string and the text hash independently. Someone who edits the prompt and updates the pinned hash without bumping `extract-003` passes. The response cache key includes the prompt version, so that change would silently reuse stale cached responses. The failure message says a new version is needed, but the test cannot enforce it.
**Fix:** Pin a (version, hash) pair, for example in one `Dictionary<string, string>` keyed by version. Keep entries for old versions, so a hash can only be added with a new key and never edited under an existing one. Alternatively, derive the cache key from the hash of the prompt text as well as the version.

### IN-03: The production (`tpAmb` 1) branch of the recipient-name rule is asserted but untested

**File:** `docs/DANFE-MAPPING.md:82`, `dotnet/src/Carimbo.Extraction/InvoiceExtractor.cs:55`
**Issue:** The doc states that a production DANFE prints the name in the DESTINATÁRIO box. I confirmed this in the BrazilFiscalReport source, but no test renders a `tpAmb` 1 document. The prompt's "when that box shows the homologation notice" branch is therefore the only one exercised. The other branch is correct today but guarded only by the library's current behaviour.
**Fix:** Optional. Add one datagen unit test that renders a single case with `tpAmb` 1 into a temporary location, not into `data/skeleton`, and asserts `NOME / RAZÃO SOCIAL <name>` is printed. Otherwise leave a one-line note in the mapping doc that this branch is unverified by the suite.

---

_Reviewed: 2026-10-08_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
