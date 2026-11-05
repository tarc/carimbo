"""Failure tags: every wrong value of a graded case gets exactly one tag (D-10, D-11, D-43).

``failure-tags-1`` is an ordered rule list, the same shape as the decision labelling rule of
D-34: the first rule whose predicate matches a wrong value gives its tag, and the rule number is
the tag's index in ``TAG_ORDER``. The rules are pure code over the graded diff and the row
assignment; no model judges a failure. ``untagged`` always matches last, so tag counts add up to
the wrong-value total and the bucket shows what the rules miss.

Rules 2 to 7 read the row assignment, every other rule is a function of one wrong value (or of the
case's header, for ``party_swap``). All numeric equality is ``Decimal``.

Pure: imports ``normalize``, ``matching`` and the standard library only.
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from decimal import Decimal
from difflib import SequenceMatcher
from typing import Any

from carimbo_evals.matching import (
    NUMERIC_COLUMNS,
    Assignment,
    clean_rows,
    column_right,
)
from carimbo_evals.normalize import normalize_id, normalize_name, parse_decimal

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

NEAR_MISS_RATIO = 0.9

_UNTAGGED_RULE = len(TAG_ORDER) - 1
_COUNT_COLUMNS = {"item_count": "item", "installment_count": "installment"}
_ZERO = Decimal(0)
_SUM_COLUMNS = ("total", "quantity")

# Item columns whose value a model can copy from another true row.
_OTHER_ROW_COLUMNS = frozenset(
    {
        "code",
        "description",
        "ncm",
        "quantity",
        "unit_price",
        "total",
        "icms_base",
        "icms_amount",
        "ipi_amount",
    }
)
# Item money columns a model can copy from another column of the same true row.
_ROW_MONEY_COLUMNS = ("quantity", "unit_price", "total", "icms_base", "icms_amount", "ipi_amount")

_HEADER_ID_PATHS = frozenset(
    {
        "access_key",
        "issuer.cnpj",
        "recipient.tax_id",
        "issuer.ie",
        "recipient.ie",
        "number",
        "series",
    }
)
_ITEM_ID_COLUMNS = frozenset({"ncm", "cst_csosn", "cfop", "code"})
_TAX_CODE_COLUMNS = frozenset({"cst_csosn", "cfop", "ncm"})
_HEADER_NEAR_MISS_PATHS = frozenset({"operation_nature", "issuer.name", "recipient.name"})
_ITEM_NEAR_MISS_COLUMNS = frozenset({"code", "description", "unit"})
_HEADER_TEXT_ONLY_PATHS = frozenset({"issuer.uf", "recipient.uf", "recipient.tax_id_kind"})


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
    # The case shows a party crossover (an issuer value in the recipient's place or the reverse).
    crossover: bool
    # Unmatched true rows a matched row absorbed, and the (true row, column) cells that absorbed.
    merged_missing: frozenset[int]
    merged_cells: frozenset[tuple[int, str]]
    # Unmatched predicted rows that are the other half of a matched row, and the matched cells.
    split_extra: frozenset[int]
    split_cells: frozenset[tuple[int, str]]

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
        predicted_items = tuple(clean_rows(predicted.get("items")))
        merged_missing, merged_cells = _merged(predicted_items, true_items, item_assignment)
        split_extra, split_cells = _split(predicted_items, true_items, item_assignment)
        return cls(
            status=status,
            predicted=predicted,
            expected=expected,
            item_assignment=item_assignment,
            installment_assignment=installment_assignment,
            predicted_items=predicted_items,
            true_items=true_items,
            predicted_installments=tuple(clean_rows(predicted.get("installments"))),
            true_installments=tuple(expected.get("installments") or ()),
            trailing_true=_trailing_unmatched(len(true_items), item_assignment),
            crossover=_crossover(predicted, expected),
            merged_missing=merged_missing,
            merged_cells=merged_cells,
            split_extra=split_extra,
            split_cells=split_cells,
        )


Predicate = Callable[[dict[str, Any], CaseContext], bool]


# --- case-level facts -------------------------------------------------------------------------


def _trailing_unmatched(true_count: int, assignment: Assignment) -> frozenset[int]:
    """The true rows from the last one backwards for as long as each is unmatched."""
    unmatched = set(assignment.unmatched_true)
    tail: set[int] = set()
    for index in range(true_count - 1, -1, -1):
        if index not in unmatched:
            break
        tail.add(index)
    return frozenset(tail)


def _dig(data: object, *path: str) -> object:
    current = data
    for part in path:
        if not isinstance(current, dict):
            return None
        current = current.get(part)
    return current


def _crosses(
    predicted_same: object,
    expected_same: object,
    expected_other: object,
    normalize: Callable[[str], str],
) -> bool:
    """The predicted value is the other party's expected value, and the two parties differ."""
    if not (
        isinstance(predicted_same, str)
        and isinstance(expected_same, str)
        and isinstance(expected_other, str)
    ):
        return False
    other = normalize(expected_other)
    return normalize(predicted_same) == other and normalize(expected_same) != other


