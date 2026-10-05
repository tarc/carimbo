---
phase: 01-walking-skeleton
plan: 06
subsystem: datagen
tags: [nfe, danfe, cnpj, access-key, brazilfiscalreport, lxml, faker, zxing-cpp, pypdfium2, determinism, tdd]

requires:
  - phase: 01-walking-skeleton
    provides: "Python project, locked pins and carimbo_datagen package (01-01); grader that reads ground-truth XML paths (01-03)"
provides:
  - "carimbo_datagen.ids: char_value, cnpj_check_digits, is_valid_cnpj, make_cnpj (numeric and alphanumeric), access_key_check_digit, make_access_key, ACCESS_KEY_PATTERN"
  - "carimbo_datagen.spec: frozen PartySpec/ItemSpec/CaseSpec, MASTER_SEED 20261004, CASE_IDS, MULTIPAGE_ITEMS 80, build_case_spec, municipality_code"
  - "carimbo_datagen.nfe_xml: NFE_NS, build_nfe_xml (nfeProc/NFe/infNFe/protNFe, homologation, no signature)"
  - "carimbo_datagen.danfe: FIXED_CREATION_DATE, render_danfe returning the page count"
  - "DATA-07 evidence: independent-oracle, collision, uniqueness and SINTETICA-marker tests"
  - "DATA-01 evidence: byte identity (in process and fresh interpreter), Code 128 decode, multi-page overflow"
affects: [01-07, 01-09, 01-13]

actuals:
  tokens: 7000
  tasks: 2
  commits: 4
plan_head_before: bb21a7c21251fba0384c61080646864bc7798e81
plan_head_after: a41c8c97f98e2921edfd61dcd0bea197228db62d

tech-stack:
  added: []
  patterns:
    - "Per-case randomness from random.Random(f\"{master_seed}:{case_id}\") plus a Faker instance seeded from it; no global RNG state, so a case builds identically alone or after others"
    - "Identifier math checked against independent oracles (textbook CNPJ vector, NT 2025.001 example, every access key in nfelib's sample XMLs) with a non-vacuity assertion"
    - "Deterministic PDF: BrazilFiscalReport core fonts plus set_creation_date with a fixed date; determinism proven against a fresh sys.executable subprocess"

key-files:
  created:
    - python/src/carimbo_datagen/ids.py
    - python/src/carimbo_datagen/spec.py
    - python/src/carimbo_datagen/nfe_xml.py
    - python/src/carimbo_datagen/danfe.py
    - python/tests/test_synthetic_only.py
    - python/tests/test_datagen.py
  modified: []

key-decisions:
  - "MULTIPAGE_ITEMS stays at 80: case-003 renders to 2 pages (40 items is 1 page, 50 is already 2), so there is margin and no tuning was needed"
  - "Party names are '<Faker last name> <business word> SINTETICA LTDA' where every business word carries pt-BR diacritics (COMERCIO, INDUSTRIA, SERVICOS, DISTRIBUICAO, CONSTRUCOES, accented), so the encoding path is exercised by every name and the text-layer test is meaningful"
  - "cMun and cMunFG are the IBGE UF code plus 00000, a documented placeholder rather than a real municipality, because PartySpec follows the plan and carries no municipality code"

patterns-established:
  - "RED commits carry signature-only stubs raising NotImplementedError so the target tests fail on the planned behaviour, not on import"

requirements-completed: []

coverage:
  - id: D1
    description: "CNPJ (numeric and alphanumeric) and access-key check digits match independent oracles"
    requirement: "DATA-07"
    verification:
      - kind: unit
        ref: "python/tests/test_synthetic_only.py::test_cnpj_check_digits_textbook_vector, test_cnpj_check_digits_alphanumeric_nt_2025_001_example, test_access_key_check_digit_matches_every_nfelib_sample_key"
        status: pass
    human_judgment: false
  - id: D2
    description: "Generated parties are synthetic: unique CNPJs, SINTETICA-marked names, no collision with nfelib samples, build-order independence"
    requirement: "DATA-07"
    verification:
      - kind: unit
        ref: "python/tests/test_synthetic_only.py (18 tests)"
        status: pass
    human_judgment: false
  - id: D3
    description: "NF-e XML and DANFE PDF are byte-reproducible, carry a decodable Code 128 access key, and the many-items case overflows to 2 pages"
    requirement: "DATA-01"
    verification:
      - kind: unit
        ref: "python/tests/test_datagen.py (26 tests)"
        status: pass
    human_judgment: false

