"""Run summary: aggregates per-case grades into ``summary.json`` and ``summary.md``.

Pure and offline: no HTTP client, no sockets. The only volatile value is ``meta.graded_at``.
"""

from __future__ import annotations

import json
from datetime import UTC, datetime
from decimal import Decimal
from pathlib import Path
from typing import Any

from carimbo_evals.matching import INSTALLMENT_COLUMNS, ITEM_COLUMNS
from carimbo_evals.money import parse_cost
from carimbo_evals.slices import SLICES, SLICES_VERSION, Slice
from carimbo_evals.stats import (
    BOOTSTRAP_SEED,
    DEFAULT_RESAMPLES,
    Z,
    bootstrap_intervals,
    nearest_rank,
    rate,
    wilson,
)
from carimbo_evals.tags import TAG_ORDER, TAGS_VERSION

SUMMARY_VERSION = 4
GRADER_VERSION = "grader-003"

OUTCOME_STATUSES = (
    "success",
    "validation_failed",
    "refused",
    "truncated",
    "schema_invalid",
    "infrastructure_failure",
    "harness_error",
)
# Statuses whose answer is graded field by field. A validation_failed candidate is graded and
# counted as caught by the validators, never as a success; a schema_invalid answer is a wrong
# answer; refusals, truncations, infrastructure failures and harness errors are not.
GRADED_STATUSES = ("success", "validation_failed", "schema_invalid")
# Statuses left out of the correct-invoice denominator and reported apart (D-02): the run did not
# get an answer to grade, so `correct` is null for them.
EXCLUDED_STATUSES = ("infrastructure_failure", "harness_error")
# The 27 graded fields of the v2 invoice: header, parties, the 11 totals boxes and the two list
# lengths. Per-item grading is Phase 5.
FIELDS = (
    "access_key",
    "number",
    "series",
    "issue_date",
    "operation_nature",
    "issuer.cnpj",
    "issuer.name",
    "issuer.ie",
    "issuer.uf",
    "recipient.tax_id",
    "recipient.tax_id_kind",
    "recipient.name",
    "recipient.ie",
    "recipient.uf",
    "totals.icms_base",
    "totals.icms_amount",
    "totals.icms_st_base",
    "totals.icms_st_amount",
    "totals.products_total",
    "totals.freight",
    "totals.insurance",
    "totals.discount",
    "totals.other_expenses",
    "totals.ipi_amount",
    "totals.invoice_total",
    "item_count",
    "installment_count",
)
SCHEMA_CHECKS = ("jsonschema", "pydantic")
TOKEN_FIELDS = (
    "input_tokens",
    "output_tokens",
    "cache_read_tokens",
    "cache_write_5m_tokens",
    "cache_write_1h_tokens",
)
CONFIG_KEYS = {
    "models_requested": "model_requested",
    "models_returned": "model_returned",
    "prompt_versions": "prompt_version",
    "max_repairs": "max_repairs",
    "schema_sha256s": "schema_sha256",
    "pricing_versions": "pricing_version",
    "cache_modes": "cache_mode",
    "replicate_salts": "replicate_salt",
}


def tolerance_text(value: Decimal) -> str:
    """A tolerance with at least two decimals, finer ones kept: 0 and 0.00 give ``0.00``."""
    normalized = value.normalize()
    exponent = normalized.as_tuple().exponent
    places = max(2, -exponent) if isinstance(exponent, int) else 2
    return format(normalized.quantize(Decimal(1).scaleb(-places)), "f")


def _unique(case_grades: list[dict[str, Any]], key: str) -> list[str]:
    return sorted({str(c[key]) for c in case_grades if c.get(key) is not None})


def _latency(case_grades: list[dict[str, Any]], key: str = "latency_ms") -> dict[str, int | None]:
    """Min, nearest-rank p50 and p95, max and n of an integer field; None and n 0 without values.

    Nearest rank never interpolates, so every figure is a value some case really had.
    """
    values = sorted(
        c[key] for c in case_grades if isinstance(c.get(key), int) and not isinstance(c[key], bool)
    )
    if not values:
        return {"min": None, "p50": None, "p95": None, "max": None, "n": 0}
    return {
        "min": values[0],
        "p50": nearest_rank(values, 50),
        "p95": nearest_rank(values, 95),
        "max": values[-1],
        "n": len(values),
    }


