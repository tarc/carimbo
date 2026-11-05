"""Failure tags: every wrong value of a graded case gets exactly one tag (D-10, D-11, D-43).

``failure-tags-1`` is an ordered rule list, the same shape as the decision labelling rule of
D-34: the first rule whose predicate matches a wrong value gives its tag, and the rule number is
the tag's index in ``TAG_ORDER``. The rules are pure code over the graded diff and the row
assignment; no model judges a failure. ``untagged`` always matches last, so tag counts add up to
the wrong-value total and the bucket shows what the rules miss.

Pure: imports ``normalize``, ``matching`` and the standard library only.
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from carimbo_evals.matching import Assignment, clean_rows

TAGS_VERSION = "failure-tags-1"

# The order is the contract: a change is a visible grader bump (D-12). The rule number of a tag is
# its index here.
TAG_ORDER = (
    "schema_invalid",
    "party_swap",
    "merged_rows",
    "split_rows",
    "trailing_rows_lost",
    "dropped_row",
    "invented_row",
    "value_from_other_row",
    "value_from_other_box",
    "thousands_misparse",
    "zero_for_blank",
    "digit_transposition",
    "digit_substitution",
    "digit_count",
    "text_near_miss",
    "text_wrong",
    "tax_code_wrong",
    "date_wrong",
    "untagged",
)

_COUNT_COLUMNS = {"item_count": "item", "installment_count": "installment"}


@dataclass(frozen=True)
class CaseContext:
    """Everything a predicate may read besides the wrong value itself."""

    status: str
    predicted: dict[str, Any]
    expected: dict[str, Any]
    item_assignment: Assignment
    installment_assignment: Assignment
    predicted_items: tuple[dict[str, Any], ...]
    true_items: tuple[dict[str, Any], ...]
    predicted_installments: tuple[dict[str, Any], ...]
    true_installments: tuple[dict[str, Any], ...]
    # True item rows inside the contiguous tail of unmatched true rows.
    trailing_true: frozenset[int]

    @classmethod
    def build(
        cls,
        status: str,
        predicted: dict[str, Any],
        expected: dict[str, Any],
        item_assignment: Assignment,
        installment_assignment: Assignment,
    ) -> CaseContext:
        true_items = tuple(expected.get("items") or ())
        return cls(
            status=status,
            predicted=predicted,
            expected=expected,
            item_assignment=item_assignment,
            installment_assignment=installment_assignment,
            predicted_items=tuple(clean_rows(predicted.get("items"))),
            true_items=true_items,
            predicted_installments=tuple(clean_rows(predicted.get("installments"))),
            true_installments=tuple(expected.get("installments") or ()),
            trailing_true=_trailing_unmatched(len(true_items), item_assignment),
        )


Predicate = Callable[[dict[str, Any], CaseContext], bool]


def _trailing_unmatched(true_count: int, assignment: Assignment) -> frozenset[int]:
    """The true rows from the last one backwards for as long as each is unmatched."""
    unmatched = set(assignment.unmatched_true)
    tail: set[int] = set()
    for index in range(true_count - 1, -1, -1):
        if index not in unmatched:
            break
        tail.add(index)
    return frozenset(tail)


def _is_count(wrong: dict[str, Any]) -> bool:
    return wrong["scope"] == "header" and wrong["column"] in _COUNT_COLUMNS


def _schema_invalid(wrong: dict[str, Any], context: CaseContext) -> bool:
    return context.status == "schema_invalid"


def _trailing_rows_lost(wrong: dict[str, Any], context: CaseContext) -> bool:
    return (
        wrong["scope"] == "item"
        and wrong["event"] == "missing"
        and wrong["row"] in context.trailing_true
    )


def _dropped_row(wrong: dict[str, Any], context: CaseContext) -> bool:
    return wrong["event"] == "missing"


def _invented_row(wrong: dict[str, Any], context: CaseContext) -> bool:
    return wrong["event"] == "extra"


def _untagged(wrong: dict[str, Any], context: CaseContext) -> bool:
    return True


PREDICATES: dict[str, Predicate] = {
    "schema_invalid": _schema_invalid,
    "trailing_rows_lost": _trailing_rows_lost,
    "dropped_row": _dropped_row,
    "invented_row": _invented_row,
    "untagged": _untagged,
}


def first_match(wrong: dict[str, Any], context: CaseContext) -> tuple[str, int]:
    """The tag and rule number of the first rule that matches ``wrong``."""
    for rule, tag in enumerate(TAG_ORDER):
        predicate = PREDICATES.get(tag)
        if predicate is not None and predicate(wrong, context):
            return tag, rule
    return "untagged", len(TAG_ORDER) - 1


def tag_case(
    status: str,
    predicted: dict[str, Any],
    expected: dict[str, Any],
    item_assignment: Assignment,
    installment_assignment: Assignment,
    wrong: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """The wrong values, in the same order, each with ``tag`` and ``rule`` added.

    ``item_count`` and ``installment_count`` take the tag and rule of the first row event of their
    kind in ``wrong`` order, so a dropped row yields two wrong values with one tag. With no such
    event they stay ``untagged``, except in a ``schema_invalid`` case where rule 0 takes them too.
    """
    context = CaseContext.build(
        status, predicted, expected, item_assignment, installment_assignment
    )
    tagged: list[dict[str, Any]] = []
    for value in wrong:
        if _is_count(value) and context.status != "schema_invalid":
            tag, rule = "untagged", len(TAG_ORDER) - 1
        else:
            tag, rule = first_match(value, context)
        tagged.append({**value, "tag": tag, "rule": rule})
    for value in tagged:
        if not _is_count(value) or context.status == "schema_invalid":
            continue
        kind = _COUNT_COLUMNS[value["column"]]
        event = next((w for w in tagged if w["scope"] == kind and w["event"] is not None), None)
        if event is not None:
            value["tag"], value["rule"] = event["tag"], event["rule"]
    return tagged
