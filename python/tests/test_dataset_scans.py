"""DATA-03 over the committed image-only twins of ``data/dataset`` (CONTEXT D-06 to D-10).

The twins are proven from the committed bytes, never from a regeneration: no text layer, exactly
one image object per page, the base's page count, a ``barcode_readable`` flag that equals a fresh
decode at the decode DPI, and no identifier inside the bytes (RESEARCH Pitfall 10). The case ids
come from the manifest, so a failure names the case.
"""

from __future__ import annotations

import json
import xml.etree.ElementTree as ET
from collections import Counter
from functools import cache
from pathlib import Path
from typing import Any

import pypdfium2 as pdfium
import pypdfium2.raw as pdfium_raw
import pytest

from carimbo_datagen.scan import RENDERINGS, barcode_readable, is_image_only

ROOT = Path(__file__).resolve().parents[2]
DATASET = ROOT / "data" / "dataset"
_NS = {"n": "http://www.portalfiscal.inf.br/nfe"}

_MANIFEST: dict[str, Any] = json.loads((DATASET / "manifest.json").read_text(encoding="utf-8"))
_CASES: dict[str, dict[str, Any]] = {entry["case_id"]: entry for entry in _MANIFEST["cases"]}
_CASE_IDS: list[str] = [entry["case_id"] for entry in _MANIFEST["cases"]]
_TWIN_IDS: list[str] = [c for c in _CASE_IDS if _CASES[c]["rendering"] != "text"]


def _pdf_bytes(case_id: str) -> bytes:
    return (DATASET / _CASES[case_id]["pdf"]).read_bytes()


@cache
def _access_key(case_id: str) -> str:
    """The access key, read from the case's XML ``infNFe`` Id without its ``NFe`` prefix."""
    root = ET.parse(DATASET / _CASES[case_id]["xml"]).getroot()
    identifier = root.find(".//n:infNFe", _NS)
    assert identifier is not None, case_id
    return identifier.attrib["Id"].removeprefix("NFe")


def _image_objects_per_page(pdf: bytes) -> list[int]:
    document = pdfium.PdfDocument(pdf)
    try:
        counts: list[int] = []
        for page in document:
            # PdfObject sets ``type`` dynamically, so pyright cannot see it.
            kinds = [obj.type for obj in page.get_objects()]  # pyright: ignore[reportAttributeAccessIssue]
            counts.append(sum(1 for kind in kinds if kind == pdfium_raw.FPDF_PAGEOBJ_IMAGE))
        return counts
    finally:
        document.close()


# --- the shape of the twin set ---------------------------------------------------------------


def test_the_dataset_has_154_cases_and_64_of_them_are_image_only() -> None:
    assert len(_CASE_IDS) == 154
    assert len(_TWIN_IDS) == 64
    assert round(100 * len(_TWIN_IDS) / len(_CASE_IDS), 1) == 41.6


def test_each_of_the_eight_renderings_has_eight_twins() -> None:
    slices = Counter((_CASES[c]["rendering"], _CASES[c]["severity"]) for c in _TWIN_IDS)
    assert dict(slices) == {pair: 8 for pair in RENDERINGS}


def test_twins_come_eight_per_curve_base_in_rendering_order() -> None:
    bases = sorted({_CASES[c]["base_case"] for c in _TWIN_IDS})
    assert bases == [
        "nfe-0001",
        "nfe-0003",
        "nfe-0019",
        "nfe-0020",
        "nfe-0048",
        "nfe-0049",
        "nfe-0064",
        "nfe-0065",
    ]
    for index, base in enumerate(bases):
        group = _TWIN_IDS[index * 8 : (index + 1) * 8]
        assert [_CASES[c]["base_case"] for c in group] == [base] * 8
        assert [(_CASES[c]["rendering"], _CASES[c]["severity"]) for c in group] == list(RENDERINGS)


def test_curve_bases_are_single_page() -> None:
    assert {_CASES[_CASES[c]["base_case"]]["pages"] for c in _TWIN_IDS} == {1}


# --- image-only (DATA-03) --------------------------------------------------------------------


@pytest.mark.parametrize("case_id", _TWIN_IDS)
def test_twin_is_image_only_with_one_image_per_page_and_the_base_page_count(case_id: str) -> None:
    entry = _CASES[case_id]
    pdf = _pdf_bytes(case_id)
    assert is_image_only(pdf), case_id
    counts = _image_objects_per_page(pdf)
    assert counts == [1] * len(counts), case_id
    assert len(counts) == entry["pages"] == _CASES[entry["base_case"]]["pages"], case_id


@pytest.mark.parametrize("case_id", [c for c in _CASE_IDS if c not in _TWIN_IDS])
def test_text_cases_keep_their_text_layer(case_id: str) -> None:
    assert not is_image_only(_pdf_bytes(case_id)), case_id


# --- the barcode flag ------------------------------------------------------------------------


@pytest.mark.parametrize("case_id", _CASE_IDS)
def test_barcode_flag_equals_a_decode_of_the_committed_bytes(case_id: str) -> None:
    entry = _CASES[case_id]
    readable = barcode_readable(_pdf_bytes(case_id), _access_key(case_id))
    assert entry["barcode_readable"] is readable, case_id
    assert ("barcode_unreadable" in entry["tags"]) is (not readable), case_id
    # A clean case that fails to decode is a generator bug, never a reason to change the flag.
    if entry["rendering"] == "barcode_damaged":
        assert readable is False, case_id
    elif entry["rendering"] in ("text", "image_clean"):
        assert readable is True, case_id


# --- no identifier inside the bytes (RESEARCH Pitfall 10) -------------------------------------


@pytest.mark.parametrize("case_id", _TWIN_IDS)
def test_twin_bytes_leak_no_identifier_and_carry_the_invariant_metadata(case_id: str) -> None:
    entry = _CASES[case_id]
    data = _pdf_bytes(case_id)
    for needle in (case_id, entry["base_case"], _access_key(entry["base_case"])):
        assert needle.encode("utf-8") not in data, (case_id, needle)
    assert b"/DCTDecode" in data, case_id
    assert b"/ASCII85Decode" not in data, case_id
    assert b"D:19800101000000" in data, case_id
