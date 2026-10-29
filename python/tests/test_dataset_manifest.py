"""DATA-02, DATA-05 and DATA-06 invariants over the committed ``data/dataset`` (CONTEXT D-01, D-06,
D-10 to D-17).

Everything here reads the committed manifest and files, except the order-independence test, which
rebuilds three cases in a separate process and compares their bytes to the committed ones. The case
ids are parametrized from the manifest, never from a constant.
"""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import warnings
from collections import Counter
from pathlib import Path
from typing import Any

import pytest

from carimbo_datagen import quota
from carimbo_datagen.quota import DATASET_SEED, QUOTA, plan_cases, select_ci_subset
from carimbo_datagen.scan import RENDERINGS

ROOT = Path(__file__).resolve().parents[2]
DATASET = ROOT / "data" / "dataset"
SIZE_LIMIT_BYTES = 20_000_000
SIZE_WARNING_BYTES = 18_000_000

MANIFEST: dict[str, Any] = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
ENTRIES: list[dict[str, Any]] = MANIFEST["cases"]
CASES: dict[str, dict[str, Any]] = {entry["case_id"]: entry for entry in ENTRIES}
CASE_IDS: list[str] = [entry["case_id"] for entry in ENTRIES]
BASES: list[dict[str, Any]] = [e for e in ENTRIES if e["rendering"] == "text"]
TWINS: list[dict[str, Any]] = [e for e in ENTRIES if e["rendering"] != "text"]

EXPECTED_CI_CELLS = (
    "regime_simples",
    "regime_normal",
    "multi_page",
    "many_items",
    "alphanumeric_cnpj",
    "cpf_recipient",
    "st_warning",
    "image_clean",
    "rotation_mild",
    "rotation_severe",
    "blur_mild",
    "blur_severe",
    "low_dpi_mild",
    "low_dpi_severe",
    "barcode_damaged",
    "short",
    "medium",
    "mixed_tax_codes",
    "long_text",
    "confusable",
)


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


# --- counts and slices (DATA-02, CONTEXT D-01, D-10) --------------------------------------------


def test_the_dataset_has_154_cases_ninety_bases_and_sixty_four_twins() -> None:
    assert len(ENTRIES) == 154
    assert len(BASES) == 90
    assert len(TWINS) == 64
    assert [f"nfe-{n:04d}" for n in range(1, 155)] == CASE_IDS
    assert round(100 * len(TWINS) / len(ENTRIES), 1) == 41.6


def test_each_of_the_eight_twin_renderings_has_eight_cases() -> None:
    slices = Counter((e["rendering"], e["severity"]) for e in TWINS)
    assert dict(slices) == {pair: 8 for pair in RENDERINGS}


def test_at_least_ten_percent_have_an_alphanumeric_issuer_cnpj() -> None:
    for group in (ENTRIES, BASES):
        share = sum(1 for e in group if "alphanumeric_cnpj" in e["tags"]) / len(group)
        assert share >= 0.10, (len(group), share)


def test_both_regimes_and_every_length_shape_are_present() -> None:
    tags = {tag for e in ENTRIES for tag in e["tags"]}
    assert {"regime_normal", "simples_nacional", "single_page", "many_items", "multi_page"} <= tags


# --- splits (DATA-05, CONTEXT D-14) -------------------------------------------------------------


def test_thirty_of_ninety_bases_and_fifty_four_of_all_cases_are_dev() -> None:
    assert sum(1 for e in BASES if e["split"] == "dev") == 30
    assert sum(1 for e in ENTRIES if e["split"] == "dev") == 54
    assert {e["split"] for e in ENTRIES} == {"dev", "test"}


def test_every_stratum_has_dev_and_test_bases() -> None:
    cell_of = {base.case_id: base.cell for base in plan_cases().bases}
    for cell in QUOTA:
        splits = {e["split"] for e in BASES if cell_of[e["case_id"]] == cell.name}
        assert splits == {"dev", "test"}, cell.name


@pytest.mark.parametrize("entry", TWINS, ids=lambda e: e["case_id"])
def test_a_twin_has_its_bases_split_and_an_xml_byte_identical_to_its_bases(
    entry: dict[str, Any],
) -> None:
    base = CASES[entry["base_case"]]
    assert base["rendering"] == "text"
    assert entry["split"] == base["split"], entry["case_id"]
    assert (DATASET / entry["xml"]).read_bytes() == (DATASET / base["xml"]).read_bytes()


# --- the CI subset (CONTEXT D-15, D-16) ---------------------------------------------------------


def test_ci_cells_are_the_twenty_cells_of_decision_d_16_in_order() -> None:
    assert quota.CI_CELLS == EXPECTED_CI_CELLS


def test_ci_subset_has_twenty_sorted_unique_dev_cases() -> None:
    subset = MANIFEST["ci_subset"]
    assert len(subset) == 20
    assert subset == sorted(subset)
    assert len(set(subset)) == 20
    assert all(CASES[c]["split"] == "dev" for c in subset)


