"""Build the versioned dataset in ``data/dataset`` and check it against a regeneration.

The quota table (``quota.py``) plans the cases, ``dataset_spec`` draws each base case, the XML and
text DANFE writers render it, and ``scan.render_twin`` makes its image-only twins. A twin's XML is a
byte copy of its base's: the ground truth is the same document, and the runner pairs each PDF with
a same-stem XML (user-approved amendment of CONTEXT D-06/D-18, 2026-10-22).

Each manifest ``expected`` block comes from ``cli._expected`` of the spec the case was built from,
never from the generated XML, so the ground-truth gates judge the XML against an independent
answer. The ``dataset-001`` label is pre-publication until plan 04-07 commits the final bytes
(CONTEXT D-17, qualified 2026-10-29).
"""

from __future__ import annotations

import json
import tempfile
from importlib.metadata import version
from pathlib import Path
from typing import Any

from carimbo_datagen.cli import (
    _PYTHON_MAJOR_MINOR,
    MANIFEST_NAME,
    _compare_directories,
    _expected,
    _render_pdf_atomic,
    _sha256,
    _write_atomic,
)
from carimbo_datagen.dataset_spec import build_dataset_spec
from carimbo_datagen.nfe_xml import build_nfe_xml
from carimbo_datagen.quota import (
    BasePlan,
    assign_splits,
    plan_cases,
    quota_sha256,
    select_ci_subset,
)
from carimbo_datagen.scan import barcode_readable, render_twin, scan_parameters
from carimbo_datagen.spec import CaseSpec

DATASET = "dataset"
DATASET_VERSION = "dataset-001"
AS_OF_DATE = "2026-10-01"
DEFAULT_DIR = Path("data/dataset")
_GENERATOR_PACKAGES = (
    "carimbo",
    "brazilfiscalreport",
    "fpdf2",
    "python-barcode",
    "faker",
    "lxml",
    "pillow",
    "numpy",
    "reportlab",
    "pypdfium2",
    "zxing-cpp",
)
_ST_CODES = frozenset({"10", "30", "51", "70"})


def _length_tag(item_count: int) -> str:
    if item_count <= 8:
        return "short"
    if item_count <= 25:
        return "medium"
    return "many_items"


def _spec_tags(plan: BasePlan, spec: CaseSpec, pages: int) -> set[str]:
    codes = {item.tax_code for item in spec.items}
    tags = set(plan.traits)
    tags.add("regime_normal" if spec.regime == "normal" else "simples_nacional")
    if spec.crt == 4:
        tags.add("mei")
    tags.add(_length_tag(len(spec.items)))
    tags.add("single_page" if pages == 1 else "multi_page")
    tags.add("alphanumeric_cnpj" if not spec.issuer.tax_id.isdigit() else "numeric_cnpj")
    if spec.recipient_tax_id_kind == "cpf":
        tags.add("cpf_recipient")
    elif not spec.recipient.tax_id.isdigit():
        tags.add("alphanumeric_recipient")
    if codes & _ST_CODES:
        tags.add("st_deferral")
    if len(codes) >= 2:
        tags.add("mixed_tax_codes")
    if spec.ipi_total:
        tags.add("ipi")
    if spec.freight or spec.discount:
        tags.add("freight_discount")
    if spec.installments:
        tags.add("installments")
    return tags


def _check_pages(plan: BasePlan, pages: int) -> None:
    if plan.pages == "single" and pages != 1:
        raise ValueError(f"{plan.case_id}: expected 1 page, the renderer produced {pages}")
    if plan.pages == "multi" and pages < 2:
        raise ValueError(
            f"{plan.case_id}: expected at least 2 pages, the renderer produced {pages}"
        )


