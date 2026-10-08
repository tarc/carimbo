"""``carimbo-evals`` command line: ``run`` a case directory against the eval endpoint.

Exit codes of ``run``: 0 every selected case has a completed record, 2 usage or configuration
error (including a missing key), 3 stopped at the cost cap, 4 harness errors remain.
"""

from __future__ import annotations

import asyncio
import os
import secrets
from datetime import UTC, datetime
from decimal import Decimal, InvalidOperation
from pathlib import Path
from typing import Annotated, Any

import typer

from carimbo_evals.grader import DEFAULT_SCHEMA_PATH, DEFAULT_TOLERANCE, grade_run
from carimbo_evals.runner import (
    DEFAULT_MAX_COST_USD,
    DEFAULT_RESERVE_USD,
    STOPPED_COST_CAP,
    discover_cases,
    run_cases,
)
from carimbo_evals.summary import write_summary

API_KEY_ENV = "CARIMBO_EVAL_API_KEY"
EXIT_USAGE = 2
EXIT_COST_CAP = 3
EXIT_HARNESS_ERRORS = 4

app = typer.Typer(
    no_args_is_help=True,
    add_completion=False,
    help="Run the extraction eval and grade stored results.",
)


@app.callback()
def main() -> None:
    """Run the extraction eval and grade stored results."""


def _money(option: str, value: str) -> Decimal:
    try:
        amount = Decimal(value)
    except InvalidOperation:
        raise typer.BadParameter(f"{value!r} is not a decimal amount", param_hint=option) from None
    if not amount.is_finite() or amount < 0:
        raise typer.BadParameter(f"{value!r} must be a non-negative amount", param_hint=option)
    return amount


def _default_run_id() -> str:
    return f"{datetime.now(UTC).strftime('%Y%m%dT%H%M%SZ')}-{secrets.token_hex(3)}"


def _line(record: dict[str, Any]) -> str:
    response = record.get("response")
    response = response if isinstance(response, dict) else {}
    outcome = response.get("outcome")
    outcome_status = outcome.get("status") if isinstance(outcome, dict) else "-"
    cost = response.get("cost_usd")
    return f"{record['case_id']}  {record['status']}  {outcome_status}  cost={cost or '-'}"


@app.command()
def run(
    cases: Annotated[Path, typer.Option(help="Directory of {case_id}.pdf/.xml pairs.")] = Path(
        "data/skeleton"
    ),
    base_url: Annotated[
        str, typer.Option(help="Eval endpoint base URL.")
    ] = "http://127.0.0.1:5080",
    out: Annotated[
        Path | None, typer.Option(help="Run directory (default evals/runs/<run-id>).")
    ] = None,
    run_id: Annotated[
        str | None, typer.Option(help="Run id (default UTC time plus 6 hex).")
    ] = None,
    concurrency: Annotated[int, typer.Option(min=1, help="Maximum requests in flight.")] = 2,
    max_cost_usd: Annotated[str, typer.Option(help="Per-run cost cap in US dollars.")] = str(
        DEFAULT_MAX_COST_USD
    ),
    reserve_usd: Annotated[
        str, typer.Option(help="Assumed cost of the next case when none has been seen yet.")
    ] = str(DEFAULT_RESERVE_USD),
    resume: Annotated[
        bool, typer.Option("--resume", help="Continue a run: skip completed cases.")
    ] = False,
    case: Annotated[
        list[str] | None, typer.Option("--case", help="Only run this case id (repeatable).")
    ] = None,
) -> None:
    """Run the selected cases through the eval endpoint and store one JSONL record per case.

    The eval key is read only from the CARIMBO_EVAL_API_KEY environment variable.
    """
    api_key = os.environ.get(API_KEY_ENV, "")
    if not api_key:
        typer.echo(
            f"error: set {API_KEY_ENV} in the environment (there is no key option)", err=True
        )
        raise typer.Exit(EXIT_USAGE)
    cap = _money("--max-cost-usd", max_cost_usd)
    reserve = _money("--reserve-usd", reserve_usd)

    try:
        selected = discover_cases(cases)
    except (OSError, ValueError) as exc:
        typer.echo(f"error: {exc}", err=True)
        raise typer.Exit(EXIT_USAGE) from exc
    if case:
        known = {c.case_id for c in selected}
        unknown = sorted(set(case) - known)
        if unknown:
            typer.echo(f"error: unknown case id(s): {', '.join(unknown)}", err=True)
            raise typer.Exit(EXIT_USAGE)
        selected = [c for c in selected if c.case_id in set(case)]
    if not selected:
        typer.echo(f"error: no cases found in {cases}", err=True)
        raise typer.Exit(EXIT_USAGE)

    resolved_run_id = run_id or _default_run_id()
    out_dir = out or Path("evals") / "runs" / resolved_run_id
    try:
        report = asyncio.run(
            run_cases(
                selected,
                base_url=base_url,
                api_key=api_key,
                out_dir=out_dir,
                run_id=resolved_run_id,
                concurrency=concurrency,
                max_cost_usd=cap,
                reserve_usd=reserve,
                resume=resume,
                on_record=lambda record: typer.echo(_line(record)),
            )
        )
    except FileExistsError as exc:
        typer.echo(f"error: {exc}", err=True)
        raise typer.Exit(EXIT_USAGE) from exc

    typer.echo(
        f"run {report.run_id}: completed={report.completed} harness_errors={report.harness_errors} "
        f"skipped={report.skipped} spent={report.spent_usd} assumed={report.assumed_usd} "
        f"unpriced={report.unpriced_cases} stopped_reason={report.stopped_reason}  -> {out_dir}"
    )
    if report.stopped_reason == STOPPED_COST_CAP:
        raise typer.Exit(EXIT_COST_CAP)
    if report.harness_errors:
        raise typer.Exit(EXIT_HARNESS_ERRORS)


@app.command()
def grade(
    run: Annotated[Path, typer.Option(help="Run directory containing cases.jsonl.")],
    cases: Annotated[Path, typer.Option(help="Directory with the ground-truth XML.")] = Path(
        "data/skeleton"
    ),
    tolerance: Annotated[str, typer.Option(help="Allowed total_amount difference.")] = str(
        DEFAULT_TOLERANCE
    ),
    schema: Annotated[Path, typer.Option(help="Canonical invoice JSON Schema.")] = (
        DEFAULT_SCHEMA_PATH
    ),
) -> None:
    """Re-grade a stored run offline and write summary.json and summary.md next to it."""
    allowed = _money("--tolerance", tolerance)
    if not (run / "cases.jsonl").is_file():
        typer.echo(f"error: {run / 'cases.jsonl'} not found", err=True)
        raise typer.Exit(EXIT_USAGE)
    try:
        summary = grade_run(run, cases, tolerance=allowed, schema_path=schema)
    except (OSError, ValueError) as exc:
        typer.echo(f"error: {exc}", err=True)
        raise typer.Exit(EXIT_USAGE) from exc
    write_summary(run, summary)
    typer.echo((run / "summary.md").read_text(encoding="utf-8"), nl=False)
