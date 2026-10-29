---
phase: "04"
slug: "synthetic-dataset"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-10-29"
---

# Phase 04 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| PyPI registry → python/.venv | third-party code enters the build and the generator | pinned wheels (numpy, reportlab, charset-normalizer, pillow, pypdfium2, zxing-cpp) |
| shared generator code → committed skeleton bytes | spec.py and nfe_xml.py also build data/skeleton, which published runs and data/decision are keyed to | synthetic XML and PDF bytes |
| generated XML → ground-truth readers and validators | the XML is the oracle every later grade is measured against | synthetic ground truth |
| generator output → the oracle (mapper, validators, schema) | the gate must judge generated ground truth, never be adjusted to it | synthetic ground truth |
| generator output → committed dataset | the byte-identical check judges the committed files against a fresh regeneration | data/dataset bytes |
| printed DANFE → model under test; XML → answer key | a difference between the two would grade a correct reading as wrong | graded strings |
| twin PDF bytes → model under test (later phases) | anything inside the image-only PDF reaches the provider and may reveal the answer | image-only PDFs |
| dev split and CI subset → published test numbers | gating and prompt iteration must never see test invoices | split labels, CI subset |
| generator → public repository | everything generated is published in a portfolio repo | synthetic parties and identifiers (no real taxpayer data, D-14) |
| PR contents → CI runner | the new CI step runs repository code on pull requests, including forks | repository code, read-only token, no secrets |
| repository docs → public readers | published claims about the dataset | D-40, README, AGENTS.md |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-04-SC | Tampering | pip installs (numpy, reportlab, runtime pillow, pypdfium2, zxing-cpp) | high | mitigate | Exact pins in python/pyproject.toml; resolve-only before review; blocking human checkpoint approved 2026-10-29 (04-02 Task 2); `uv sync --project python --locked` in justfile py-check fails on drift | closed |
| T-04-01 | Tampering | spec.py / nfe_xml.py shared with data/skeleton | high | mitigate | Additive fields only; `just datagen-check` prints "ok: data/skeleton matches a regeneration from seed 20261004" in `just check` at ceaa2c4 | closed |
| T-04-02 | Tampering (oracle integrity) | new ICMS group writers | medium | mitigate | python/tests/test_tax_groups.py: NF-e 4.00 XSD per group, exact sums and vNF, strict model and JSON Schema | closed |
| T-04-03 | Tampering | python/uv.lock sources | medium | mitigate | `grep -cE 'source = \{ (git\|url) = ' python/uv.lock` prints 0; reviewer confirmed new names were only charset-normalizer, numpy, reportlab | closed |
| T-04-04 | Elevation of privilege | building a source distribution during install | low | accept | See Accepted Risks Log AR-04-01 | closed |
| T-04-05 | Information disclosure (eval integrity) | twin metadata, hidden text, embedded strings | medium | mitigate | scan.py reportlab `invariant=1`, `is_image_only`; key and case-id byte grep in python/tests/test_scan.py; committed twins re-checked by test_dataset_scans.py | closed |
| T-04-06 | Tampering (reproducibility) | non-deterministic raster, JPEG or PDF writer | medium | mitigate | scan.py per-twin `numpy.random.default_rng` from the seed text, reportlab invariant, fresh-process digest test in test_scan.py; `just dataset-check` byte-identical | closed |
| T-04-07 | Denial of service | rasterizing large inputs | low | accept | See Accepted Risks Log AR-04-02 | closed |
| T-04-08 | Tampering (oracle integrity) | expected blocks and gate | medium | mitigate | test_dataset_ground_truth.py recomputes expected blocks from quota.plan_cases(); `git diff 98353e4..HEAD -- dotnet/src` is empty | closed |
| T-04-09 | Repudiation (silent skip) | gate case selection | medium | mitigate | `test_manifest_case_set_equals_the_files_on_disk` (Python) and `The_manifest_case_set_equals_the_xml_and_pdf_files_on_disk` (.NET) | closed |
| T-04-10 | Tampering | XML parsing in the gate | low | accept | See Accepted Risks Log AR-04-03 | closed |
| T-04-11 | Elevation of privilege | new CI step | low | mitigate | ci.yml `permissions: contents: read`, bare `- run: just dataset-check`; test_repo_layout.py `test_ci_workflow_is_fork_safe_and_unfiltered` asserts `contents: read` and forbids `secrets.` and `pull_request_target` | closed |
| T-04-12 | Information disclosure | real taxpayer data in parties or identifiers | high | mitigate | SINTETICA marker in every name; test_synthetic_only.py `test_dataset_party_names_carry_the_synthetic_marker`, `test_dataset_identifiers_are_valid_and_absent_from_every_nfelib_sample` | closed |
| T-04-13 | Tampering | identifier collisions merging two invoices | low | mitigate | test_synthetic_only.py `test_no_dataset_cnpj_cpf_or_access_key_repeats_across_bases` | closed |
| T-04-14 | Tampering (eval integrity) | truncated or reformatted printed text versus XML | medium | mitigate | python/tests/test_dataset_text_equals_xml.py over all 90 text-layer PDFs (D-05) | closed |
| T-04-15 | Tampering (eval integrity) | float formatting distorting many-digit values | medium | mitigate | test_dataset_spec.py `test_every_amount_stays_below_1e13_and_every_four_decimal_value_below_1e9`; expected strings formatted from Decimal | closed |
| T-04-16 | Information disclosure (eval integrity) | dev/test leakage through twins or the CI subset | medium | mitigate | test_dataset_manifest.py: twins inherit base split, `test_ci_subset_has_twenty_sorted_unique_dev_cases`, subset recomputed | closed |
| T-04-17 | Information disclosure (eval integrity) | identifiers inside committed twins | medium | mitigate | test_dataset_scans.py `test_twin_bytes_leak_no_identifier_and_carry_the_invariant_metadata` over every committed twin | closed |
| T-04-18 | Denial of service | repository and clone size | low | mitigate | test_dataset_manifest.py `SIZE_LIMIT_BYTES = 20_000_000`; measured 19,368,019 bytes (warning above 18,000,000 fires) | closed |
| T-04-19 | Tampering (reproducibility) | byte drift on another Linux x64 machine | medium | mitigate | `just dataset-check` in `just check` and the CI python job (ci.yml line 47); first green CI run is the end-of-phase human check | closed |
| T-04-20 | Information disclosure | provider key in new docs or recipes | low | mitigate | `just secrets-check` prints "secrets ok" in `just check` at ceaa2c4 | closed |
| T-04-21 | Repudiation | dataset decisions undocumented or rewritten | low | mitigate | docs/DECISIONS.md D-40 added (no earlier entry rewritten); `just docs-check` covers D-18 to D-40 | closed |
| T-04-22 | Tampering (reproducibility) | dataset-check comparison | medium | mitigate | cli.py `_compare_directories` byte-compares a fresh regeneration in a temporary directory; python/tests/test_dataset_check.py proves missing, extra and changed files are reported by name and exit 1 | closed |
| T-04-23 | Tampering (eval integrity) | locked quota slot tuples rewritten to fit the CI subset | medium | mitigate | CI-SUBSET-EMPTY-CELL stop did not trigger (all 20 cells found a dev case); no trait slot tuple changed (04-07 SUMMARY); literal slot table in test_dataset_spec.py | closed |
| T-04-24 | Repudiation (eval integrity) | an eval run recorded against a pre-final dataset-001 | medium | mitigate | `git grep -l dataset-001 -- evals/results` is empty; D-40 records the pre-publication rule | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above workflow.security_block_on count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-04-01 | T-04-04 | All six locked scan-stack packages ship wheels for this platform (reviewer confirmed in the 04-02 provenance table); a missing wheel would have been a stop at the checkpoint | plan 04-02 threat model; human reviewer at the 04-02 checkpoint | 2026-10-29 |
| AR-04-02 | T-04-07 | Inputs to scan.py are the generator's own A4 DANFEs (at most a few pages; scan DPI is now 220, raised from 200 in 04-07); no external input reaches the rasterizer | plan 04-03 threat model | 2026-10-29 |
| AR-04-03 | T-04-10 | Gate inputs are generator output; NfeXmlMapper prohibits DTD processing and sets no resolver | plan 04-09 threat model | 2026-10-29 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-10-29 | 25 | 25 | 0 | secure-phase (orchestrator, L1 grep-depth; auditor skipped per short-circuit: register authored at plan time, asvs_level 1) |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-10-29