def test_ci_subset_is_the_recomputed_selection_over_the_manifest_entries() -> None:
    assert select_ci_subset(ENTRIES, DATASET_SEED) == MANIFEST["ci_subset"]


def test_every_ci_cell_is_covered_by_a_member_that_matches_it() -> None:
    members = [CASES[c] for c in MANIFEST["ci_subset"]]
    for cell in quota.CI_CELLS:
        predicate = quota._CI_PREDICATES[cell]  # pyright: ignore[reportPrivateUsage]
        assert any(predicate(member) for member in members), cell


def test_no_ci_member_or_its_base_is_a_test_case() -> None:
    for case_id in MANIFEST["ci_subset"]:
        entry = CASES[case_id]
        assert entry["split"] == "dev", case_id
        assert CASES[entry["base_case"]]["split"] == "dev", case_id


# --- ordering and hygiene (DATA-02) -------------------------------------------------------------


def test_cases_are_sorted_by_case_id_and_every_tags_list_is_sorted_and_unique() -> None:
    assert sorted(CASE_IDS) == CASE_IDS
    assert MANIFEST["ci_subset"] == sorted(MANIFEST["ci_subset"])
    for entry in ENTRIES:
        assert entry["tags"] == sorted(set(entry["tags"])), entry["case_id"]


def test_barcode_unreadable_is_tagged_exactly_when_the_flag_is_false() -> None:
    for entry in ENTRIES:
        assert ("barcode_unreadable" in entry["tags"]) is (not entry["barcode_readable"]), entry[
            "case_id"
        ]


def test_quota_sha256_equals_the_code() -> None:
    assert MANIFEST["quota_sha256"] == quota.quota_sha256()


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_file_hashes_match_the_manifest(case_id: str) -> None:
    entry = CASES[case_id]
    assert _sha256(DATASET / entry["xml"]) == entry["xml_sha256"]
    assert _sha256(DATASET / entry["pdf"]) == entry["pdf_sha256"]


def test_manifest_has_no_machine_specific_values() -> None:
    text = (DATASET / "manifest.json").read_text(encoding="utf-8")
    for needle in (str(ROOT), "/home/", "/nix/", "/tmp/", "T00:00", "hostname"):
        assert needle not in text, needle


# --- size and commit-everything (DATA-06, CONTEXT D-11, D-12) -----------------------------------


def _total_bytes() -> int:
    return sum(path.stat().st_size for path in DATASET.iterdir() if path.is_file())


def test_the_dataset_stays_within_the_size_budget() -> None:
    total = _total_bytes()
    if total > SIZE_WARNING_BYTES:
        warnings.warn(
            f"data/dataset is {total:,} bytes, above the {SIZE_WARNING_BYTES:,}-byte warning "
            f"line (hard limit {SIZE_LIMIT_BYTES:,}, CONTEXT D-12)",
            stacklevel=1,
        )
    assert total <= SIZE_LIMIT_BYTES, (
        f"data/dataset is {total:,} bytes (limit {SIZE_LIMIT_BYTES:,})"
    )


def test_no_dataset_file_is_git_ignored() -> None:
    try:
        result = subprocess.run(
            ["git", "ls-files", "--others", "--ignored", "--exclude-standard", "data/dataset"],
            capture_output=True,
            text=True,
            check=True,
            cwd=ROOT,
        )
    except (FileNotFoundError, subprocess.CalledProcessError):
        pytest.skip("not a git work tree")
    assert result.stdout.split() == [], "CONTEXT D-11 commits every dataset file"


# --- order independence (DATA-02) ---------------------------------------------------------------

_SUBPROCESS_BUILD = """
import hashlib, sys, tempfile
from pathlib import Path
from carimbo_datagen.dataset import build_case_files

seed, ids = int(sys.argv[1]), sys.argv[2:]
with tempfile.TemporaryDirectory() as tmp:
    out = Path(tmp)
    build_case_files(seed, ids, out)
    for path in sorted(out.iterdir()):
        print(path.name, hashlib.sha256(path.read_bytes()).hexdigest())
"""


def test_a_separate_process_building_cases_in_reverse_order_writes_the_committed_bytes() -> None:
    multi_page = next(e["case_id"] for e in BASES if "multi_page" in e["tags"])
    ids = ["nfe-0154", multi_page, "nfe-0001"]
    proc = subprocess.run(
        [sys.executable, "-c", _SUBPROCESS_BUILD, str(DATASET_SEED), *ids],
        capture_output=True,
        text=True,
        check=True,
        cwd=ROOT,
    )
    built = dict(line.split() for line in proc.stdout.splitlines())
    expected: dict[str, str] = {}
    for case_id in ids:
        for name in (CASES[case_id]["xml"], CASES[case_id]["pdf"]):
            expected[name] = _sha256(DATASET / name)
    assert built == expected
