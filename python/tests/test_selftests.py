"""EVAL-04: the grader proven on ground truth and seeded corruptions over all 154 cases.

Ground truth must score perfect with no tag; each corruption of the catalogue
(``carimbo_evals.corruptions``) must produce wrong values tagged by exactly its own tag; deleted,
duplicated and swapped rows must give exact precision and recall. Twins share their base's XML, so
the 154 cases are 90 distinct ground truths (key: manifest ``xml_sha256``): each is diffed once.
"""

from __future__ import annotations

import copy
import functools
import json
import random
from collections.abc import Iterator
from decimal import Decimal
from fractions import Fraction
from pathlib import Path
from typing import Any

import pytest

from carimbo_evals.corruptions import CORRUPTIONS, corrupted, stored_record
from carimbo_evals.grader import GroundTruth, diff_invoice, grade_case, load_ground_truth
from carimbo_evals.tags import TAG_ORDER

REPO_ROOT = Path(__file__).resolve().parents[2]
DATASET = REPO_ROOT / "data" / "dataset"
EXACT = Decimal("0.00")

MANIFEST: dict[str, Any] = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
CASES: list[dict[str, Any]] = MANIFEST["cases"]
CASE_IDS: list[str] = [case["case_id"] for case in CASES]
SHA_OF: dict[str, str] = {case["case_id"]: case["xml_sha256"] for case in CASES}
XML_OF: dict[str, str] = {case["xml_sha256"]: case["xml"] for case in CASES}
CORRUPTION_TAGS = list(TAG_ORDER[1:])

# Cases a corruption applies to: all 154 unless it needs rows or a non-zero value (research,
# measured on data/dataset).
APPLICABLE = {
    "party_swap": 154,
    "trailing_rows_lost": 117,
    "dropped_row": 117,
    "invented_row": 154,
    "merged_rows": 128,
    "split_rows": 154,
    "value_from_other_row": 128,
    "value_from_other_box": 154,
    "thousands_misparse": 154,
    "zero_for_blank": 41,
    "digit_transposition": 154,
    "digit_substitution": 154,
    "digit_count": 154,
    "text_near_miss": 154,
    "text_wrong": 154,
    "tax_code_wrong": 154,
    "date_wrong": 154,
    "untagged": 154,
}


@functools.cache
def _truth(sha: str) -> GroundTruth:
    return load_ground_truth(DATASET / XML_OF[sha])


def _tags(wrong: list[dict[str, Any]]) -> set[str]:
    return {value["tag"] for value in wrong}


@pytest.fixture(scope="module")
def battery() -> dict[str, dict[str, list[dict[str, Any]] | None]]:
    """For every kind and distinct ground truth: the tagged wrong values, or None if N/A."""
    results: dict[str, dict[str, list[dict[str, Any]] | None]] = {}
    for tag in CORRUPTION_TAGS:
        per_truth: dict[str, list[dict[str, Any]] | None] = {}
        for sha in XML_OF:
            truth = _truth(sha)
            invoice = corrupted(tag, truth.invoice, sha)
            per_truth[sha] = (
                None if invoice is None else diff_invoice("success", invoice, truth, EXACT)["wrong"]
            )
        results[tag] = per_truth
    return results


# --- ground truth scores perfect ---------------------------------------------------------------


def test_the_dataset_is_154_cases_over_90_distinct_ground_truths() -> None:
    assert len(CASE_IDS) == 154
    assert len(XML_OF) == 90


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_ground_truth_scores_perfect_with_no_tag(case_id: str) -> None:
    truth = _truth(SHA_OF[case_id])
    diff = diff_invoice("success", truth.invoice, truth, EXACT)
    assert diff["wrong"] == []
    assert not any(diff["fields"][field] is False for field in diff["fields"])
    graded = grade_case(stored_record(case_id, truth.invoice), truth, EXACT)
    assert graded["correct"] is True
    assert graded["failure_tags"] == []
    assert graded["wrong"] == []


# --- each corruption produces exactly its tag --------------------------------------------------


@pytest.mark.parametrize("tag", CORRUPTION_TAGS)
def test_a_corruption_produces_wrong_values_tagged_by_exactly_its_tag(
    tag: str, battery: dict[str, dict[str, list[dict[str, Any]] | None]]
) -> None:
    applicable = 0
    for case_id in CASE_IDS:
        wrong = battery[tag][SHA_OF[case_id]]
        if wrong is None:
            continue
        applicable += 1
        assert wrong, f"{case_id}: the {tag} corruption left the answer correct"
        stray = [(w["path"], w["tag"]) for w in wrong if w["tag"] != tag]
        assert _tags(wrong) == {tag}, f"{case_id}: expected only {tag}, got {stray}"
        assert {value["rule"] for value in wrong} == {TAG_ORDER.index(tag)}
    assert applicable == APPLICABLE[tag]