def _totals(case_grades: list[dict[str, Any]]) -> dict[str, Any]:
    tokens = dict.fromkeys(TOKEN_FIELDS, 0)
    cost = Decimal(0)
    incurred = Decimal(0)
    unpriced = 0
    unpriced_incurred = 0
    for case in case_grades:
        usage = case.get("usage")
        if isinstance(usage, dict):
            for name in TOKEN_FIELDS:
                value = usage.get(name)
                if isinstance(value, int) and not isinstance(value, bool):
                    tokens[name] += value
        if case["status"] == "harness_error":
            continue  # no response, so nothing was priced or unpriced
        priced = parse_cost(case.get("cost_usd"))
        if priced is None:
            unpriced += 1
        else:
            cost += priced
        paid = parse_cost(case.get("incurred_cost_usd"))
        if paid is None:
            unpriced_incurred += 1
        else:
            incurred += paid
    return {
        **tokens,
        # cost_usd is the notional cost (what the calls cost when first made); incurred_cost_usd
        # is what this run paid, zero for a cache replay.
        "cost_usd": format(cost, "f"),
        "incurred_cost_usd": format(incurred, "f"),
        "unpriced_cases": unpriced,
        "unpriced_incurred_cases": unpriced_incurred,
    }


def _cache(cases: list[dict[str, Any]]) -> dict[str, Any]:
    """How much of the run came from the response cache: attempts answered, and the share of hits.

    ``hit_rate`` is the only float in a summary, rounded to 4 places; ``None`` when no attempt was
    answered, which is different from ``0.0`` (answered attempts, none from the cache).
    """
    hits = 0
    answered = 0
    for case in cases:
        for name in ("cache_hits", "answered_attempts"):
            value = case.get(name)
            if not isinstance(value, int) or isinstance(value, bool):
                break
        else:
            hits += case["cache_hits"]
            answered += case["answered_attempts"]
    return {
        "hits": hits,
        "answered_attempts": answered,
        "hit_rate": rate(hits, answered),
    }


def _within_cent(cases: list[dict[str, Any]]) -> dict[str, int]:
    """Wrong values off by R$0.01 or less, and the cases whose every wrong value is such a value.

    Both views come from one grading: regrading at tolerance 0.01 forgives exactly these values.
    """
    values = 0
    only_cents = 0
    for case in cases:
        wrong = case.get("wrong")
        if not isinstance(wrong, list) or not wrong:
            continue
        flagged = sum(1 for value in wrong if value.get("within_cent") is True)
        values += flagged
        only_cents += int(flagged == len(wrong))
    return {"values": values, "cases": only_cents}


def _validator(case: dict[str, Any]) -> dict[str, Any] | None:
    block = case.get("validator")
    return block if isinstance(block, dict) else None


def _validation(cases: list[dict[str, Any]]) -> dict[str, Any]:
    """What the validators caught: cases ending validation_failed, cases with warnings, and for
    each rule id the number of cases whose final findings contain it."""
    rule_counts: dict[str, int] = {}
    with_warnings = 0
    for case in cases:
        block = _validator(case)
        if block is None:
            continue
        warnings = block.get("warnings")
        if isinstance(warnings, int) and not isinstance(warnings, bool) and warnings > 0:
            with_warnings += 1
        rule_ids = block.get("rule_ids")
        for rule_id in set(rule_ids) if isinstance(rule_ids, list) else ():
            rule_counts[rule_id] = rule_counts.get(rule_id, 0) + 1
    return {
        "caught": sum(1 for case in cases if case["status"] == "validation_failed"),
        "with_warnings": with_warnings,
        "rule_counts": dict(sorted(rule_counts.items())),
    }


def _attempts(cases: list[dict[str, Any]]) -> dict[str, Any]:
    """What repair cost: attempts in total, successes that needed more than one, the maximum.

    Cases without an attempt count (a contract 1 record, a harness error) contribute nothing.
    """
    counts = [
        (case["status"], case["attempt_count"])
        for case in cases
        if isinstance(case.get("attempt_count"), int)
        and not isinstance(case["attempt_count"], bool)
    ]
    return {
        "total": sum(count for _, count in counts),
        "repaired": sum(1 for status, count in counts if status == "success" and count > 1),
        "max": max((count for _, count in counts), default=None),
    }


def correct_row(cases: list[dict[str, Any]]) -> dict[str, Any]:
    """The correct-invoice rate of one row of cases, with both counts and a Wilson interval.

    The denominator is every case with a verdict (``correct`` not None), so a refused, truncated or
    schema_invalid case stays in it as not correct (D-02). ``invoices`` is the distinct
    ``base_case`` values among them and is the n of the interval (D-14). ``excluded`` counts the
    infrastructure_failure and harness_error cases left out.
    """
    counted = [case for case in cases if case.get("correct") is not None]
    invoices = _invoices(counted)
    correct = sum(1 for case in counted if case["correct"] is True)
    interval = wilson(correct, len(counted), invoices)
    return {
        "cases": len(counted),
        "invoices": invoices,
        "excluded": sum(1 for case in cases if case["status"] in EXCLUDED_STATUSES),
        "correct": correct,
        "rate": rate(correct, len(counted)),
        "ci95": None if interval is None else [float(bound) for bound in interval],
    }


