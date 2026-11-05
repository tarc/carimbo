"""Row matching and per-column grading of item and installment lists (D-42).

Rows and installments are paired by optimal assignment over an integer score, so the order in
which a model lists them never affects a pass. Matching uses exact identity columns only and
ignores ``--tolerance``, so a regrade at another tolerance never moves a pair.

Imports ``normalize`` only. numpy and scipy are imported inside ``assign_rows``, so start-up stays
fast and a comparison of empty lists needs neither.
"""

from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal
from itertools import pairwise
from typing import Any

from carimbo_evals.normalize import normalize_id, normalize_name, parse_decimal

ITEM_COLUMNS = (
    "code",
    "description",
    "ncm",
    "cst_csosn",
    "cfop",
    "unit",
    "quantity",
    "unit_price",
    "total",
    "icms_base",
    "icms_rate",
    "icms_amount",
    "ipi_rate",
    "ipi_amount",
)
INSTALLMENT_COLUMNS = ("number", "due_date", "amount")
TEXT_COLUMNS = frozenset({"code", "description", "unit"})
ID_COLUMNS = frozenset({"ncm", "cst_csosn", "cfop", "number"})
NUMERIC_COLUMNS = frozenset(
    {
        "quantity",
        "unit_price",
        "total",
        "icms_base",
        "icms_rate",
        "icms_amount",
        "ipi_rate",
        "ipi_amount",
        "amount",
    }
)
# Identity columns of the match score: 7 of the 14 item columns, summing to 14. A pair below the
# minimum earns nothing and stays unmatched.
ITEM_WEIGHTS = {
    "code": 3,
    "description": 3,
    "ncm": 1,
    "unit": 1,
    "quantity": 2,
    "unit_price": 2,
    "total": 2,
}
ITEM_MINIMUM = 6
INSTALLMENT_WEIGHTS = {"number": 2, "due_date": 2, "amount": 1}
INSTALLMENT_MINIMUM = 2

_CENT = Decimal("0.01")
_TIE_SCALE = 10**6


def column_right(column: str, predicted: object, expected: object, tolerance: Decimal) -> bool:
    """True when ``predicted`` is right for ``column``: equal after the declared normalization.

    Text columns compare by ``normalize_name``, id columns by ``normalize_id``, numeric columns as
    ``Decimal`` inside ``tolerance`` (inclusive) and ``due_date`` by exact string equality. A
    prediction that is not a string is wrong. Nothing fuzzy counts.
    """
    if not isinstance(predicted, str) or not isinstance(expected, str):
        return False
    if column in TEXT_COLUMNS:
        return normalize_name(predicted) == normalize_name(expected)
    if column in ID_COLUMNS:
        return normalize_id(predicted) == normalize_id(expected)
    if column in NUMERIC_COLUMNS:
        value = parse_decimal(predicted)
        truth = parse_decimal(expected)
        return value is not None and truth is not None and abs(value - truth) <= tolerance
    return predicted == expected


def is_within_cent(predicted: object, expected: object) -> bool:
    """True when both sides parse as decimals and differ by R$0.01 or less."""
    value = parse_decimal(predicted)
    truth = parse_decimal(expected)
    return value is not None and truth is not None and abs(value - truth) <= _CENT


def row_score(
    predicted_row: dict[str, Any], true_row: dict[str, Any], weights: dict[str, int]
) -> int:
    """The summed weight of the identity columns that are exactly right, at tolerance 0."""
    zero = Decimal(0)
    return sum(
        weight
        for column, weight in weights.items()
        if column_right(column, predicted_row.get(column), true_row.get(column), zero)
    )


@dataclass(frozen=True)
class Assignment:
    """Which predicted row answers which true row.

    ``pairs`` holds ``(predicted_index, true_index)`` sorted by predicted index; the two
    ``unmatched`` tuples hold the indices left over. Predicted indices refer to ``clean_rows``.
    """

    pairs: tuple[tuple[int, int], ...]
    unmatched_true: tuple[int, ...]
    unmatched_predicted: tuple[int, ...]


def clean_rows(predicted: object) -> list[dict[str, Any]]:
    """The dict rows of a prediction; a non-list is no rows and non-dict elements are dropped."""
    if not isinstance(predicted, list):
        return []
    return [row for row in predicted if isinstance(row, dict)]