duration: 25min
completed: 2026-10-05
status: complete
---

# Phase 1 Plan 06: Seeded synthetic NF-e XML and DANFE rendering Summary

**Seeded generator core: mod-11 CNPJ and access-key math (numeric and alphanumeric), per-case-seeded specs, lxml NF-e 4.00 XML and a BrazilFiscalReport DANFE with a decodable Code 128 key, byte-identical across runs and across a fresh interpreter.**

## Performance

- **Duration:** about 25 min
- **Tasks:** 2 (both TDD, 4 commits)
- **Files created:** 6 (four modules, two test files)

## Accomplishments

- Identifier math in `ids.py` uses one rule (ASCII-48 value, weights, remainder below 2 gives 0) for the numeric and alphanumeric CNPJ and for the 44-character access key. It matches the textbook vector (`112223330001` gives `81`), the NT 2025.001 example (`12ABC34501DE` gives `35`) and every `Id="NFe..."` key found in nfelib's sample XMLs. The test asserts at least one key and one sample CNPJ were found, so the oracle cannot pass vacuously.
- `spec.py` builds the three skeleton profiles from `random.Random(f"{master_seed}:{case_id}")`. case-001 has numeric CNPJs and 3 items, case-002 has an alphanumeric issuer CNPJ (its access key is alphanumeric too) and 4 items, case-003 has numeric CNPJs and 80 items. The six CNPJs are distinct, no CNPJ appears in the nfelib samples, and every party name carries `SINTETICA` within 60 characters.
- `nfe_xml.py` writes `nfeProc/NFe/infNFe` plus `protNFe` with tpAmb 2, Simples Nacional (CRT 1, CSOSN 102, PIS/COFINS CST 07), a "SEM VALOR FISCAL" note and no signature. `carimbo_evals.grader.load_ground_truth` reads back every spec value.
- `danfe.py` renders through BrazilFiscalReport with `set_creation_date(FIXED_CREATION_DATE)`. A fresh `sys.executable` subprocess produced the same PDF SHA-256 for all three cases. Code 128 decodes to the exact access key at 300 dpi for all three cases (numeric and alphanumeric). case-001 and case-002 are 1 page, case-003 is 2 pages.
- Final run: `pytest python/tests` gives 44 passed (3 deselected e2e/live), ruff check, ruff format --check and pyright are clean.

## TDD Cycle

- **RED (task 1), `74e1560`:** 18 tests in `test_synthetic_only.py` collected and all 18 failed with `NotImplementedError` from signature-only stubs. Semantic assessment: every target test executed and failed on the unimplemented behaviour (no import, collection or fixture fault, no unexpected green). The classifier (`check tdd-red-evidence`) was not run because pytest console output is not one of its supported report formats and `workflow.tdd_mode` is off; the assessment is manual, recorded here.
- **GREEN (task 1), `4c6b8ee`:** `ids.py` and `spec.py` implemented, 18 passed.
- **RED (task 2), `d2c17d0`:** 15 failed and 10 errored (fixture `rendered`, 9 of them plus the page-count test) with `NotImplementedError`, 1 passed. The one pass is `test_dataset_builder_never_imports_nfelib`, a standing guard for the acceptance criterion that needs no implementation. Semantic assessment as above.
- **GREEN (task 2), `a41c8c9`:** `nfe_xml.py` and `danfe.py` implemented, 44 passed across both files on the first run.
- **REFACTOR:** none needed.

## Task Commits

