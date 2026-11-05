"""Seeded corruptions of a ground-truth invoice, one per failure tag (EVAL-04).

``CORRUPTIONS`` is keyed by the tag each corruption must produce, so the pytest self-tests and the
end-to-end proof over HTTP (plan 05-10) share one catalogue. A corruption works on a deep copy of
the invoice and returns ``None`` when it does not apply to that invoice (too few rows, a zero value
to copy, and so on). ``stored_record`` wraps an invoice in a run record the grader can read.

Pure and offline: standard library only, no HTTP client.
"""

from __future__ import annotations

import copy
import json
import random
from collections.abc import Callable
from decimal import Decimal
from typing import Any

Corruption = Callable[[dict[str, Any], random.Random], dict[str, Any] | None]

CORRUPTIONS: dict[str, Corruption] = {}


def _corruption(tag: str) -> Callable[[Callable[..., bool]], Corruption]:
    """Register ``apply(invoice, rng) -> bool`` under ``tag`` as a corruption of a deep copy."""

    def register(apply: Callable[..., bool]) -> Corruption:
        def corrupt(invoice: dict[str, Any], rng: random.Random) -> dict[str, Any] | None:
            copied = copy.deepcopy(invoice)
            return copied if apply(copied, rng) else None

        corrupt.__name__ = f"corrupt_{tag}"
        CORRUPTIONS[tag] = corrupt
        return corrupt

    return register


def _money(value: str) -> Decimal:
    return Decimal(value)


@_corruption("party_swap")
def _party_swap(invoice: dict[str, Any], rng: random.Random) -> bool:
    issuer, recipient = invoice["issuer"], invoice["recipient"]
    issuer["cnpj"], recipient["tax_id"] = recipient["tax_id"], issuer["cnpj"]
    issuer["name"], recipient["name"] = recipient["name"], issuer["name"]
    return True


@_corruption("trailing_rows_lost")
def _trailing_rows_lost(invoice: dict[str, Any], rng: random.Random) -> bool:
    if len(invoice["items"]) < 4:
        return False
    del invoice["items"][-2:]
    return True


@_corruption("dropped_row")
def _dropped_row(invoice: dict[str, Any], rng: random.Random) -> bool:
    rows = len(invoice["items"])
    if rows < 4:
        return False
    del invoice["items"][rng.randrange(1, rows - 2)]
    return True


@_corruption("invented_row")
def _invented_row(invoice: dict[str, Any], rng: random.Random) -> bool:
    invented = copy.deepcopy(invoice["items"][0])
    invented.update(
        code="ZZ9999",
        description="PRODUTO INEXISTENTE INVENTADO",
        ncm="99999999",
        unit="KG",
        quantity="1.0000",
        unit_price="1.0000",
        total="1.00",
        icms_base="0.00",
        icms_amount="0.00",
    )
    invoice["items"].insert(rng.randrange(0, len(invoice["items"]) + 1), invented)
    return True


@_corruption("merged_rows")
def _merged_rows(invoice: dict[str, Any], rng: random.Random) -> bool:
    items = invoice["items"]
    if len(items) < 3:
        return False
    i = rng.randrange(0, len(items) - 1)
    first, second = items[i], items[i + 1]
    if _money(first["total"]) == 0 or _money(second["total"]) == 0:
        return False
    first["quantity"] = str(_money(first["quantity"]) + _money(second["quantity"]))
    first["total"] = str(_money(first["total"]) + _money(second["total"]))
    del items[i + 1]
    return True


@_corruption("split_rows")
def _split_rows(invoice: dict[str, Any], rng: random.Random) -> bool:
    items = invoice["items"]
    i = rng.randrange(0, len(items))
    row = items[i]
    total, quantity = _money(row["total"]), _money(row["quantity"])
    if total < Decimal("0.10") or quantity < Decimal("0.0002"):
        return False
    half_quantity = (quantity / 2).quantize(Decimal("0.0001"))
    half_total = (total / 2).quantize(Decimal("0.01"))
    other = copy.deepcopy(row)
    row["quantity"], row["total"] = str(half_quantity), str(half_total)
    other["quantity"], other["total"] = str(quantity - half_quantity), str(total - half_total)
    items.insert(i + 1, other)
    return True


@_corruption("value_from_other_row")
def _value_from_other_row(invoice: dict[str, Any], rng: random.Random) -> bool:
    items = invoice["items"]
    if len(items) < 3:
        return False
    i = rng.randrange(0, len(items))
    j = (i + 1) % len(items)
    if _money(items[i]["total"]) == _money(items[j]["total"]) or _money(items[j]["total"]) == 0:
        return False
    items[i]["total"] = items[j]["total"]
    return True


@_corruption("value_from_other_box")
def _value_from_other_box(invoice: dict[str, Any], rng: random.Random) -> bool:
    totals = invoice["totals"]
    products = _money(totals["products_total"])
    if products == 0 or products == _money(totals["freight"]):
        return False
    totals["freight"] = totals["products_total"]
    return True


@_corruption("thousands_misparse")
def _thousands_misparse(invoice: dict[str, Any], rng: random.Random) -> bool:
    total = _money(invoice["totals"]["invoice_total"])
    if total < 1:
        return False
    invoice["totals"]["invoice_total"] = format(total * 100, "f")
    return True


@_corruption("zero_for_blank")
def _zero_for_blank(invoice: dict[str, Any], rng: random.Random) -> bool:
    for row in invoice["items"]:
        if _money(row["icms_amount"]) != 0:
            row["icms_amount"] = "0.00"
            return True
    return False


