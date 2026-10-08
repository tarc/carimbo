"""DATA-01 evidence: byte-reproducible NF-e XML and DANFE PDF, barcode decodes to the key."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import unicodedata
from functools import cache
from pathlib import Path

import pypdfium2 as pdfium
import pytest
import zxingcpp
from typer.testing import CliRunner

from carimbo_datagen.cli import app as cli_app
from carimbo_datagen.cli import build_dataset
from carimbo_datagen.danfe import render_danfe
from carimbo_datagen.nfe_xml import build_nfe_xml
from carimbo_datagen.spec import CASE_IDS, MASTER_SEED, CaseSpec, build_case_spec
from carimbo_evals.grader import load_ground_truth

_SRC = Path(__file__).resolve().parents[1] / "src"
_REPO_ROOT = Path(__file__).resolve().parents[2]
_SUBPROCESS_RENDER = """
import hashlib, sys
from pathlib import Path
from carimbo_datagen.cli import app as cli_app
from carimbo_datagen.cli import build_dataset
from carimbo_datagen.danfe import render_danfe
from carimbo_datagen.nfe_xml import build_nfe_xml
from carimbo_datagen.spec import build_case_spec

spec = build_case_spec(int(sys.argv[1]), sys.argv[2])
out = Path(sys.argv[3])
render_danfe(build_nfe_xml(spec), out)
print(hashlib.sha256(out.read_bytes()).hexdigest())
"""


@cache
def _spec(case_id: str) -> CaseSpec:
    return build_case_spec(MASTER_SEED, case_id)


def _norm(text: str) -> str:
    return " ".join(unicodedata.normalize("NFC", text).casefold().split())


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


@pytest.fixture(scope="module")
def rendered(tmp_path_factory: pytest.TempPathFactory) -> dict[str, tuple[Path, int]]:
    out_dir = tmp_path_factory.mktemp("danfe")
    result: dict[str, tuple[Path, int]] = {}
    for case_id in CASE_IDS:
        path = out_dir / f"{case_id}.pdf"
        pages = render_danfe(build_nfe_xml(_spec(case_id)), path)
        result[case_id] = (path, pages)
    return result


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_xml_is_byte_identical_across_builds(case_id: str) -> None:
    assert build_nfe_xml(_spec(case_id)) == build_nfe_xml(build_case_spec(MASTER_SEED, case_id))


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_xml_is_utf8_with_declaration_homologation_and_no_signature(case_id: str) -> None:
    xml = build_nfe_xml(_spec(case_id))
    assert xml.startswith(b"<?xml")
    assert b"UTF-8" in xml.split(b"?>", 1)[0]
    text = xml.decode("utf-8")
    assert "<tpAmb>2</tpAmb>" in text
    assert "SEM VALOR FISCAL" in text
    assert "Signature" not in text


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_xml_ground_truth_reads_back_the_spec(case_id: str, tmp_path: Path) -> None:
    spec = _spec(case_id)
    xml_path = tmp_path / f"{case_id}.xml"
    xml_path.write_bytes(build_nfe_xml(spec))
    truth = load_ground_truth(xml_path)
    assert truth.access_key == spec.access_key
    assert truth.number == spec.number
    assert truth.series == spec.series
    assert truth.issue_date == f"{spec.issue_datetime:%Y-%m-%d}"
    assert truth.issuer_cnpj == spec.issuer.cnpj
    assert truth.issuer_name == spec.issuer.name
    assert truth.recipient_cnpj == spec.recipient.cnpj
    assert truth.recipient_name == spec.recipient.name
    assert truth.total_amount == spec.total_amount
    assert f"<vNF>{spec.total_amount}</vNF>" in xml_path.read_text(encoding="utf-8")


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_infnfe_id_is_nfe_prefix_plus_access_key(case_id: str) -> None:
    spec = _spec(case_id)
    assert f'Id="NFe{spec.access_key}"' in build_nfe_xml(spec).decode("utf-8")


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_same_process_rendering_is_byte_identical(case_id: str, tmp_path: Path) -> None:
    xml = build_nfe_xml(_spec(case_id))
    first, second = tmp_path / "a.pdf", tmp_path / "b.pdf"
    render_danfe(xml, first)
    render_danfe(xml, second)
    assert first.read_bytes() == second.read_bytes()


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_fresh_process_rendering_matches_this_process(
    case_id: str, rendered: dict[str, tuple[Path, int]], tmp_path: Path
) -> None:
    out = tmp_path / "fresh.pdf"
    proc = subprocess.run(
        [sys.executable, "-c", _SUBPROCESS_RENDER, str(MASTER_SEED), case_id, str(out)],
        capture_output=True,
        text=True,
        check=True,
        cwd=_REPO_ROOT,
    )
    assert proc.stdout.strip() == _sha256(rendered[case_id][0])


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_danfe_text_layer_carries_both_party_names(
    case_id: str, rendered: dict[str, tuple[Path, int]]
) -> None:
    spec = _spec(case_id)
    pdf = pdfium.PdfDocument(str(rendered[case_id][0]))
    try:
        text = _norm(pdf[0].get_textpage().get_text_range())
    finally:
        pdf.close()
    assert _norm(spec.issuer.name) in text
    assert _norm(spec.recipient.name) in text


@pytest.mark.parametrize("case_id", CASE_IDS)
def test_danfe_code128_barcode_decodes_to_the_access_key(
    case_id: str, rendered: dict[str, tuple[Path, int]]
) -> None:
    spec = _spec(case_id)
    pdf = pdfium.PdfDocument(str(rendered[case_id][0]))
    try:
        # pypdfium2 types scale as int but documents a float (DPI / 72).
        image = pdf[0].render(scale=300 / 72).to_pil()  # pyright: ignore[reportArgumentType]
    finally:
        pdf.close()
    results = zxingcpp.read_barcodes(image)
    code128 = [r.text for r in results if r.format == zxingcpp.BarcodeFormat.Code128]
    assert spec.access_key in code128


def test_page_counts_single_page_cases_and_multipage_overflow(
    rendered: dict[str, tuple[Path, int]],
) -> None:
    assert rendered["case-001"][1] == 1
    assert rendered["case-002"][1] == 1
    assert rendered["case-003"][1] >= 2
    pdf = pdfium.PdfDocument(str(rendered["case-003"][0]))
    try:
        assert len(pdf) == rendered["case-003"][1]
    finally:
        pdf.close()


def test_dataset_builder_never_imports_nfelib() -> None:
    for module in ("nfe_xml.py", "spec.py"):
        source = (_SRC / "carimbo_datagen" / module).read_text(encoding="utf-8")
        for line in source.splitlines():
            assert not line.startswith(("import nfelib", "from nfelib")), (module, line)


_COMMITTED = _REPO_ROOT / "data" / "skeleton"
_EXPECTED_FILES = {"manifest.json"} | {f"{c}.{ext}" for c in CASE_IDS for ext in ("xml", "pdf")}


def test_committed_skeleton_matches_regeneration(tmp_path: Path) -> None:
    """D-07: a clean runner regenerates the dataset and must get the committed bytes."""
    build_dataset(MASTER_SEED, tmp_path, CASE_IDS)
    regenerated = {p.name for p in tmp_path.iterdir()}
    committed = {p.name for p in _COMMITTED.iterdir()}
    assert regenerated == committed == _EXPECTED_FILES
    differing = [
        name
        for name in sorted(regenerated)
        if (tmp_path / name).read_bytes() != (_COMMITTED / name).read_bytes()
    ]
    assert differing == []


def test_manifest_hashes_match_files() -> None:
    manifest = json.loads((_COMMITTED / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["dataset_version"] == "skeleton-001"
    assert manifest["master_seed"] == MASTER_SEED
    assert [c["case_id"] for c in manifest["cases"]] == list(CASE_IDS)
    for entry in manifest["cases"]:
        assert entry["xml_sha256"] == _sha256(_COMMITTED / entry["xml"])
        assert entry["pdf_sha256"] == _sha256(_COMMITTED / entry["pdf"])
        pdf = pdfium.PdfDocument(str(_COMMITTED / entry["pdf"]))
        try:
            assert len(pdf) == entry["pages"]
        finally:
            pdf.close()


def test_manifest_has_no_machine_specific_values() -> None:
    text = (_COMMITTED / "manifest.json").read_text(encoding="utf-8")
    for needle in (str(_REPO_ROOT), "/home/", "/nix/", "T00:00", "hostname"):
        assert needle not in text


def test_check_command_names_every_drifted_file(tmp_path: Path) -> None:
    build_dataset(MASTER_SEED, tmp_path, CASE_IDS)
    runner = CliRunner()
    assert runner.invoke(cli_app, ["check", "--dir", str(tmp_path)]).exit_code == 0

    (tmp_path / "case-001.pdf").write_bytes(b"tampered")
    (tmp_path / "case-002.xml").unlink()
    result = runner.invoke(cli_app, ["check", "--dir", str(tmp_path)])
    assert result.exit_code == 1
    assert "case-001.pdf" in result.output
    assert "case-002.xml" in result.output
    assert "case-003.pdf" not in result.output