def _crossover(predicted: dict[str, Any], expected: dict[str, Any]) -> bool:
    """True when the issuer and recipient identity values of the answer are each other's.

    The predicted issuer CNPJ equals the expected recipient tax id, or the predicted recipient tax
    id equals the expected issuer CNPJ (by ``normalize_id``); or the same for the names (by
    ``normalize_name``). The two expected values must differ, so a party that is also its own
    counterpart shows no crossover.
    """
    pairs = (
        (("issuer", "cnpj"), ("recipient", "tax_id"), normalize_id),
        (("issuer", "name"), ("recipient", "name"), normalize_name),
    )
    for issuer_path, recipient_path, normalize in pairs:
        if _crosses(
            _dig(predicted, *issuer_path),
            _dig(expected, *issuer_path),
            _dig(expected, *recipient_path),
            normalize,
        ) or _crosses(
            _dig(predicted, *recipient_path),
            _dig(expected, *recipient_path),
            _dig(expected, *issuer_path),
            normalize,
        ):
            return True
    return False


def _merged(
    predicted: tuple[dict[str, Any], ...],
    truth: tuple[dict[str, Any], ...],
    assignment: Assignment,
) -> tuple[frozenset[int], frozenset[tuple[int, str]]]:
    """Unmatched true rows ``u`` and matched pairs ``(p, t)`` with ``p[col] == t[col] + u[col]``.

    ``col`` is ``total`` or ``quantity``; ``t[col]`` and ``u[col]`` are non-zero, so a row that
    prints zero is never taken for a merge.
    """
    missing: set[int] = set()
    cells: set[tuple[int, str]] = set()
    for u in assignment.unmatched_true:
        for p, t in assignment.pairs:
            for column in _SUM_COLUMNS:
                have = parse_decimal(predicted[p].get(column))
                base = parse_decimal(truth[t].get(column))
                absorbed = parse_decimal(truth[u].get(column))
                if have is None or base is None or absorbed is None:
                    continue
                if base != _ZERO and absorbed != _ZERO and have == base + absorbed:
                    missing.add(u)
                    cells.add((t, column))
    return frozenset(missing), frozenset(cells)


def _split(
    predicted: tuple[dict[str, Any], ...],
    truth: tuple[dict[str, Any], ...],
    assignment: Assignment,
) -> tuple[frozenset[int], frozenset[tuple[int, str]]]:
    """Unmatched predicted rows ``q`` and matched pairs ``(p, t)``, ``p[col] + q[col] == t[col]``.

    ``t[col]`` and ``q[col]`` are non-zero, so an extra row that prints zero is never a split.
    """
    extra: set[int] = set()
    cells: set[tuple[int, str]] = set()
    for q in assignment.unmatched_predicted:
        for p, t in assignment.pairs:
            for column in _SUM_COLUMNS:
                have = parse_decimal(predicted[p].get(column))
                other = parse_decimal(predicted[q].get(column))
                whole = parse_decimal(truth[t].get(column))
                if have is None or other is None or whole is None:
                    continue
                if whole != _ZERO and other != _ZERO and have + other == whole:
                    extra.add(q)
                    cells.add((t, column))
    return frozenset(extra), frozenset(cells)


# --- what kind of value a wrong value is ------------------------------------------------------


def _is_item(wrong: dict[str, Any]) -> bool:
    return wrong["scope"] == "item"


def _is_value(wrong: dict[str, Any]) -> bool:
    """A wrong value of a matched cell or a header field, not a missing or extra row."""
    return wrong["event"] is None


def _is_numeric(wrong: dict[str, Any]) -> bool:
    if not _is_value(wrong):
        return False
    scope, column = wrong["scope"], wrong["column"]
    if scope == "item":
        return column in NUMERIC_COLUMNS
    if scope == "installment":
        return column == "amount"
    return str(wrong["path"]).startswith("totals.")