1. Task 1 RED: `74e1560` test(01-06): add failing tests for identifier math and synthetic case specs
2. Task 1 GREEN: `4c6b8ee` feat(01-06): implement identifier math and seeded synthetic case specs
3. Task 2 RED: `d2c17d0` test(01-06): add failing tests for deterministic NF-e XML and DANFE rendering
4. Task 2 GREEN: `a41c8c9` feat(01-06): implement deterministic NF-e XML builder and DANFE renderer

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] pyright cannot resolve `from lxml import etree`**
- **Found during:** Task 2 verification
- **Issue:** lxml 6.1.3 ships no type stubs, so `from lxml import etree` is reported as an unknown import symbol and pyright failed the plan's lint gate. Installing `lxml-stubs` would add a package, which the plan's T-01-SC mitigation forbids.
- **Fix:** `import lxml.etree as etree`, which pyright accepts, with no new dependency.
- **Files modified:** `python/src/carimbo_datagen/nfe_xml.py`
- **Commit:** `a41c8c9`

**2. [Rule 3 - Blocking] pypdfium2 types `render(scale=)` as int**
- **Found during:** Task 2 verification
- **Issue:** The plan's 300 dpi render passes `300 / 72`, a float. pypdfium2 5.13.0 documents the parameter as a float but annotates it `int`, so pyright reported an error.
- **Fix:** Kept the 300 dpi float and added a targeted `# pyright: ignore[reportArgumentType]` with a comment explaining the stub mismatch.
- **Files modified:** `python/tests/test_datagen.py`
- **Commit:** `a41c8c9`

**3. [Plan note] Signature-only stubs in the RED commits**
- The plan does not describe RED mechanics. The RED commits include `NotImplementedError` stubs (and `MULTIPAGE_ITEMS = 80` and the dataclass shapes in `spec.py`) so tests fail on behaviour rather than on import. The stubs are fully replaced in the GREEN commits.

### Environment deviation (not a plan deviation)

`import zxingcpp` fails on this NixOS-WSL host with `libstdc++.so.6: cannot open shared object file`. Tests were run with `LD_LIBRARY_PATH` pointing at nixpkgs `stdenv.cc.cc.lib` (`/nix/store/j7qx4s4mr17j1wqgvqdzj33lmrnzb387-gcc-16.2.0-lib/lib`, built with `--no-link`, so no GC root), exactly as 01-01 verified. No project code works around the host. The devenv follow-up recorded in STATE still applies. The fresh-process determinism test inherits that variable from the pytest process.

## Authentication Gates

None.

## Known Stubs

None. The RED-phase stubs no longer exist in the final source. One intentional placeholder is documented: `cMun`/`cMunFG` are `<IBGE UF code>00000`, not a real municipality code (see key-decisions). Nothing grades or renders against it.

## Threat Flags

None. No new network endpoints, auth paths or trust-boundary file access. nfelib sample XMLs are read only in `test_synthetic_only.py` (T-01-14, accepted) and nothing from them is written anywhere. T-01-13 is mitigated by the own CNPJ generator, the collision test, the SINTETICA marker, tpAmb 2 and the "SEM VALOR FISCAL" note; the residual risk (a check-digit-valid synthetic CNPJ coinciding with a real one) is unchanged and documented in the plan.

## Issues Encountered

None beyond the two pyright items above.

## Notes for Downstream Plans

- **01-07** can commit `data/skeleton/` from `build_case_spec(MASTER_SEED, case_id)`, `build_nfe_xml` and `render_danfe`. The cross-process determinism evidence here was gathered on this host's Nix Python 3.12; research assumption A9 (uv-managed interpreter gives the same bytes) is still only checked by CI regenerate-and-compare. If it differs, try `set_compression(False)` as the plan suggests.
- Printed CNPJs on the DANFE are formatted (`XX.XXX.XXX/0001-XX`), so graders must normalise, as `carimbo_evals.grader.normalize_id` already does.
- DATA-01 and DATA-07 are left unchecked in REQUIREMENTS.md on purpose. 01-07 owns the "committed and verified" half and lists both IDs, so it should mark them complete.

## Self-Check: PASSED

- FOUND: python/src/carimbo_datagen/ids.py, spec.py, nfe_xml.py, danfe.py
- FOUND: python/tests/test_synthetic_only.py, python/tests/test_datagen.py
- FOUND commits: 74e1560, 4c6b8ee, d2c17d0, a41c8c9