def silent_block(cases: list[dict[str, Any]]) -> dict[str, Any]:
    """Invoices that passed the validators and were still wrong (D-05, D-06).

    Over the ``success`` cases: ``silent`` have at least one wrong value, ``payment`` of those touch
    a payment value, ``other`` are the rest, so ``payment + other == silent``. A
    ``validation_failed`` case is caught, never silent. ``rate`` and ``ci95`` are null without a
    success case.
    """
    success = [case for case in cases if case["status"] == "success"]
    silent = [case for case in success if case.get("wrong")]
    payment = sum(
        1 for case in silent if any(value.get("payment") is True for value in case["wrong"])
    )
    invoices = _invoices(success)
    interval = wilson(len(silent), len(success), invoices)
    return {
        "success_cases": len(success),
        "invoices": invoices,
        "silent": len(silent),
        "rate": rate(len(silent), len(success)),
        "ci95": None if interval is None else [float(bound) for bound in interval],
        "payment": payment,
        "other": len(silent) - payment,
    }


def _value_label(value: dict[str, Any]) -> str:
    """``issuer.cnpj`` for a header value, ``item.total`` or ``installment.amount`` for a row."""
    scope = value.get("scope")
    if scope == "header":
        return str(value["path"])
    return f"{scope}.{value['column']}"


def wrong_on_success(cases: list[dict[str, Any]]) -> dict[str, int]:
    """Wrong values per field label over ``success`` cases only (D-07), keys sorted.

    These passed the validators and were still wrong, so the block shows what the validators cannot
    see. A wrong value on a ``validation_failed`` case adds nothing.
    """
    counts: dict[str, int] = {}
    for case in cases:
        if case["status"] != "success":
            continue
        for value in case.get("wrong") or []:
            label = _value_label(value)
            counts[label] = counts.get(label, 0) + 1
    return dict(sorted(counts.items()))


def failure_tags(cases: list[dict[str, Any]]) -> dict[str, Any]:
    """Counted failure tags over every wrong value of every graded case (D-10).

    ``counts`` has a key for every tag in ``TAG_ORDER``, zeros kept, so the counts add up to
    ``wrong_values``; ``cases`` counts the cases holding at least one value of the tag; ``untagged``
    repeats the count of the bucket the rules miss.
    """
    counts: dict[str, int] = dict.fromkeys(sorted(TAG_ORDER), 0)
    case_counts: dict[str, int] = dict.fromkeys(sorted(TAG_ORDER), 0)
    for case in cases:
        seen: set[str] = set()
        for value in case.get("wrong") or []:
            tag = value.get("tag") if value.get("tag") in counts else "untagged"
            counts[tag] += 1
            seen.add(tag)
        for tag in seen:
            case_counts[tag] += 1
    return {
        "version": TAGS_VERSION,
        "wrong_values": sum(counts.values()),
        "counts": counts,
        "cases": case_counts,
        "untagged": counts["untagged"],
    }


def incomplete_state(infrastructure: int, harness: int, run_meta: dict[str, Any]) -> dict[str, Any]:
    """Whether the run is unfinished, and how (D-02, EVAL-04).

    Incomplete when any case ended ``infrastructure_failure`` or ``harness_error``, when the cost
    cap stopped the run, or when a selected case has no record. The detail counts each apart from
    wrong answers. A run directory whose ``run.json`` is missing or damaged is incomplete too, and
    says so in ``run_state`` (D-49); that key exists only then.
    """
    detail: dict[str, Any] = {
        "infrastructure_failure": infrastructure,
        "harness_error": harness,
        "cases_not_run": int(run_meta.get("cases_not_run") or 0),
        "stopped_reason": run_meta.get("stopped_reason"),
    }
    if run_meta.get("run_state") is not None:
        detail["run_state"] = run_meta["run_state"]
    flagged = any(
        (
            detail["infrastructure_failure"] > 0,
            detail["harness_error"] > 0,
            detail["cases_not_run"] > 0,
            detail["stopped_reason"] is not None,
            "run_state" in detail,
        )
    )
    return {"incomplete": flagged, "incomplete_detail": detail}


