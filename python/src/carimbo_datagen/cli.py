"""Command line for the seeded skeleton dataset: ``build`` writes it, ``check`` proves it current.

``build`` renders every case through ``build_nfe_xml`` and ``render_danfe`` and writes a manifest
of per-file SHA-256 values. ``check`` regenerates into a temporary directory and compares bytes
with the committed directory, so silent drift (a dependency bump, another interpreter) is loud
(D-07).

The manifest holds no timestamps, hostnames, absolute paths or interpreter patch versions, so it is
identical on every machine that has the pinned generator packages.
"""

from __future__ import annotations

import hashlib
import json
import os
import tempfile
from collections.abc import Sequence
from importlib.metadata import version
from pathlib import Path
from typing import Annotated, Any

import typer

from carimbo_datagen.danfe import render_danfe
from carimbo_datagen.nfe_xml import build_nfe_xml
from carimbo_datagen.spec import CASE_IDS, MASTER_SEED, build_case_spec

DATASET = "skeleton"
DATASET_VERSION = "skeleton-001"
DEFAULT_DIR = Path("data/skeleton")
MANIFEST_NAME = "manifest.json"
_PYTHON_MAJOR_MINOR = "3.12"
_GENERATOR_PACKAGES = (
    "carimbo",
    "brazilfiscalreport",
    "fpdf2",
    "python-barcode",
    "faker",
    "lxml",
)
_TAGS = {
    "case-001": ["numeric_cnpj", "simples_nacional", "single_page"],
    "case-002": ["alphanumeric_cnpj", "simples_nacional", "single_page"],
    "case-003": ["many_items", "multi_page", "numeric_cnpj", "simples_nacional"],
}

app = typer.Typer(
    no_args_is_help=True,
    add_completion=False,
    help="Seeded synthetic NF-e dataset generator: build the skeleton cases and check them.",
)


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _write_atomic(path: Path, data: bytes) -> None:
    """Write via a temporary sibling then ``os.replace``, so no half-written file is ever left."""
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)


def _render_pdf_atomic(xml: bytes, pdf_path: Path) -> int:
    tmp = pdf_path.with_name(pdf_path.name + ".tmp")
    pages = render_danfe(xml, tmp)
    os.replace(tmp, pdf_path)
    return pages


def build_dataset(seed: int, out_dir: Path, case_ids: Sequence[str]) -> dict[str, Any]:
    """Write each case's XML+PDF pair and ``manifest.json`` to ``out_dir``; return the manifest."""
    out_dir.mkdir(parents=True, exist_ok=True)
    cases: list[dict[str, Any]] = []
    for case_id in sorted(case_ids):
        if case_id not in _TAGS:
            raise ValueError(f"unknown case {case_id!r}; known cases: {', '.join(CASE_IDS)}")
        xml = build_nfe_xml(build_case_spec(seed, case_id))
        xml_name, pdf_name = f"{case_id}.xml", f"{case_id}.pdf"
        _write_atomic(out_dir / xml_name, xml)
        pages = _render_pdf_atomic(xml, out_dir / pdf_name)
        cases.append(
            {
                "case_id": case_id,
                "xml": xml_name,
                "xml_sha256": _sha256(out_dir / xml_name),
                "pdf": pdf_name,
                "pdf_sha256": _sha256(out_dir / pdf_name),
                "pages": pages,
                "tags": _TAGS[case_id],
            }
        )
    manifest: dict[str, Any] = {
        "dataset": DATASET,
        "dataset_version": DATASET_VERSION,
        "master_seed": seed,
        "python": _PYTHON_MAJOR_MINOR,
        "generator": {name: version(name) for name in _GENERATOR_PACKAGES},
        "cases": cases,
    }
    text = json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    _write_atomic(out_dir / MANIFEST_NAME, text.encode("utf-8"))
    return manifest


@app.command()
def build(
    seed: Annotated[int, typer.Option(help="Master seed; each case derives its own RNG.")] = (
        MASTER_SEED
    ),
    out: Annotated[Path, typer.Option(help="Output directory.")] = DEFAULT_DIR,
    case: Annotated[
        list[str] | None,
        typer.Option(help="Case id to build; repeat for several. Default: all cases."),
    ] = None,
) -> None:
    """Write the skeleton XML+PDF pairs and manifest.json from the seed."""
    try:
        manifest = build_dataset(seed, out, case or CASE_IDS)
    except ValueError as error:
        typer.echo(f"error: {error}", err=True)
        raise typer.Exit(2) from error
    for entry in manifest["cases"]:
        typer.echo(
            f"{entry['case_id']}: {entry['pages']} page(s) "
            f"xml {entry['xml_sha256'][:12]} pdf {entry['pdf_sha256'][:12]}"
        )
    typer.echo(f"wrote {len(manifest['cases'])} case(s) and {MANIFEST_NAME} to {out}")


@app.command()
def check(
    seed: Annotated[int, typer.Option(help="Master seed to regenerate with.")] = MASTER_SEED,
    directory: Annotated[
        Path, typer.Option("--dir", help="Committed directory to compare against.")
    ] = DEFAULT_DIR,
) -> None:
    """Regenerate into a temporary directory and compare every file byte for byte with --dir."""
    with tempfile.TemporaryDirectory() as tmp:
        fresh_dir = Path(tmp)
        build_dataset(seed, fresh_dir, CASE_IDS)
        fresh = {path.name: path for path in fresh_dir.iterdir() if path.is_file()}
        committed = (
            {path.name: path for path in directory.iterdir() if path.is_file()}
            if directory.is_dir()
            else {}
        )
        differing: list[str] = []
        for name in sorted(fresh.keys() | committed.keys()):
            if name not in committed:
                differing.append(f"{directory / name}: missing from the committed directory")
            elif name not in fresh:
                differing.append(f"{directory / name}: not produced by a regeneration")
            elif fresh[name].read_bytes() != committed[name].read_bytes():
                differing.append(f"{directory / name}: bytes differ from a regeneration")
    if differing:
        for line in differing:
            typer.echo(line, err=True)
        raise typer.Exit(1)
    typer.echo(f"ok: {directory} matches a regeneration from seed {seed}")
