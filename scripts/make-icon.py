"""Renders the Rift logo into src/Rift.App/Assets/rift.ico and docs/logo.png.

Logo: two slanted bars (accent blue + light) on a rounded graphite square —
same mark as in the title bar. Needs Pillow: pip install pillow
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
BG = (24, 24, 27, 255)        # Rift.Panel (dark)
ACCENT = (76, 141, 255, 255)  # Rift.Accent (dark)
LIGHT = (237, 237, 239, 255)  # Rift.Text (dark)
SS = 4                        # supersampling factor


def bar(size, x, w, h, color):
    """Rounded vertical bar, skewed like SkewTransform(AngleX=-12)."""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    top = (size - h) / 2
    d.rounded_rectangle((x, top, x + w, top + h), radius=w * 0.33, fill=color)
    k = 0.2126  # tan(12°)
    # Shear around the vertical center so the bar stays centered.
    return layer.transform(layer.size, Image.AFFINE, (1, k, -k * size / 2, 0, 1, 0), resample=Image.BICUBIC)


def render(px):
    size = px * SS
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(img).rounded_rectangle((0, 0, size - 1, size - 1), radius=size * 0.22, fill=BG)

    w = size * 0.15
    h = size * 0.52
    gap = size * 0.07
    left = (size - (2 * w + gap)) / 2
    img.alpha_composite(bar(size, left, w, h, ACCENT))
    img.alpha_composite(bar(size, left + w + gap, w, h, LIGHT))
    return img.resize((px, px), Image.LANCZOS)


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    images = [render(s) for s in sizes]
    ico = ROOT / "src" / "Rift.App" / "Assets" / "rift.ico"
    ico.parent.mkdir(parents=True, exist_ok=True)
    images[-1].save(ico, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])

    docs = ROOT / "docs"
    docs.mkdir(exist_ok=True)
    render(128).save(docs / "logo.png")
    print(f"wrote {ico.relative_to(ROOT)} and docs/logo.png")


if __name__ == "__main__":
    main()
