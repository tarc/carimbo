"""Image-only twins of a DANFE for the dataset (CONTEXT D-07 to D-09).

A twin is built by rasterizing the text DANFE with pdfium, degrading the raster, encoding grayscale
JPEG and wrapping the pages with reportlab. No text layer survives, so the twin tests reading from
pixels. Determinism holds on the pinned Linux x64 stack (CONTEXT D-13): every random draw comes from
a generator seeded by the twin's own seed text, and reportlab runs with ``invariant=1``.
"""

from __future__ import annotations

import hashlib
import io
from collections.abc import Sequence

import numpy
import pypdfium2 as pdfium
import pypdfium2.raw as pdfium_raw
import zxingcpp
from PIL import Image
from reportlab import rl_config
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

SCAN_DPI = 200
JPEG_QUALITY = 50
BARCODE_DECODE_DPI = 200

# Degradation parameters per rendering and severity (CONTEXT D-08).
DEGRADATION: dict[str, dict[str, float]] = {
    "rotation": {"mild": 1.0, "severe": 3.5},
}


def _rng(seed_text: str) -> numpy.random.Generator:
    """A generator that depends only on ``seed_text``, never on module state."""
    digest = hashlib.sha256(seed_text.encode("utf-8")).digest()
    return numpy.random.default_rng(int.from_bytes(digest[:8], "big"))


def rasterize(pdf: bytes, dpi: int) -> list[Image.Image]:
    """Render every page with pdfium at ``dpi`` and convert to grayscale."""
    document = pdfium.PdfDocument(pdf)
    try:
        # pypdfium2 types scale as int but documents a float (DPI / 72).
        return [
            page.render(scale=dpi / 72).to_pil().convert("L")  # pyright: ignore[reportArgumentType]
            for page in document
        ]
    finally:
        document.close()


def page_sizes(pdf: bytes) -> list[tuple[float, float]]:
    """Each page's (width, height) in points."""
    document = pdfium.PdfDocument(pdf)
    try:
        return [page.get_size() for page in document]
    finally:
        document.close()


def write_image_only_pdf(
    pages: Sequence[Image.Image],
    sizes: Sequence[tuple[float, float]],
    quality: int = JPEG_QUALITY,
) -> bytes:
    """Wrap ``pages`` (mode ``L``) as full-page grayscale JPEG images in one reportlab PDF."""
    rl_config.useA85 = 0  # the default wraps the JPEG in ASCII85 (+25%)
    buffer = io.BytesIO()
    pdf = canvas.Canvas(buffer, pagesize=sizes[0], invariant=1, pageCompression=0)
    for page, (width, height) in zip(pages, sizes, strict=True):
        pdf.setPageSize((width, height))
        jpeg = io.BytesIO()
        page.save(jpeg, "JPEG", quality=quality, optimize=True)
        jpeg.seek(0)
        pdf.drawImage(ImageReader(jpeg), 0, 0, width=width, height=height)
        pdf.showPage()
    pdf.save()
    return buffer.getvalue()


def _rotate(
    pages: list[Image.Image], degrees: float, rng: numpy.random.Generator
) -> list[Image.Image]:
    sign = 1.0 if rng.integers(0, 2) == 0 else -1.0
    return [
        page.rotate(sign * degrees, resample=Image.Resampling.BICUBIC, expand=False, fillcolor=255)
        for page in pages
    ]


def render_twin(
    base_pdf: bytes,
    rendering: str,
    severity: str | None,
    *,
    seed_text: str,
    access_key: str,
) -> bytes:
    """Return the image-only twin of ``base_pdf`` for one rendering and severity."""
    del access_key  # used by the barcode rendering added with the remaining renderings
    rng = _rng(seed_text)
    sizes = page_sizes(base_pdf)
    if rendering == "rotation":
        levels = DEGRADATION["rotation"]
        if severity not in levels:
            raise ValueError(f"unknown rotation severity: {severity!r}")
        pages = _rotate(rasterize(base_pdf, SCAN_DPI), levels[severity], rng)
    else:
        raise ValueError(f"unknown rendering: {rendering!r}")
    return write_image_only_pdf(pages, sizes)


def is_image_only(pdf: bytes) -> bool:
    """True when no page has a pdfium text object or any extractable text character."""
    document = pdfium.PdfDocument(pdf)
    try:
        for page in document:
            # PdfObject sets ``type`` dynamically, so pyright cannot see it.
            kinds = [obj.type for obj in page.get_objects()]  # pyright: ignore[reportAttributeAccessIssue]
            if pdfium_raw.FPDF_PAGEOBJ_TEXT in kinds:
                return False
            if page.get_textpage().get_text_range():
                return False
        return True
    finally:
        document.close()


def barcode_readable(pdf: bytes, access_key: str, dpi: int = BARCODE_DECODE_DPI) -> bool:
    """True when zxing-cpp reads a Code 128 equal to ``access_key`` on page 1 at ``dpi``."""
    document = pdfium.PdfDocument(pdf)
    try:
        # pypdfium2 types scale as int but documents a float (DPI / 72).
        image = document[0].render(scale=dpi / 72).to_pil()  # pyright: ignore[reportArgumentType]
    finally:
        document.close()
    return any(
        result.text == access_key and result.format == zxingcpp.BarcodeFormat.Code128
        for result in zxingcpp.read_barcodes(image)
    )
