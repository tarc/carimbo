"""DATA-03 evidence: image-only twins of a DANFE are deterministic, leak nothing, and carry a measured barcode flag."""

from __future__ import annotations

import hashlib
import re
import subprocess
import sys
from functools import cache
from pathlib import Path

import pypdfium2 as pdfium
import pypdfium2.raw as pdfium_raw
import pytest
from PIL import Image

from carimbo_datagen import scan

_REPO_ROOT = Path(__file__).resolve().parents[2]
_SKELETON = _REPO_ROOT / "data" / "skeleton"
_SEED_TEXT = "t:case-x:scan"
_SUBPROCESS_TWIN = """
import hashlib, sys
from pathlib import Path
from carimbo_datagen import scan

base = Path(sys.argv[1]).read_bytes()
twin = scan.render_twin(base, "rotation", "mild", seed_text=sys.argv[2], access_key=sys.argv[3])
print(hashlib.sha256(twin).hexdigest())
"""


@cache
def _base(case_id: str) -> bytes:
    return (_SKELETON / f"{case_id}.pdf").read_bytes()


@cache
def _key(case_id: str) -> str:
    xml = (_SKELETON / f"{case_id}.xml").read_text(encoding="utf-8")
    match = re.search(r'Id="NFe([0-9A-Z]{44})"', xml)
    assert match is not None
    return match.group(1)


def _object_kinds(pdf_bytes: bytes) -> list[list[int]]:
    pdf = pdfium.PdfDocument(pdf_bytes)
    try:
        return [[obj.type for obj in page.get_objects()] for page in pdf]
    finally:
        pdf.close()


def _text_chars(pdf_bytes: bytes) -> int:
    pdf = pdfium.PdfDocument(pdf_bytes)
    try:
        return sum(len(page.get_textpage().get_text_range()) for page in pdf)
    finally:
        pdf.close()


def _white_pdf() -> bytes:
    page = Image.new("L", (1654, 2339), 255)
    return scan.write_image_only_pdf([page], [(595.2756, 841.8898)])


def test_rotation_mild_twin_is_one_page_with_one_image_and_no_text() -> None:
    twin = scan.render_twin(
        _base("case-001"), "rotation", "mild", seed_text=_SEED_TEXT, access_key=_key("case-001")
    )
    kinds = _object_kinds(twin)
    assert len(kinds) == 1
    assert kinds[0].count(pdfium_raw.FPDF_PAGEOBJ_IMAGE) == 1
    assert pdfium_raw.FPDF_PAGEOBJ_TEXT not in kinds[0]
    assert _text_chars(twin) == 0
    assert scan.is_image_only(twin)
    assert scan.page_sizes(twin) == pytest.approx(scan.page_sizes(_base("case-001")), abs=0.01)


def test_same_inputs_give_identical_bytes_in_this_process() -> None:
    args = (_base("case-001"), "rotation", "mild")
    kwargs = {"seed_text": _SEED_TEXT, "access_key": _key("case-001")}
    assert scan.render_twin(*args, **kwargs) == scan.render_twin(*args, **kwargs)


def test_fresh_process_twin_matches_this_process() -> None:
    twin = scan.render_twin(
        _base("case-001"), "rotation", "mild", seed_text=_SEED_TEXT, access_key=_key("case-001")
    )
    proc = subprocess.run(
        [
            sys.executable,
            "-c",
            _SUBPROCESS_TWIN,
            str(_SKELETON / "case-001.pdf"),
            _SEED_TEXT,
            _key("case-001"),
        ],
        capture_output=True,
        text=True,
        check=True,
        cwd=_REPO_ROOT,
    )
    assert proc.stdout.strip() == hashlib.sha256(twin).hexdigest()


def test_skeleton_text_pdf_is_not_image_only() -> None:
    assert not scan.is_image_only(_base("case-001"))


def test_barcode_is_readable_on_the_text_pdf_and_not_on_a_white_page() -> None:
    assert scan.barcode_readable(_base("case-001"), _key("case-001"))
    assert not scan.barcode_readable(_white_pdf(), _key("case-001"))


def test_barcode_readable_requires_the_exact_access_key() -> None:
    other = "0" * 44
    assert not scan.barcode_readable(_base("case-001"), other)


def test_twin_bytes_leak_neither_key_nor_case_id_and_use_dct_without_a85() -> None:
    twin = scan.render_twin(
        _base("case-001"), "rotation", "mild", seed_text=_SEED_TEXT, access_key=_key("case-001")
    )
    assert _key("case-001").encode() not in twin
    assert b"case-001" not in twin
    assert b"/DCTDecode" in twin
    assert b"/ASCII85Decode" not in twin
    assert b"D:19800101000000" in twin


def test_unknown_rendering_or_severity_raises_value_error() -> None:
    base, key = _base("case-001"), _key("case-001")
    with pytest.raises(ValueError):
        scan.render_twin(base, "sepia", "mild", seed_text=_SEED_TEXT, access_key=key)
    with pytest.raises(ValueError):
        scan.render_twin(base, "rotation", "extreme", seed_text=_SEED_TEXT, access_key=key)