def _is_id_like(wrong: dict[str, Any]) -> bool:
    if not _is_value(wrong):
        return False
    scope, column = wrong["scope"], wrong["column"]
    if scope == "item":
        return column in _ITEM_ID_COLUMNS
    if scope == "installment":
        return column == "number"
    return wrong["path"] in _HEADER_ID_PATHS


def _is_near_miss_text(wrong: dict[str, Any]) -> bool:
    if not _is_value(wrong):
        return False
    if _is_item(wrong):
        return wrong["column"] in _ITEM_NEAR_MISS_COLUMNS
    return wrong["scope"] == "header" and wrong["path"] in _HEADER_NEAR_MISS_PATHS


def _is_text_only(wrong: dict[str, Any]) -> bool:
    return (
        _is_value(wrong) and wrong["scope"] == "header" and wrong["path"] in _HEADER_TEXT_ONLY_PATHS
    )


def _decimal_pair(wrong: dict[str, Any]) -> tuple[Decimal | None, Decimal | None]:
    return parse_decimal(wrong["predicted"]), parse_decimal(wrong["expected"])


def _alphanumeric(text: str) -> str:
    return "".join(character for character in text.upper() if character.isalnum())


def _digit_text(value: object, *, numeric: bool) -> str | None:
    """Uppercase letters and digits of a value; a number goes through its plain decimal form."""
    if numeric:
        number = parse_decimal(value)
        return None if number is None else _alphanumeric(format(number, "f"))
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return str(value)
    if isinstance(value, str):
        return _alphanumeric(value)
    return None


def _digit_pair(wrong: dict[str, Any]) -> tuple[str, str] | None:
    """The predicted and expected strings the digit rules compare, ``None`` outside their scope."""
    numeric = _is_numeric(wrong)
    if not numeric and not _is_id_like(wrong):
        return None
    predicted = _digit_text(wrong["predicted"], numeric=numeric)
    expected = _digit_text(wrong["expected"], numeric=numeric)
    if predicted is None or expected is None:
        return None
    return predicted, expected


def _significant_digits(number: Decimal) -> str:
    return format(abs(number).normalize(), "f").replace(".", "").strip("0")


# --- the rules --------------------------------------------------------------------------------


def _schema_invalid(wrong: dict[str, Any], context: CaseContext) -> bool:
    return context.status == "schema_invalid"


def _party_swap(wrong: dict[str, Any], context: CaseContext) -> bool:
    return context.crossover and str(wrong["path"]).startswith(("issuer.", "recipient."))


