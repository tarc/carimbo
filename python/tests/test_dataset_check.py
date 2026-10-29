"""DATA-06: the byte-identical check reports every missing, extra and changed file by name.

The tests run on small purpose-built directories and never regenerate the dataset, so the default
pytest selection stays fast however large data/dataset grows.
"""

from __future__ import annotations

from pathlib import Path

import pytest
from typer.testing import CliRunner

from carimbo_datagen import dataset
from carimbo_datagen.cli import _compare_directories, app

MISSING = "missing from the committed directory"
EXTRA = "not produced by a regeneration"
CHANGED = "bytes differ from a regeneration"


def _write(directory: Path, files: dict[str, bytes]) -> Path:
    directory.mkdir(parents=True, exist_ok=True)
    for name, data in files.items():
        (directory / name).write_bytes(data)
    return directory


def test_identical_directories_report_nothing(tmp_path: Path) -> None:
    files = {"a.xml": b"<a/>", "a.pdf": b"%PDF", "manifest.json": b"{}\n"}
    fresh = _write(tmp_path / "fresh", files)
    committed = _write(tmp_path / "committed", files)
    assert _compare_directories(fresh, committed) == []


def test_a_file_only_in_the_fresh_directory_is_missing_from_the_committed_one(
    tmp_path: Path,
) -> None:
    fresh = _write(tmp_path / "fresh", {"a.xml": b"1", "b.xml": b"2"})
    committed = _write(tmp_path / "committed", {"a.xml": b"1"})
    assert _compare_directories(fresh, committed) == [f"{committed / 'b.xml'}: {MISSING}"]


def test_a_file_only_in_the_committed_directory_is_not_produced(tmp_path: Path) -> None:
    fresh = _write(tmp_path / "fresh", {"a.xml": b"1"})
    committed = _write(tmp_path / "committed", {"a.xml": b"1", "stale.pdf": b"old"})
    assert _compare_directories(fresh, committed) == [f"{committed / 'stale.pdf'}: {EXTRA}"]


def test_one_changed_byte_is_reported(tmp_path: Path) -> None:
    fresh = _write(tmp_path / "fresh", {"a.pdf": b"%PDF-1.4 abc"})
    committed = _write(tmp_path / "committed", {"a.pdf": b"%PDF-1.4 abd"})
    assert _compare_directories(fresh, committed) == [f"{committed / 'a.pdf'}: {CHANGED}"]


def test_a_missing_committed_directory_reports_every_fresh_file(tmp_path: Path) -> None:
    fresh = _write(tmp_path / "fresh", {"a.xml": b"1", "b.pdf": b"2"})
    committed = tmp_path / "absent"
    assert _compare_directories(fresh, committed) == [
        f"{committed / 'a.xml'}: {MISSING}",
        f"{committed / 'b.pdf'}: {MISSING}",
    ]


def test_mixed_differences_come_in_sorted_name_order(tmp_path: Path) -> None:
    fresh = _write(tmp_path / "fresh", {"a.xml": b"same", "b.xml": b"new", "d.pdf": b"x"})
    committed = _write(tmp_path / "committed", {"a.xml": b"same", "c.xml": b"old", "d.pdf": b"y"})
    assert _compare_directories(fresh, committed) == [
        f"{committed / 'b.xml'}: {MISSING}",
        f"{committed / 'c.xml'}: {EXTRA}",
        f"{committed / 'd.pdf'}: {CHANGED}",
    ]


def test_dataset_check_exits_1_and_writes_the_differences_to_stderr(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    line = f"{tmp_path / 'nfe-0001.xml'}: {CHANGED}"
    monkeypatch.setattr(dataset, "check_dataset", lambda seed, directory: [line])
    result = CliRunner().invoke(app, ["dataset-check", "--dir", str(tmp_path)])
    assert result.exit_code == 1
    assert line in result.stderr
    assert "ok:" not in result.stdout


def test_dataset_check_exits_0_with_the_ok_line_when_nothing_differs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(dataset, "check_dataset", lambda seed, directory: [])
    result = CliRunner().invoke(app, ["dataset-check", "--dir", str(tmp_path)])
    assert result.exit_code == 0
    assert f"ok: {tmp_path} matches a regeneration from seed 20261022" in result.stdout
