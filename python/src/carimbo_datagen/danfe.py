"""RED stub: signature only, behaviour arrives in the GREEN commit."""

from __future__ import annotations

from datetime import UTC, datetime
from pathlib import Path

FIXED_CREATION_DATE = datetime(2026, 1, 1, tzinfo=UTC)


def render_danfe(xml: bytes, out_path: Path) -> int:
    raise NotImplementedError
