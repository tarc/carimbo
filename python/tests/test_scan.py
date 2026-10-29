"""DATA-03 evidence: image-only twins are deterministic, leak nothing and carry a barcode flag."""

from __future__ import annotations

import hashlib
import os
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
        # PdfObject sets ``type`` dynamically, so pyright cannot see it.
        return [
            [obj.type for obj in page.get_objects()]  # pyright: ignore[reportAttributeAccessIssue]
            for page in pdf
        ]
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


def test_twin_bytes_do_not_depend_on_source_date_epoch(monkeypatch: pytest.MonkeyPatch) -> None:
    def render() -> bytes:
        return scan.render_twin(
            _base("case-001"),
            "rotation",
            "mild",
            seed_text=_SEED_TEXT,
            access_key=_key("case-001"),
        )

    monkeypatch.delenv("SOURCE_DATE_EPOCH", raising=False)
    unset = render()
    monkeypatch.setenv("SOURCE_DATE_EPOCH", "1700000000")
    other = render()
    monkeypatch.setenv("SOURCE_DATE_EPOCH", "315532800")
    devenv = render()
    assert unset == other == devenv
    assert b"D:19800101000000" in unset


def test_writing_a_pdf_restores_source_date_epoch(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("SOURCE_DATE_EPOCH", raising=False)
    _white_pdf()
    assert "SOURCE_DATE_EPOCH" not in os.environ

    monkeypatch.setenv("SOURCE_DATE_EPOCH", "1700000000")
    _white_pdf()
    assert os.environ["SOURCE_DATE_EPOCH"] == "1700000000"

    page = Image.new("L", (1654, 2339), 255)
    with pytest.raises(ValueError):  # two pages, one size: zip(strict=True) raises in the scope
        scan.write_image_only_pdf([page, page], [(595.2756, 841.8898)])
    assert os.environ["SOURCE_DATE_EPOCH"] == "1700000000"

    monkeypatch.delenv("SOURCE_DATE_EPOCH")
    with pytest.raises(ValueError):
        scan.write_image_only_pdf([page, page], [(595.2756, 841.8898)])
    assert "SOURCE_DATE_EPOCH" not in os.environ


def test_unknown_rendering_or_severity_raises_value_error() -> None:
    base, key = _base("case-001"), _key("case-001")
    with pytest.raises(ValueError):
        scan.render_twin(base, "sepia", "mild", seed_text=_SEED_TEXT, access_key=key)
    with pytest.raises(ValueError):
        scan.render_twin(base, "rotation", "extreme", seed_text=_SEED_TEXT, access_key=key)


_EXPECTED_RENDERINGS = (
    ("image_clean", None),
    ("rotation", "mild"),
    ("rotation", "severe"),
    ("blur", "mild"),
    ("blur", "severe"),
    ("low_dpi", "mild"),
    ("low_dpi", "severe"),
    ("barcode_damaged", None),
)
_CASES = ("case-001", "case-003")


def _twin(case_id: str, rendering: str, severity: str | None) -> bytes:
    return scan.render_twin(
        _base(case_id),
        rendering,
        severity,
        seed_text=f"t:{case_id}:{rendering}:{severity}",
        access_key=_key(case_id),
    )


def _image_pixel_widths(pdf_bytes: bytes) -> list[int]:
    pdf = pdfium.PdfDocument(pdf_bytes)
    try:
        widths: list[int] = []
        for page in pdf:
            for obj in page.get_objects():
                if obj.type == pdfium_raw.FPDF_PAGEOBJ_IMAGE:  # pyright: ignore[reportAttributeAccessIssue]
                    widths.append(obj.get_px_size()[0])  # pyright: ignore[reportAttributeAccessIssue]
        return widths
    finally:
        pdf.close()


def test_renderings_catalogue_is_the_numbering_order() -> None:
    assert scan.RENDERINGS == _EXPECTED_RENDERINGS


@pytest.mark.parametrize("case_id", _CASES)
@pytest.mark.parametrize(("rendering", "severity"), _EXPECTED_RENDERINGS)
def test_every_rendering_is_image_only_with_the_base_pages_and_deterministic(
    case_id: str, rendering: str, severity: str | None
) -> None:
    base = _base(case_id)
    twin = _twin(case_id, rendering, severity)
    kinds = _object_kinds(twin)
    assert len(kinds) == len(_object_kinds(base))
    assert all(page.count(pdfium_raw.FPDF_PAGEOBJ_IMAGE) == 1 for page in kinds)
    assert all(pdfium_raw.FPDF_PAGEOBJ_TEXT not in page for page in kinds)
    assert scan.is_image_only(twin)
    assert scan.page_sizes(twin) == pytest.approx(scan.page_sizes(base), abs=0.01)
    assert b"/DCTDecode" in twin
    assert b"/ASCII85Decode" not in twin
    assert _key(case_id).encode() not in twin
    assert case_id.encode() not in twin
    assert twin == _twin(case_id, rendering, severity)


@pytest.mark.parametrize("case_id", _CASES)
def test_barcode_flag_is_true_for_clean_scans_and_false_for_damaged_ones(case_id: str) -> None:
    key = _key(case_id)
    assert scan.barcode_readable(_base(case_id), key)
    assert scan.barcode_readable(_twin(case_id, "image_clean", None), key)
    assert not scan.barcode_readable(_twin(case_id, "barcode_damaged", None), key)


@pytest.mark.parametrize("case_id", _CASES)
@pytest.mark.parametrize(("severity", "dpi"), [("mild", 110), ("severe", 72)])
def test_low_dpi_twins_embed_fewer_pixels_at_the_full_page_size(
    case_id: str, severity: str, dpi: int
) -> None:
    twin = _twin(case_id, "low_dpi", severity)
    for width_px, (width_pt, _) in zip(
        _image_pixel_widths(twin), scan.page_sizes(twin), strict=True
    ):
        assert abs(width_px - width_pt / 72 * dpi) <= 1


def test_barcode_damaged_on_a_page_without_a_barcode_raises_value_error() -> None:
    with pytest.raises(ValueError, match="Code 128"):
        scan.render_twin(
            _white_pdf(),
            "barcode_damaged",
            None,
            seed_text=_SEED_TEXT,
            access_key=_key("case-001"),
        )


def test_scan_parameters_are_json_serializable_and_complete() -> None:
    import json

    parameters = scan.scan_parameters()
    assert parameters["scan_dpi"] == 220
    assert parameters["jpeg_quality"] == 50
    assert parameters["barcode_decode_dpi"] == 220
    assert parameters["degradation"] == scan.DEGRADATION
    assert parameters["barcode_damage"] == scan.BARCODE_DAMAGE
    assert json.loads(json.dumps(parameters)) == parameters