def _hazard_row(cases: list[dict[str, Any]]) -> dict[str, int]:
    return {"cases": len(cases), "correct": sum(1 for case in cases if case.get("correct") is True)}


def hazard_block(cases: list[dict[str, Any]]) -> dict[str, dict[str, int]]:
    """Counts of the text-layer cases that carry each DANFE wrap hazard (D-47): counts only.

    ``any`` and ``none`` partition the text-layer cases. A hazard is reported beside the result, so
    this block is neither a slice nor a headline.
    """
    text = [case for case in cases if case.get("rendering") == "text"]

    def carrying(name: str) -> list[dict[str, Any]]:
        return [case for case in text if name in (case.get("render_hazards") or [])]

    flagged = [case for case in text if case.get("render_hazards")]
    clear = [case for case in text if not case.get("render_hazards")]
    return {
        "unit_wrap": _hazard_row(carrying("unit_wrap")),
        "number_wrap": _hazard_row(carrying("number_wrap")),
        "any": _hazard_row(flagged),
        "none": _hazard_row(clear),
    }


def _invoice_groups(cases: list[dict[str, Any]], key: str) -> list[list[dict[str, Any]]]:
    """The ``key`` blocks (``items`` or ``installments``) of the cases that have one, grouped by
    ``base_case`` and ordered by it, so a base and its twins are one resampling unit (D-14)."""
    groups: dict[str, list[dict[str, Any]]] = {}
    for case in cases:
        block = case.get(key)
        if isinstance(block, dict):
            groups.setdefault(str(case.get("base_case") or case["case_id"]), []).append(block)
    return [groups[name] for name in sorted(groups)]


def _column_counts(blocks: list[dict[str, Any]], column: str) -> tuple[int, int]:
    correct = n = 0
    for block in blocks:
        counts = (block.get("columns") or {}).get(column)
        if isinstance(counts, dict):
            correct += counts["correct"]
            n += counts["n"]
    return correct, n


def row_block(
    cases: list[dict[str, Any]], key: str, *, seed: int, resamples: int
) -> dict[str, Any]:
    """Pooled row metrics of one scope for ``items`` or ``installments`` (D-14, EVAL-05).

    Precision, recall, F1 and the all-values-correct row rate are pooled counts over the cases that
    have the block; each interval is a seeded bootstrap over invoices (``base_case``), so rows of
    one invoice, and an invoice's twins, move together. Values are null when a denominator is 0.
    ``order_preserved`` is descriptive and gates nothing.
    """
    columns = ITEM_COLUMNS if key == "items" else INSTALLMENT_COLUMNS
    groups = _invoice_groups(cases, key)
    vectors: list[list[int]] = []
    for group in groups:
        matched = sum(block["matched"] for block in group)
        predicted = sum(block["predicted_rows"] for block in group)
        true = sum(block["true_rows"] for block in group)
        vector = [
            matched,
            predicted,
            true,
            sum(block["rows_all_correct"] for block in group),
            2 * matched,
            predicted + true,
        ]
        for column in columns:
            vector.extend(_column_counts(group, column))
        vectors.append(vector)
    ratios = {
        "precision": (0, 1),
        "recall": (0, 2),
        "f1": (4, 5),
        "all_values_correct": (3, 2),
        **{column: (6 + 2 * index, 7 + 2 * index) for index, column in enumerate(columns)},
    }
    intervals = bootstrap_intervals(vectors, ratios, seed=seed, resamples=resamples)
    totals = (
        [sum(vector[index] for vector in vectors) for index in range(6)] if vectors else [0] * 6
    )
    matched, predicted, true, all_correct, doubled, both = totals

    def point(numerator: int, denominator: int, name: str) -> dict[str, Any]:
        return {"value": rate(numerator, denominator), "ci95": intervals[name]}

    blocks = [block for group in groups for block in group]
    eligible = [block for block in blocks if block["matched"] >= 2]
    column_rows: dict[str, Any] = {}
    for index, column in enumerate(columns):
        correct, n = (
            sum(vector[6 + 2 * index + offset] for vector in vectors) for offset in (0, 1)
        )
        column_rows[column] = {
            "correct": correct,
            "n": n,
            "rate": rate(correct, n),
            "ci95": intervals[column],
        }
    return {
        "true_rows": true,
        "predicted_rows": predicted,
        "matched": matched,
        "precision": point(matched, predicted, "precision"),
        "recall": point(matched, true, "recall"),
        "f1": point(doubled, both, "f1"),
        "all_values_correct": point(all_correct, true, "all_values_correct"),
        "columns": column_rows,
        "order_preserved": {
            "cases": sum(1 for block in eligible if block["order_preserved"] is True),
            "n": len(eligible),
        },
    }