def build_dataset_files(seed: int, out_dir: Path) -> dict[str, Any]:
    """Write every case's XML and PDF and ``manifest.json`` to ``out_dir``; return the manifest."""
    out_dir.mkdir(parents=True, exist_ok=True)
    plan = plan_cases()
    splits = assign_splits(plan, seed)
    entries: dict[str, dict[str, Any]] = {}
    base_bytes: dict[str, tuple[bytes, bytes]] = {}
    specs: dict[str, CaseSpec] = {}

    for base in plan.bases:
        spec = build_dataset_spec(seed, base)
        specs[base.case_id] = spec
        xml = build_nfe_xml(spec)
        xml_path, pdf_path = out_dir / f"{base.case_id}.xml", out_dir / f"{base.case_id}.pdf"
        _write_atomic(xml_path, xml)
        pages = _render_pdf_atomic(xml, pdf_path)
        _check_pages(base, pages)
        if base.curve_split is not None:
            base_bytes[base.case_id] = (xml, pdf_path.read_bytes())
        tags = _spec_tags(base, spec, pages) | {"text_layer"}
        entries[base.case_id] = _entry(
            base.case_id,
            out_dir,
            spec,
            pages,
            tags,
            splits[base.case_id],
            base.case_id,
            "text",
            None,
        )

    for twin in plan.twins:
        spec = specs[twin.base_case]
        xml, base_pdf = base_bytes[twin.base_case]
        _write_atomic(out_dir / f"{twin.case_id}.xml", xml)
        scan = render_twin(
            base_pdf,
            twin.rendering,
            twin.severity,
            seed_text=f"{seed}:{twin.case_id}:scan",
            access_key=spec.access_key,
        )
        _write_atomic(out_dir / f"{twin.case_id}.pdf", scan)
        base_entry = entries[twin.base_case]
        tags = {t for t in base_entry["tags"] if t != "text_layer"} | {"image_only", twin.rendering}
        if twin.severity is not None:
            tags.add(twin.severity)
        entries[twin.case_id] = _entry(
            twin.case_id,
            out_dir,
            spec,
            base_entry["pages"],
            tags,
            splits[twin.case_id],
            twin.base_case,
            twin.rendering,
            twin.severity,
        )

    cases = [entries[case_id] for case_id in sorted(entries)]
    manifest: dict[str, Any] = {
        "dataset": DATASET,
        "dataset_version": DATASET_VERSION,
        "as_of_date": AS_OF_DATE,
        "master_seed": seed,
        "python": _PYTHON_MAJOR_MINOR,
        "generator": {
            **{name: version(name) for name in _GENERATOR_PACKAGES},
            "scan": scan_parameters(),
        },
        "quota_sha256": quota_sha256(),
        "ci_subset": select_ci_subset(cases, seed),
        "cases": cases,
    }
    text = json.dumps(manifest, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    _write_atomic(out_dir / MANIFEST_NAME, text.encode("utf-8"))

    listed = {name for entry in cases for name in (entry["xml"], entry["pdf"])}
    for path in sorted(out_dir.iterdir()):
        if path.suffix in (".xml", ".pdf") and path.name not in listed:
            path.unlink()
    return manifest


def _entry(
    case_id: str,
    out_dir: Path,
    spec: CaseSpec,
    pages: int,
    tags: set[str],
    split: str,
    base_case: str,
    rendering: str,
    severity: str | None,
) -> dict[str, Any]:
    pdf_path = out_dir / f"{case_id}.pdf"
    readable = barcode_readable(pdf_path.read_bytes(), spec.access_key)
    if not readable:
        tags = tags | {"barcode_unreadable"}
    return {
        "case_id": case_id,
        "xml": f"{case_id}.xml",
        "xml_sha256": _sha256(out_dir / f"{case_id}.xml"),
        "pdf": f"{case_id}.pdf",
        "pdf_sha256": _sha256(pdf_path),
        "pages": pages,
        "tags": sorted(tags),
        "expected": _expected(spec),
        "split": split,
        "base_case": base_case,
        "rendering": rendering,
        "severity": severity,
        "barcode_readable": readable,
    }


def check_dataset(seed: int, directory: Path) -> list[str]:
    """Regenerate into a temporary directory; return one line per differing file."""
    with tempfile.TemporaryDirectory() as tmp:
        fresh_dir = Path(tmp)
        build_dataset_files(seed, fresh_dir)
        return _compare_directories(fresh_dir, directory)