def _merged_rows(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_item(wrong):
        return False
    if wrong["event"] == "missing":
        return wrong["row"] in context.merged_missing
    return _is_value(wrong) and (wrong["row"], wrong["column"]) in context.merged_cells


def _split_rows(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_item(wrong):
        return False
    if wrong["event"] == "extra":
        return wrong["predicted_row"] in context.split_extra
    return _is_value(wrong) and (wrong["row"], wrong["column"]) in context.split_cells


def _trailing_rows_lost(wrong: dict[str, Any], context: CaseContext) -> bool:
    return _is_item(wrong) and wrong["event"] == "missing" and wrong["row"] in context.trailing_true


def _dropped_row(wrong: dict[str, Any], context: CaseContext) -> bool:
    return wrong["event"] == "missing"


def _invented_row(wrong: dict[str, Any], context: CaseContext) -> bool:
    return wrong["event"] == "extra"


def _value_from_other_row(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not (_is_item(wrong) and _is_value(wrong) and wrong["column"] in _OTHER_ROW_COLUMNS):
        return False
    column, value, row = wrong["column"], wrong["predicted"], wrong["row"]
    if column in NUMERIC_COLUMNS:
        number = parse_decimal(value)
        if number is None or number == _ZERO:
            return False
    for index, other in enumerate(context.true_items):
        if index == row:
            continue
        if column_right(column, value, other.get(column), _ZERO) and not column_right(
            column, value, wrong["expected"], _ZERO
        ):
            return True
    return False


def _value_from_other_box(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_value(wrong):
        return False
    if wrong["scope"] == "header" and str(wrong["path"]).startswith("totals."):
        number = parse_decimal(wrong["predicted"])
        boxes = _dig(context.expected, "totals")
        if number is None or number == _ZERO or not isinstance(boxes, dict):
            return False
        own = str(wrong["path"]).removeprefix("totals.")
        return any(name != own and parse_decimal(box) == number for name, box in boxes.items())
    if _is_item(wrong) and wrong["column"] in _ROW_MONEY_COLUMNS:
        number = parse_decimal(wrong["predicted"])
        row = wrong["row"]
        if number is None or number == _ZERO or row is None or row >= len(context.true_items):
            return False
        true_row = context.true_items[row]
        return any(
            column != wrong["column"] and parse_decimal(true_row.get(column)) == number
            for column in _ROW_MONEY_COLUMNS
        )
    return False


def _thousands_misparse(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_numeric(wrong):
        return False
    predicted, expected = _decimal_pair(wrong)
    if predicted is None or expected is None or predicted in (_ZERO, expected):
        return False
    return _significant_digits(predicted) == _significant_digits(expected)


def _zero_for_blank(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_numeric(wrong):
        return False
    predicted, expected = _decimal_pair(wrong)
    return (
        predicted is not None and expected is not None and predicted == _ZERO and expected != _ZERO
    )


def _digit_transposition(wrong: dict[str, Any], context: CaseContext) -> bool:
    pair = _digit_pair(wrong)
    if pair is None or len(pair[0]) != len(pair[1]):
        return False
    predicted, expected = pair
    differing = [i for i in range(len(predicted)) if predicted[i] != expected[i]]
    if len(differing) != 2 or differing[1] != differing[0] + 1:
        return False
    first, second = differing
    return predicted[first] == expected[second] and predicted[second] == expected[first]


def _digit_substitution(wrong: dict[str, Any], context: CaseContext) -> bool:
    pair = _digit_pair(wrong)
    if pair is None or len(pair[0]) != len(pair[1]):
        return False
    return sum(a != b for a, b in zip(*pair, strict=True)) == 1


def _digit_count(wrong: dict[str, Any], context: CaseContext) -> bool:
    pair = _digit_pair(wrong)
    if pair is None:
        return False
    longer, shorter = sorted(pair, key=len, reverse=True)
    if len(longer) != len(shorter) + 1:
        return False
    return any(longer[:i] + longer[i + 1 :] == shorter for i in range(len(longer)))


def _text_near_miss(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_near_miss_text(wrong):
        return False
    predicted, expected = wrong["predicted"], wrong["expected"]
    if not (isinstance(predicted, str) and isinstance(expected, str)):
        return False
    ratio = SequenceMatcher(
        None, normalize_name(predicted), normalize_name(expected), autojunk=False
    ).ratio()
    return ratio >= NEAR_MISS_RATIO


def _text_wrong(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not (_is_near_miss_text(wrong) or _is_text_only(wrong)):
        return False
    return isinstance(wrong["predicted"], str) and isinstance(wrong["expected"], str)


def _tax_code_wrong(wrong: dict[str, Any], context: CaseContext) -> bool:
    return _is_item(wrong) and _is_value(wrong) and wrong["column"] in _TAX_CODE_COLUMNS


def _date_wrong(wrong: dict[str, Any], context: CaseContext) -> bool:
    if not _is_value(wrong):
        return False
    if wrong["scope"] == "installment":
        return wrong["column"] == "due_date"
    return wrong["scope"] == "header" and wrong["path"] == "issue_date"


def _untagged(wrong: dict[str, Any], context: CaseContext) -> bool:
    return True


PREDICATES: dict[str, Predicate] = {
    "schema_invalid": _schema_invalid,
    "party_swap": _party_swap,
    "merged_rows": _merged_rows,
    "split_rows": _split_rows,
    "trailing_rows_lost": _trailing_rows_lost,
    "dropped_row": _dropped_row,
    "invented_row": _invented_row,
    "value_from_other_row": _value_from_other_row,
    "value_from_other_box": _value_from_other_box,
    "thousands_misparse": _thousands_misparse,
    "zero_for_blank": _zero_for_blank,
    "digit_transposition": _digit_transposition,
    "digit_substitution": _digit_substitution,
    "digit_count": _digit_count,
    "text_near_miss": _text_near_miss,
    "text_wrong": _text_wrong,
    "tax_code_wrong": _tax_code_wrong,
    "date_wrong": _date_wrong,
    "untagged": _untagged,
}


def first_match(wrong: dict[str, Any], context: CaseContext) -> tuple[str, int]:
    """The tag and rule number of the first rule that matches ``wrong``."""
    for rule, tag in enumerate(TAG_ORDER):
        if PREDICATES[tag](wrong, context):
            return tag, rule
    return "untagged", _UNTAGGED_RULE


def _is_count(wrong: dict[str, Any]) -> bool:
    return wrong["scope"] == "header" and wrong["column"] in _COUNT_COLUMNS


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
            tag, rule = "untagged", _UNTAGGED_RULE
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
