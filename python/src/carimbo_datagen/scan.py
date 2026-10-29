"""Image-only twins of a DANFE for the dataset (CONTEXT D-07 to D-09).

RED stub: signatures only, so the tests fail on behavior instead of on import.
"""

from __future__ import annotations

from collections.abc import Sequence

from PIL import Image

SCAN_DPI = 200
JPEG_QUALITY = 50
BARCODE_DECODE_DPI = 200


def rasterize(pdf: bytes, dpi: int) -> list[Image.Image]:
    raise NotImplementedError


def page_sizes(pdf: bytes) -> list[tuple[float, float]]:
    raise NotImplementedError


def write_image_only_pdf(
    pages: Sequence[Image.Image], sizes: Sequence[tuple[float, float]], quality: int = JPEG_QUALITY
) -> bytes:
    raise NotImplementedError


def render_twin(
    base_pdf: bytes, rendering: str, severity: str | None, *, seed_text: str, access_key: str
) -> bytes:
    raise NotImplementedError


def is_image_only(pdf: bytes) -> bool:
    raise NotImplementedError


def barcode_readable(pdf: bytes, access_key: str, dpi: int = BARCODE_DECODE_DPI) -> bool:
    raise NotImplementedError