def test_the_applicable_counts_are_pinned_as_literals() -> None:
    assert APPLICABLE["trailing_rows_lost"] == 117
    assert APPLICABLE["dropped_row"] == 117
    assert APPLICABLE["merged_rows"] == 128
    assert APPLICABLE["value_from_other_row"] == 128
    assert APPLICABLE["zero_for_blank"] == 41
    assert {
        count
        for tag, count in APPLICABLE.items()
        if tag
        not in (
            "trailing_rows_lost",
            "dropped_row",
            "merged_rows",
            "value_from_other_row",
            "zero_for_blank",
        )
    } == {154}


def test_every_tag_has_a_seeded_corruption() -> None:
    assert set(TAG_ORDER) == set(CORRUPTIONS) | {"schema_invalid"}
    assert set(CORRUPTIONS) == set(TAG_ORDER) - {"schema_invalid"}
    assert set(APPLICABLE) == set(CORRUPTIONS)


def test_schema_invalid_is_proven_by_grading_a_schema_invalid_record() -> None:
    sha = next(iter(XML_OF))
    graded = grade_case(
        stored_record("nfe-0001", None, status="schema_invalid"), _truth(sha), EXACT
    )
    assert graded["status"] == "schema_invalid"
    assert graded["failure_tags"] == ["schema_invalid"]
    assert graded["wrong"]
    assert {value["rule"] for value in graded["wrong"]} == {0}
    assert graded["correct"] is False


def test_a_corruption_is_reproducible_and_leaves_the_original_untouched() -> None:
    truth = _truth(next(iter(XML_OF)))
    before = copy.deepcopy(truth.invoice)
    for tag in CORRUPTION_TAGS:
        first = corrupted(tag, truth.invoice, "seed")
        second = corrupted(tag, truth.invoice, "seed")
        assert first == second
        assert first is None or first != truth.invoice
    assert truth.invoice == before


# --- rows: exact precision and recall ----------------------------------------------------------


def _row_truths() -> Iterator[tuple[str, GroundTruth]]:
    for sha in XML_OF:
        truth = _truth(sha)
        if len(truth.invoice["items"]) >= 2:
            yield sha, truth


def _precision_recall(invoice: dict[str, Any], truth: GroundTruth) -> tuple[Fraction, Fraction]:
    items = diff_invoice("success", invoice, truth, EXACT)["items"]
    return (
        Fraction(items["matched"], items["predicted_rows"]),
        Fraction(items["matched"], items["true_rows"]),
    )


def test_a_deleted_row_costs_recall_exactly_and_never_precision() -> None:
    checked = 0
    for sha, truth in _row_truths():
        invoice = copy.deepcopy(truth.invoice)
        n = len(invoice["items"])
        del invoice["items"][random.Random(f"{sha}-delete").randrange(n)]
        precision, recall = _precision_recall(invoice, truth)
        assert (precision, recall) == (Fraction(1), Fraction(n - 1, n)), sha
        checked += 1
    assert checked > 0


def test_a_duplicated_row_costs_precision_exactly_and_never_recall() -> None:
    checked = 0
    for sha in XML_OF:
        truth = _truth(sha)
        invoice = copy.deepcopy(truth.invoice)
        n = len(invoice["items"])
        index = random.Random(f"{sha}-duplicate").randrange(n)
        invoice["items"].insert(index + 1, copy.deepcopy(invoice["items"][index]))
        precision, recall = _precision_recall(invoice, truth)
        assert (precision, recall) == (Fraction(n, n + 1), Fraction(1)), sha
        checked += 1
    assert checked == len(XML_OF)


def test_two_swapped_rows_keep_precision_and_recall_and_break_the_order_only() -> None:
    checked = 0
    for sha, truth in _row_truths():
        invoice = copy.deepcopy(truth.invoice)
        n = len(invoice["items"])
        i, j = sorted(random.Random(f"{sha}-swap").sample(range(n), 2))
        invoice["items"][i], invoice["items"][j] = invoice["items"][j], invoice["items"][i]
        diff = diff_invoice("success", invoice, truth, EXACT)
        assert _precision_recall(invoice, truth) == (Fraction(1), Fraction(1)), sha
        assert diff["wrong"] == [], sha
        assert diff["items"]["order_preserved"] is False, sha
        graded = grade_case(stored_record("nfe-0001", invoice), truth, EXACT)
        assert graded["correct"] is True, sha
        checked += 1
    assert checked > 0


# --- infrastructure outcomes are not wrong answers ---------------------------------------------


@pytest.mark.parametrize("status", ["infrastructure_failure", "harness_error"])
def test_an_excluded_status_is_neither_correct_nor_wrong(status: str) -> None:
    truth = _truth(next(iter(XML_OF)))
    graded = grade_case(stored_record("nfe-0001", None, status=status), truth, EXACT)
    assert graded["correct"] is None
    assert graded["wrong"] == []
    assert graded["failure_tags"] == []


@pytest.mark.parametrize("status", ["refused", "truncated"])
def test_a_refusal_or_truncation_is_not_correct_and_has_no_wrong_value(status: str) -> None:
    truth = _truth(next(iter(XML_OF)))
    graded = grade_case(stored_record("nfe-0001", None, status=status), truth, EXACT)
    assert graded["correct"] is False
    assert graded["wrong"] == []
    assert graded["failure_tags"] == []