def assign_rows(
    predicted: list[dict[str, Any]],
    truth: list[dict[str, Any]],
    weights: dict[str, int],
    minimum: int,
) -> Assignment:
    """Pair predicted rows with true rows by optimal assignment of the integer score.

    The credit of a pair is its score when that reaches ``minimum`` and 0 otherwise. The cost is
    ``(sum(weights) - credit) * 10**6 + abs(i - j)``, so the index distance breaks equal-score
    ties and the same input always gives the same pairs. Only pairs with credit stay matched.
    """
    if not predicted or not truth:
        return Assignment(
            pairs=(),
            unmatched_true=tuple(range(len(truth))),
            unmatched_predicted=tuple(range(len(predicted))),
        )
    import numpy as np
    from scipy.optimize import linear_sum_assignment

    full = sum(weights.values())
    credit = np.zeros((len(predicted), len(truth)), dtype=np.int64)
    cost = np.zeros((len(predicted), len(truth)), dtype=np.int64)
    for i, predicted_row in enumerate(predicted):
        for j, true_row in enumerate(truth):
            score = row_score(predicted_row, true_row, weights)
            credit[i, j] = score if score >= minimum else 0
            cost[i, j] = (full - credit[i, j]) * _TIE_SCALE + abs(i - j)
    rows, columns = linear_sum_assignment(cost)
    pairs = sorted((int(i), int(j)) for i, j in zip(rows, columns, strict=True) if credit[i, j] > 0)
    matched_predicted = {i for i, _ in pairs}
    matched_true = {j for _, j in pairs}
    return Assignment(
        pairs=tuple(pairs),
        unmatched_true=tuple(j for j in range(len(truth)) if j not in matched_true),
        unmatched_predicted=tuple(i for i in range(len(predicted)) if i not in matched_predicted),
    )


@dataclass(frozen=True)
class RowDiff:
    """The assignment, every wrong value and the row statistics of one list."""

    assignment: Assignment
    wrong: list[dict[str, Any]]
    stats: dict[str, Any]


def _wrong_value(
    path: str,
    scope: str,
    column: str,
    *,
    row: int | None,
    predicted_row: int | None,
    event: str | None,
    expected: object,
    predicted: object,
    within_cent: bool,
) -> dict[str, Any]:
    return {
        "path": path,
        "scope": scope,
        "column": column,
        "row": row,
        "predicted_row": predicted_row,
        "event": event,
        "expected": expected,
        "predicted": predicted,
        "within_cent": within_cent,
    }


def diff_rows(
    kind: str, predicted: object, truth: list[dict[str, Any]], tolerance: Decimal
) -> RowDiff:
    """Match ``predicted`` to ``truth`` and list every wrong value.

    ``kind`` is ``"item"`` or ``"installment"``. A matched pair contributes one wrong value per
    wrong column. A true row nobody answers is one ``missing`` value on its item total (installment
    amount); a predicted row nobody asked for is one ``extra`` value on the same column (D-06).
    Values are ordered by true row, then column order, then the extras.
    """
    if kind == "item":
        columns, weights, minimum = ITEM_COLUMNS, ITEM_WEIGHTS, ITEM_MINIMUM
        list_name, anchor = "items", "total"
    elif kind == "installment":
        columns, weights, minimum = INSTALLMENT_COLUMNS, INSTALLMENT_WEIGHTS, INSTALLMENT_MINIMUM
        list_name, anchor = "installments", "amount"
    else:
        raise ValueError(f"unknown row kind: {kind!r}")

    rows = clean_rows(predicted)
    assignment = assign_rows(rows, truth, weights, minimum)
    predicted_for_true = {j: i for i, j in assignment.pairs}

    wrong: list[dict[str, Any]] = []
    column_stats = {column: {"correct": 0, "n": 0} for column in columns}
    rows_all_correct = 0
    for j, true_row in enumerate(truth):
        i = predicted_for_true.get(j)
        if i is None:
            wrong.append(
                _wrong_value(
                    f"{list_name}[{j}].{anchor}",
                    kind,
                    anchor,
                    row=j,
                    predicted_row=None,
                    event="missing",
                    expected=true_row.get(anchor),
                    predicted=None,
                    within_cent=False,
                )
            )
            continue
        all_right = True
        for column in columns:
            value, expected = rows[i].get(column), true_row.get(column)
            column_stats[column]["n"] += 1
            if column_right(column, value, expected, tolerance):
                column_stats[column]["correct"] += 1
                continue
            all_right = False
            wrong.append(
                _wrong_value(
                    f"{list_name}[{j}].{column}",
                    kind,
                    column,
                    row=j,
                    predicted_row=i,
                    event=None,
                    expected=expected,
                    predicted=value,
                    within_cent=column in NUMERIC_COLUMNS and is_within_cent(value, expected),
                )
            )
        rows_all_correct += int(all_right)
    for i in assignment.unmatched_predicted:
        wrong.append(
            _wrong_value(
                f"{list_name}[+{i}].{anchor}",
                kind,
                anchor,
                row=None,
                predicted_row=i,
                event="extra",
                expected=None,
                predicted=rows[i].get(anchor),
                within_cent=False,
            )
        )
    matched_true = [j for _, j in assignment.pairs]
    stats = {
        "true_rows": len(truth),
        "predicted_rows": len(rows),
        "matched": len(assignment.pairs),
        "rows_all_correct": rows_all_correct,
        "order_preserved": all(a < b for a, b in pairwise(matched_true)),
        "columns": column_stats,
    }
    return RowDiff(assignment=assignment, wrong=wrong, stats=stats)