def column_intervals(
    cases: list[dict[str, Any]], *, seed: int, resamples: int
) -> tuple[int, dict[str, list[float] | None]]:
    """The invoice count and, for each ``item.<column>`` and ``installment.<column>``, a seeded
    bootstrap interval over the invoices of one slice, from a single resample draw (D-14, D-44).

    An invoice's vector sums its cases, so a base and its twins move together. Every call starts
    from ``seed``, so a slice's intervals depend on no other slice. With no invoice nothing is drawn
    and every name maps to None.
    """
    names = [f"item.{column}" for column in ITEM_COLUMNS] + [
        f"installment.{column}" for column in INSTALLMENT_COLUMNS
    ]
    per_invoice: dict[str, list[int]] = {}
    for case in cases:
        has_block = any(isinstance(case.get(key), dict) for key in ("items", "installments"))
        if not has_block:
            continue
        vector = per_invoice.setdefault(
            str(case.get("base_case") or case["case_id"]), [0] * (2 * len(names))
        )
        offset = 0
        for key, columns in (("items", ITEM_COLUMNS), ("installments", INSTALLMENT_COLUMNS)):
            block = case.get(key)
            for column in columns:
                if isinstance(block, dict):
                    correct, n = _column_counts([block], column)
                    vector[offset] += correct
                    vector[offset + 1] += n
                offset += 2
    vectors = [per_invoice[name] for name in sorted(per_invoice)]
    ratios = {name: (2 * index, 2 * index + 1) for index, name in enumerate(names)}
    return len(vectors), bootstrap_intervals(vectors, ratios, seed=seed, resamples=resamples)


def scopes(
    cases: list[dict[str, Any]], run_meta: dict[str, Any]
) -> dict[str, list[dict[str, Any]]]:
    """The case groups a summary reports: ``overall`` always, ``test`` and ``dev`` when a case has
    that split, and ``full`` only when the run holds every manifest case and each has a split."""
    found: dict[str, list[dict[str, Any]]] = {"overall": cases}
    for name in ("test", "dev"):
        members = [case for case in cases if case.get("split") == name]
        if members:
            found[name] = members
    manifest_ids = run_meta.get("manifest_case_ids")
    if (
        manifest_ids
        and {case["case_id"] for case in cases} >= set(manifest_ids)
        and all(case.get("split") is not None for case in cases)
    ):
        found["full"] = cases
    return found


def _rate_entry(correct: int, n: int, invoices: int) -> dict[str, Any]:
    interval = wilson(correct, n, invoices)
    return {
        "correct": correct,
        "n": n,
        "invoices": invoices,
        "rate": rate(correct, n),
        "ci95": None if interval is None else [float(bound) for bound in interval],
    }


def _invoices(cases: list[dict[str, Any]]) -> int:
    return len({str(case.get("base_case") or case["case_id"]) for case in cases})