@_corruption("digit_transposition")
def _digit_transposition(invoice: dict[str, Any], rng: random.Random) -> bool:
    key = invoice["access_key"]
    for i in range(len(key) - 1):
        if key[i].isdigit() and key[i + 1].isdigit() and key[i] != key[i + 1]:
            invoice["access_key"] = key[:i] + key[i + 1] + key[i] + key[i + 2 :]
            return True
    return False


@_corruption("digit_substitution")
def _digit_substitution(invoice: dict[str, Any], rng: random.Random) -> bool:
    cnpj = invoice["issuer"]["cnpj"]
    for i, character in enumerate(cnpj):
        if character.isdigit():
            invoice["issuer"]["cnpj"] = (
                cnpj[:i] + ("1" if character == "0" else "0") + cnpj[i + 1 :]
            )
            return True
    return False


@_corruption("digit_count")
def _digit_count(invoice: dict[str, Any], rng: random.Random) -> bool:
    invoice["recipient"]["tax_id"] = invoice["recipient"]["tax_id"][:-1]
    return True


@_corruption("text_near_miss")
def _text_near_miss(invoice: dict[str, Any], rng: random.Random) -> bool:
    description = invoice["items"][0]["description"]
    if len(description) < 12:
        return False
    invoice["items"][0]["description"] = description[:-1]
    return True


@_corruption("text_wrong")
def _text_wrong(invoice: dict[str, Any], rng: random.Random) -> bool:
    if invoice["operation_nature"] == "DEVOLUCAO DE MERCADORIA":
        return False
    invoice["operation_nature"] = "DEVOLUCAO DE MERCADORIA"
    return True


@_corruption("tax_code_wrong")
def _tax_code_wrong(invoice: dict[str, Any], rng: random.Random) -> bool:
    row = invoice["items"][0]
    row["cfop"] = "5102" if row["cfop"] in ("6403", "6404") else "6403"
    return True


@_corruption("date_wrong")
def _date_wrong(invoice: dict[str, Any], rng: random.Random) -> bool:
    year, month, day = invoice["issue_date"].split("-")
    invoice["issue_date"] = f"{year}-{month}-{'03' if day == '02' else '02'}"
    return True


@_corruption("untagged")
def _untagged(invoice: dict[str, Any], rng: random.Random) -> bool:
    totals = invoice["totals"]
    totals["icms_amount"] = format(_money(totals["icms_amount"]) + Decimal("3817.46"), "f")
    return True


def corrupted(tag: str, invoice: dict[str, Any], seed: str | int) -> dict[str, Any] | None:
    """The invoice with the ``tag`` corruption applied, or ``None`` when it does not apply.

    The generator is ``random.Random(f"{seed}-{tag}")``, so the same seed and tag always pick the
    same row.
    """
    return CORRUPTIONS[tag](invoice, random.Random(f"{seed}-{tag}"))


def stored_record(
    case_id: str,
    invoice: dict[str, Any] | None,
    *,
    status: str = "success",
    run_id: str = "self-test",
    cost: str | None = "0.0100",
    latency_ms: int = 1500,
) -> dict[str, Any]:
    """A contract 3, record version 3 run record whose outcome carries ``invoice``.

    ``status`` is the outcome status (``success``, ``validation_failed``, ``refused``,
    ``truncated``, ``schema_invalid`` or ``infrastructure_failure``); ``harness_error`` gives a
    record whose own status is not ``completed`` and that has no response, which is how the runner
    stores a call that never got an answer.
    """
    request = {"pdf_sha256": "0" * 64, "traceparent": "x", "trace_id": "a" * 32}
    if status == "harness_error":
        return {
            "record_version": 3,
            "run_id": run_id,
            "case_id": case_id,
            "status": "harness_error",
            "request": request,
            "http": {"status": 502, "wall_ms": 7, "error": "HTTP 502", "error_type": None},
            "response": None,
        }
    answered = status != "infrastructure_failure"
    usage = {"input_tokens": 1000, "output_tokens": 100}
    return {
        "record_version": 3,
        "run_id": run_id,
        "case_id": case_id,
        "status": "completed",
        "request": request,
        "http": {"status": 200, "wall_ms": latency_ms + 5, "error": None, "error_type": None},
        "response": {
            "contract_version": "3",
            "case_id": case_id,
            "trace_id": "a" * 32,
            "effective": {
                "model": "claude-haiku-4-5",
                "prompt_version": "extract-003",
                "schema_sha256": "b" * 64,
                "pricing_version": "pricing-001",
                "max_repairs": 2,
                "cache_mode": "off",
                "replicate_salt": None,
            },
            "outcome": {
                "status": status,
                "invoice": invoice,
                "failure": None if status == "success" else {"kind": status},
                "raw_output": None if invoice is None else json.dumps(invoice),
                "findings": [],
            },
            "attempts": (
                [
                    {
                        "index": 0,
                        "kind": "initial",
                        "status": status,
                        "findings": [],
                        "usage": usage,
                        "latency_ms": latency_ms,
                        "cost_usd": cost,
                        "incurred_cost_usd": cost,
                        "cache": None,
                    }
                ]
                if answered
                else []
            ),
            "usage": usage if answered else None,
            "cost_usd": cost if answered else None,
            "incurred_cost_usd": cost if answered else None,
            "latency_ms": latency_ms,
            "stop_reason": "end_turn" if answered else None,
            "model_returned": "claude-haiku-4-5" if answered else None,
            "provider_message_id": "msg_1" if answered else None,
        },
    }
