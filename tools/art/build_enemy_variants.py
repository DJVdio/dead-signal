#!/usr/bin/env python3
"""Build the eight zombie and eight raider animation model sets.

The source sheets are project-original authored survivors.  Reusing their complete
pose sheets keeps every frame on the exact 128x147 grid; the generated enemy
variants only change visual identity and never touch combat data.
"""

from __future__ import annotations

from pathlib import Path
import random

from PIL import Image, ImageEnhance


ROOT = Path(__file__).resolve().parents[2]
ANIM = ROOT / "godot/assets/world/animations"
ATTACK = ROOT / "godot/assets/world/attacks"

# Four men and four women, alternating.  The last woman derives from the same
# complete pose source as Nightingale but receives a distinct build/palette pass.
MODELS = (
    ("sam", "m"),
    ("christine", "f"),
    ("notty", "m"),
    ("rat", "f"),
    ("doug", "m"),
    ("nightingale", "f"),
    ("pete", "m"),
    ("nightingale", "f"),
)

CELL_W = 128
CELL_H = 147


def alpha_blend_tint(image: Image.Image, tint: tuple[int, int, int], amount: float) -> Image.Image:
    rgba = image.convert("RGBA")
    color = Image.new("RGBA", rgba.size, (*tint, 255))
    mixed = Image.blend(rgba, color, amount)
    mixed.putalpha(rgba.getchannel("A"))
    return mixed


def reshape_cells(image: Image.Image, width_factor: float, height_factor: float) -> Image.Image:
    """Resize each cell around its bottom-center anchor without changing the grid."""
    cols = image.width // CELL_W
    rows = image.height // CELL_H
    out = Image.new("RGBA", image.size)
    for row in range(rows):
        for col in range(cols):
            box = (col * CELL_W, row * CELL_H, (col + 1) * CELL_W, (row + 1) * CELL_H)
            cell = image.crop(box)
            resized = cell.resize(
                (max(1, round(CELL_W * width_factor)), max(1, round(CELL_H * height_factor))),
                Image.Resampling.NEAREST,
            )
            x = col * CELL_W + (CELL_W - resized.width) // 2
            y = (row + 1) * CELL_H - resized.height
            out.alpha_composite(resized, (x, y))
    return out


def raider_variant(source: Image.Image, index: int) -> Image.Image:
    palette = (
        (86, 72, 66), (66, 76, 72), (76, 68, 58), (72, 62, 70),
        (76, 64, 52), (72, 70, 58), (60, 70, 78), (54, 68, 72),
    )
    out = ImageEnhance.Color(source.convert("RGBA")).enhance(0.82)
    out = alpha_blend_tint(out, palette[index], 0.08 if index < 7 else 0.18)
    if index == 7:
        out = reshape_cells(out, 0.88, 0.96)
    return out


def zombie_variant(source: Image.Image, index: int) -> Image.Image:
    corpse_tints = (
        (72, 83, 60), (77, 72, 67), (58, 77, 66), (72, 66, 78),
        (80, 73, 54), (66, 76, 72), (61, 70, 78), (56, 75, 73),
    )
    out = ImageEnhance.Color(source.convert("RGBA")).enhance(0.34)
    out = ImageEnhance.Brightness(out).enhance(0.82)
    out = alpha_blend_tint(out, corpse_tints[index], 0.26)
    if index == 7:
        out = reshape_cells(out, 0.88, 0.96)

    # Deterministic grime/blood flecks, clipped to the existing opaque silhouette.
    pixels = out.load()
    rng = random.Random(0xD34D510 + index)
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = pixels[x, y]
            if a > 160 and rng.random() < 0.0045:
                pixels[x, y] = (max(38, r // 2), min(g, 38), min(b, 34), a)
    return out


def save(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, optimize=True)


def main() -> None:
    for index, (source_name, _gender) in enumerate(MODELS):
        number = index + 1
        anim_source = Image.open(ANIM / f"{source_name}.png")
        attack_source = Image.open(ATTACK / f"{source_name}.png")

        save(zombie_variant(anim_source, index), ANIM / f"zombie-{number:02}.png")
        save(raider_variant(anim_source, index), ANIM / f"raider-{number:02}.png")
        save(raider_variant(attack_source, index), ATTACK / f"raider-{number:02}.png")


if __name__ == "__main__":
    main()