def _field_stats(cases: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    """Per header field: cases graded on it, distinct invoices among them, rate and Wilson interval.

    ``field_accuracy`` keeps its two-key entries; this block adds the invoices and the interval.
    """
    stats: dict[str, dict[str, Any]] = {}
    for field in FIELDS:
        members = [
            case
            for case in cases
            if isinstance(case.get("fields"), dict) and field in case["fields"]
        ]
        correct = sum(1 for case in members if case["fields"][field] is True)
        stats[field] = _rate_entry(correct, len(members), _invoices(members))
    return stats


def _column_stats(cases: list[dict[str, Any]], block: str, prefix: str) -> dict[str, Any]:
    """Pooled row counts per column, summed over the cases' ``items`` or ``installments`` blocks.

    Rows of one invoice are correlated, so these carry counts and a rate only (D-14).
    """
    names = ITEM_COLUMNS if block == "items" else INSTALLMENT_COLUMNS
    blocks = [case[block] for case in cases if isinstance(case.get(block), dict)]
    entries: dict[str, Any] = {}
    for column in names:
        correct, n = _column_counts(blocks, column)
        entries[f"{prefix}.{column}"] = {"correct": correct, "n": n, "rate": rate(correct, n)}
    return entries


def _field_by_slice(members: list[dict[str, Any]], resamples: int) -> dict[str, Any]:
    """Header fields with a Wilson interval; every item and installment column with the slice's
    invoice count and a seeded bootstrap interval from one draw per slice (D-14, D-44)."""
    entries = {
        **_field_stats(members),
        **_column_stats(members, "items", "item"),
        **_column_stats(members, "installments", "installment"),
    }
    invoices, intervals = column_intervals(members, seed=BOOTSTRAP_SEED, resamples=resamples)
    for name, interval in intervals.items():
        entries[name]["invoices"] = invoices
        entries[name]["ci95"] = interval
    return entries


def _headline(cases: list[dict[str, Any]]) -> dict[str, Any]:
    text = [case for case in cases if case.get("rendering") == "text"]
    image = [case for case in cases if case.get("rendering") not in (None, "text")]
    return {"text": correct_row(text), "image": correct_row(image), "pooled": correct_row(cases)}


def _in_slice(cases: list[dict[str, Any]], declared: Slice) -> list[dict[str, Any]]:
    return [case for case in cases if declared.matches(case)]


def build_summary(
    case_grades: list[dict[str, Any]],
    run_meta: dict[str, Any],
    manifest: dict[str, Any] | None,
) -> dict[str, Any]:
    """Aggregate per-case grades.

    ``run_meta`` carries ``run_id`` and ``tolerance``; ``manifest`` is ``{name, version,
    manifest_sha256}`` or ``None`` when the cases directory has no manifest.
    """
    cases = sorted(case_grades, key=lambda c: c["case_id"])
    counts = dict.fromkeys(OUTCOME_STATUSES, 0)
    for case in cases:
        counts[case["status"]] += 1
    field_accuracy = {field: {"correct": 0, "n": 0} for field in FIELDS}
    schema_validity = {check: {"correct": 0, "n": 0} for check in SCHEMA_CHECKS}
    for case in cases:
        for field, correct in case.get("fields", {}).items():
            field_accuracy[field]["n"] += 1
            field_accuracy[field]["correct"] += int(correct)
        if case["status"] in GRADED_STATUSES:
            for check in SCHEMA_CHECKS:
                schema_validity[check]["n"] += 1
                schema_validity[check]["correct"] += int(bool(case.get(f"schema_valid_{check}")))
    groups = scopes(cases, run_meta)
    resamples = int(run_meta.get("resamples", DEFAULT_RESAMPLES))
    return {
        "summary_version": SUMMARY_VERSION,
        "grader_version": GRADER_VERSION,
        "run_id": run_meta["run_id"],
        "tolerance": tolerance_text(Decimal(str(run_meta["tolerance"]))),
        "dataset": manifest,
        "config": {name: _unique(cases, key) for name, key in CONFIG_KEYS.items()},
        "counts": {**counts, "total": len(cases)},
        "slices_version": SLICES_VERSION,
        "primary_metric": {
            "name": "correct_invoice_rate",
            "headline_row": "test.text",
            "second_row": "test.image",
            "correct": "status success and no wrong value",
            "denominator": "every case except infrastructure_failure and harness_error",
        },
        "stats": {
            "confidence": "0.95",
            "z": str(Z),
            "interval": "wilson",
            "n": "distinct invoices (base_case)",
            "bootstrap": {
                "seed": BOOTSTRAP_SEED,
                "resamples": resamples,
                "unit": "base_case",
                "method": "percentile",
                "rng": "random.Random.random",
            },
            "field_by_slice": (
                "wilson on header fields; seeded bootstrap over the slice's invoices on item and "
                "installment columns, narrow on few invoices (D-44)"
            ),
        },
        "overall": correct_row(cases),
        "headline": {
            name: _headline(members) for name, members in groups.items() if name != "overall"
        },
        "slices": {
            name: {sl.slice_id: correct_row(_in_slice(members, sl)) for sl in SLICES}
            for name, members in groups.items()
            if name != "overall"
        },
        "field_stats": {name: _field_stats(members) for name, members in groups.items()},
        "field_by_slice": {
            name: {sl.slice_id: _field_by_slice(_in_slice(members, sl), resamples) for sl in SLICES}
            for name, members in groups.items()
            if name != "overall"
        },
        "items": {
            name: row_block(members, "items", seed=BOOTSTRAP_SEED, resamples=resamples)
            for name, members in groups.items()
        },
        "installments": {
            name: row_block(members, "installments", seed=BOOTSTRAP_SEED, resamples=resamples)
            for name, members in groups.items()
        },
        "silent_errors": {name: silent_block(members) for name, members in groups.items()},
        "wrong_on_success": wrong_on_success(cases),
        "tags_version": TAGS_VERSION,
        "failure_tags": failure_tags(cases),
        **incomplete_state(counts["infrastructure_failure"], counts["harness_error"], run_meta),
        "render_hazards": {
            name: hazard_block(members)
            for name, members in groups.items()
            if name in ("test", "dev", "full")
        },
        "field_accuracy": field_accuracy,
        "schema_validity": schema_validity,
        "within_cent": _within_cent(cases),
        "validation": _validation(cases),
        "attempts": _attempts(cases),
        "cases": cases,
        "totals": _totals(cases),
        "cache": _cache(cases),
        "latency_ms": _latency(cases),
        "model_latency_ms": _latency(cases, "model_latency_ms"),
        "meta": {"graded_at": datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")},
    }


ROW_HEADER = (
    "| row | cases | invoices | excluded | correct | rate | 95% interval |",
    "|---|---|---|---|---|---|---|",
)


def _row_line(label: str, row: dict[str, Any]) -> str:
    rate_text = "-" if row["rate"] is None else f"{row['rate']:.4f}"
    interval = row["ci95"]
    interval_text = "-" if interval is None else f"[{interval[0]:.4f}, {interval[1]:.4f}]"
    return (
        f"| {label} | {row['cases']} | {row['invoices']} | {row['excluded']} | {row['correct']} "
        f"| {rate_text} | {interval_text} |"
    )


def _scope_label(scope: str, row_name: str) -> str:
    """``test text (primary)``; every dev figure is labelled dev, test and full are not."""
    primary = " (primary)" if (scope, row_name) == ("test", "text") else ""
    dev = " (dev)" if scope == "dev" else ""
    return f"{scope} {row_name}{primary}{dev}"


def _hazard_notes(summary: dict[str, Any]) -> list[str]:
    """One note line per scope that has text-layer cases with a wrap hazard (D-47)."""
    notes: list[str] = []
    for scope in ("test", "dev"):
        block = summary["render_hazards"].get(scope)
        if block is None or block["any"]["cases"] == 0:
            continue
        total = block["any"]["cases"] + block["none"]["cases"]
        notes.append(
            f"{scope} text: {block['any']['cases']} of {total} cases carry a DANFE wrap hazard "
            "(see D-47)"
        )
    return notes


def _correct_invoices_lines(summary: dict[str, Any]) -> list[str]:
    lines = ["## Correct invoices", "", *ROW_HEADER]
    headline = summary["headline"]
    if not headline:
        lines.append(_row_line("overall", summary["overall"]))
    for scope, rows in headline.items():
        for row_name in ("text", "image", "pooled"):
            lines.append(_row_line(_scope_label(scope, row_name), rows[row_name]))
    notes = _hazard_notes(summary)
    return [*lines, "", *(f"- {note}" for note in notes), *([""] if notes else [])]


def _silent_lines(summary: dict[str, Any]) -> list[str]:
    lines = [
        "## Silent errors",
        "",
        "| scope | success cases | invoices | silent | rate | 95% interval | payment | other |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for scope, block in summary["silent_errors"].items():
        rate_text = "-" if block["rate"] is None else f"{block['rate']:.4f}"
        interval = block["ci95"]
        interval_text = "-" if interval is None else f"[{interval[0]:.4f}, {interval[1]:.4f}]"
        lines.append(
            f"| {scope}{' (dev)' if scope == 'dev' else ''} | {block['success_cases']} "
            f"| {block['invoices']} | {block['silent']} | {rate_text} | {interval_text} "
            f"| {block['payment']} | {block['other']} |"
        )
    return [*lines, ""]


def _failure_tag_lines(summary: dict[str, Any]) -> list[str]:
    tags = summary["failure_tags"]
    lines = ["## Failure tags", "", "| tag | values | cases |", "|---|---|---|"]
    for tag in TAG_ORDER:
        if tags["counts"][tag]:
            lines.append(f"| {tag} | {tags['counts'][tag]} | {tags['cases'][tag]} |")
    lines.append("")
    lines.append(
        f"{tags['wrong_values']} wrong values, {tags['untagged']} untagged, "
        f"tag list {tags['version']}"
    )
    return [*lines, ""]


def _incomplete_line(summary: dict[str, Any]) -> list[str]:
    if not summary["incomplete"]:
        return []
    detail = summary["incomplete_detail"]
    parts = [
        f"{detail['infrastructure_failure']} infrastructure_failure",
        f"{detail['harness_error']} harness_error",
        f"{detail['cases_not_run']} selected cases not run",
    ]
    if detail["stopped_reason"] is not None:
        parts.append(f"stopped: {detail['stopped_reason']}")
    if "run_state" in detail:
        parts.append(f"run.json {detail['run_state']}")
    return [f"- INCOMPLETE: {', '.join(parts)}"]


def _percent_text(point: dict[str, Any]) -> str:
    value, interval = point["value"], point["ci95"]
    if value is None:
        return "-"
    if interval is None:
        return f"{value:.4f}"
    return f"{value:.4f} [{interval[0]:.4f}, {interval[1]:.4f}]"


def _items_lines(summary: dict[str, Any]) -> list[str]:
    lines = [
        "## Items",
        "",
        "| scope | true rows | predicted rows | matched | precision | recall | F1 "
        "| all values correct |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for scope, block in summary["items"].items():
        lines.append(
            f"| {scope}{' (dev)' if scope == 'dev' else ''} | {block['true_rows']} "
            f"| {block['predicted_rows']} | {block['matched']} "
            f"| {_percent_text(block['precision'])} | {_percent_text(block['recall'])} "
            f"| {_percent_text(block['f1'])} | {_percent_text(block['all_values_correct'])} |"
        )
    return [*lines, ""]


def _rendering_lines(summary: dict[str, Any]) -> list[str]:
    if not summary["slices"]:
        return []
    lines = ["## Rendering", "", *ROW_HEADER]
    for scope, rows in summary["slices"].items():
        suffix = " (dev)" if scope == "dev" else ""
        for slice_id, row in rows.items():
            if slice_id.startswith("rendering="):
                lines.append(_row_line(f"{scope} {slice_id}{suffix}", row))
    return [*lines, ""]


def _model_latency_line(summary: dict[str, Any]) -> str:
    latency = summary["model_latency_ms"]
    if latency["n"] == 0:
        return "- model latency: -"
    return (
        f"- model latency: p50 {latency['p50']} ms, p95 {latency['p95']} ms "
        "(original latency, also on a replay)"
    )


def render_markdown(summary: dict[str, Any]) -> str:
    counts = summary["counts"]
    count_text = ", ".join(f"{name} {counts[name]}" for name in OUTCOME_STATUSES if counts[name])
    models = ", ".join(
        summary["config"]["models_returned"] or summary["config"]["models_requested"]
    )
    totals = summary["totals"]
    cache = summary["cache"]
    lines = [
        f"# Run {summary['run_id']}",
        "",
        *_incomplete_line(summary),
        f"- models: {models or '-'}",
        f"- cases: {counts['total']} ({count_text or 'none'})",
        f"- cost: US${totals['incurred_cost_usd']} incurred, US${totals['cost_usd']} notional "
        f"({totals['unpriced_cases']} unpriced)",
        f"- cache: {cache['hits']}/{cache['answered_attempts']} answered attempts from the cache",
        f"- tokens in/out: {totals['input_tokens']}/{totals['output_tokens']}",
        _model_latency_line(summary),
        f"- validation_failed (caught): {summary['validation']['caught']}",
        f"- grader: {summary['grader_version']}, tolerance {summary['tolerance']}",
        "",
        *_correct_invoices_lines(summary),
        *_rendering_lines(summary),
        *_items_lines(summary),
        *_silent_lines(summary),
        *_failure_tag_lines(summary),
        "## Cases",
        "",
        "| case_id | status | fields | attempts | hits | findings | total delta | cost "
        "| latency ms | trace id |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for case in summary["cases"]:
        fields = case.get("fields")
        graded = f"{sum(fields.values())}/{len(fields)}" if fields is not None else "-"
        validator = _validator(case)
        findings = "-" if validator is None else f"{validator['errors']}E/{validator['warnings']}W"
        attempts = case.get("attempt_count")
        hits, answered = case.get("cache_hits"), case.get("answered_attempts")
        cached = (
            f"{hits}/{answered}" if isinstance(hits, int) and isinstance(answered, int) else "-"
        )
        lines.append(
            "| "
            + " | ".join(
                [
                    case["case_id"],
                    case["status"],
                    graded,
                    "-" if attempts is None else str(attempts),
                    cached,
                    findings,
                    str(case.get("total_delta") or "-"),
                    str(case.get("cost_usd") or "-"),
                    str(case.get("latency_ms") if case.get("latency_ms") is not None else "-"),
                    str(case.get("trace_id") or "-"),
                ]
            )
            + " |"
        )
    return "\n".join(lines) + "\n"


def write_summary(run_dir: Path, summary: dict[str, Any]) -> None:
    """Write ``summary.json`` (sorted keys, indent 2) and ``summary.md`` into ``run_dir``."""
    text = json.dumps(summary, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    (run_dir / "summary.json").write_text(text, encoding="utf-8")
    (run_dir / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
