"""Deterministic DANFE PDF rendering with BrazilFiscalReport.

The renderer uses core PDF fonts, so no system font lookup can differ between machines. The
creation date is the only wall-clock value in the file; pinning it makes the bytes reproducible.
"""

from __future__ import annotations

from datetime import UTC, datetime
from pathlib import Path

from brazilfiscalreport.danfe import Danfe

FIXED_CREATION_DATE = datetime(2026, 1, 1, tzinfo=UTC)


def render_danfe(xml: bytes, out_path: Path) -> int:
    """Render ``xml`` (an NF-e document) to ``out_path`` and return the page count."""
    danfe = Danfe(xml=xml.decode("utf-8"))
    danfe.set_creation_date(FIXED_CREATION_DATE)
    danfe.output(str(out_path))
    return danfe.pages_count
