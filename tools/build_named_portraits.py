#!/usr/bin/env python3
"""Derive named survivor card portraits from the approved actor atlases."""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "godot/assets/world/animations"
OUTPUT = ROOT / "godot/assets/portraits/named"
NAMES = ("sam", "notty", "christine", "rat", "doug", "nightingale", "pete")
CELL_W = 128
CELL_H = 147
OUT_W = 365
OUT_H = 564


def background(name: str) -> Image.Image:
    seed = sum(ord(c) for c in name)
    image = Image.new("RGBA", (OUT_W, OUT_H), (22, 25, 23, 255))
    draw = ImageDraw.Draw(image)
    for y in range(0, OUT_H, 8):
        shade = 21 + ((y // 8 + seed) % 5)
        draw.rectangle((0, y, OUT_W, y + 7), fill=(shade, shade + 3, shade + 1, 255))
    draw.rectangle((12, 12, OUT_W - 13, OUT_H - 13), outline=(67, 66, 56, 255), width=4)
    draw.rectangle((20, 20, OUT_W - 21, OUT_H - 21), outline=(35, 38, 34, 255), width=2)
    return image


def build(name: str) -> None:
    with Image.open(SOURCE / f"{name}.png").convert("RGBA") as atlas:
        frame = atlas.crop((0, 0, CELL_W, CELL_H))
    bbox = frame.getchannel("A").getbbox()
    if bbox is None:
        raise RuntimeError(f"{name}: idle frame is empty")

    left, top, right, bottom = bbox
    figure_height = bottom - top
    # Portrait crop: head, shoulders and most of torso; keep authored identity pixels untouched.
    portrait_bottom = min(bottom, top + round(figure_height * 0.72))
    margin = max(4, round((right - left) * 0.12))
    subject = frame.crop((max(0, left - margin), max(0, top - margin), min(CELL_W, right + margin), portrait_bottom))
    scale = min((OUT_W - 56) / subject.width, (OUT_H - 56) / subject.height)
    subject = subject.resize((round(subject.width * scale), round(subject.height * scale)), Image.Resampling.NEAREST)

    final = background(name)
    x = (OUT_W - subject.width) // 2
    y = OUT_H - 26 - subject.height
    final.alpha_composite(subject, (x, y))
    OUTPUT.mkdir(parents=True, exist_ok=True)
    final.save(OUTPUT / f"{name}.png", optimize=True)
    print(f"{name}: {OUT_W}x{OUT_H} RGBA")


def main() -> None:
    for name in NAMES:
        build(name)


if __name__ == "__main__":
    main()
