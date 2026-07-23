"""Prepare a generated eight-direction turnaround for actual-size review.

The input is eight equal horizontal panels on a #ff00ff background. The script
softly removes the chroma background, normalizes every direction to a shared
height and foot baseline, and emits individual 128x147 candidate frames plus a
nearest-neighbour review sheet. It deliberately does not touch production art.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


CELL_W = 128
CELL_H = 147
TARGET_HEIGHT = 132
FOOT_Y = 142
DIRECTIONS = ("南", "西南", "西", "西北", "北", "东北", "东", "东南")


def remove_magenta(image: Image.Image) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.float32)
    rgb = rgba[:, :, :3]
    magenta = np.array([255.0, 0.0, 255.0], dtype=np.float32)
    hsv = np.asarray(image.convert("HSV"), dtype=np.float32)
    hue = hsv[:, :, 0]
    saturation = hsv[:, :, 1]
    magenta_hue = 213.0
    hue_distance = np.minimum(
        np.abs(hue - magenta_hue), 255.0 - np.abs(hue - magenta_hue)
    )
    hue_strength = np.clip((34.0 - hue_distance) / 14.0, 0.0, 1.0)
    saturation_strength = np.clip((saturation - 24.0) / 52.0, 0.0, 1.0)
    alpha = 1.0 - hue_strength * saturation_strength

    # Undo the chroma contribution on semitransparent edge pixels.
    safe_alpha = np.maximum(alpha, 1.0 / 255.0)[:, :, None]
    foreground = (rgb - (1.0 - safe_alpha) * magenta) / safe_alpha
    output = np.concatenate(
        (np.clip(foreground, 0.0, 255.0), (alpha * 255.0)[:, :, None]), axis=2
    )
    return Image.fromarray(output.astype(np.uint8), mode="RGBA")


def normalize_frame(panel: Image.Image) -> Image.Image:
    transparent = remove_magenta(panel)
    alpha = np.asarray(transparent.getchannel("A"))
    column_counts = (alpha >= 16).sum(axis=0)
    occupied = np.where(column_counts >= 4)[0]
    if len(occupied) == 0:
        raise ValueError("panel contains no foreground columns")
    runs: list[tuple[int, int]] = []
    start = previous = int(occupied[0])
    for column in occupied[1:]:
        column = int(column)
        if column - previous > 5:
            runs.append((start, previous))
            start = column
        previous = column
    runs.append((start, previous))
    main_left, main_right = max(
        runs, key=lambda run: int(column_counts[run[0] : run[1] + 1].sum())
    )
    keep_left = max(0, main_left - 4)
    keep_right = min(alpha.shape[1], main_right + 5)
    filtered = np.zeros_like(alpha)
    filtered[:, keep_left:keep_right] = alpha[:, keep_left:keep_right]
    transparent.putalpha(Image.fromarray(filtered))
    alpha = filtered
    rows, columns = np.where(alpha >= 16)
    if len(columns) == 0:
        raise ValueError("panel contains no foreground")
    crop = transparent.crop(
        (columns.min(), rows.min(), columns.max() + 1, rows.max() + 1)
    )
    scale = TARGET_HEIGHT / crop.height
    width = max(1, round(crop.width * scale))
    resized = crop.resize((width, TARGET_HEIGHT), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    x = (CELL_W - width) // 2
    y = FOOT_Y - TARGET_HEIGHT + 1
    frame.alpha_composite(resized, (x, y))
    return frame


def checkerboard(size: tuple[int, int]) -> Image.Image:
    image = Image.new("RGBA", size, (36, 40, 42, 255))
    draw = ImageDraw.Draw(image)
    tile = 12
    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle(
                    (x, y, x + tile - 1, y + tile - 1), fill=(53, 58, 60, 255)
                )
    return image


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    source = Image.open(args.source).convert("RGBA")
    args.output.mkdir(parents=True, exist_ok=True)

    frames: list[Image.Image] = []
    for index, direction in enumerate(DIRECTIONS):
        left = round(index * source.width / 8)
        right = round((index + 1) * source.width / 8)
        panel = source.crop(
            (left, 0, right, source.height)
        )
        frame = normalize_frame(panel)
        frame.save(args.output / f"{index}-{direction}.png")
        frames.append(frame)

    scale = 4
    review = Image.new(
        "RGBA", (CELL_W * scale * 8, CELL_H * scale + 28), (16, 19, 20, 255)
    )
    draw = ImageDraw.Draw(review)
    for index, (direction, frame) in enumerate(zip(DIRECTIONS, frames)):
        enlarged = frame.resize(
            (CELL_W * scale, CELL_H * scale), Image.Resampling.NEAREST
        )
        x = index * CELL_W * scale
        review.alpha_composite(checkerboard(enlarged.size), (x, 28))
        review.alpha_composite(enlarged, (x, 28))
        draw.text((x + 5, 7), direction, fill=(240, 214, 132, 255))
        draw.line(
            (x, 28 + FOOT_Y * scale, x + CELL_W * scale, 28 + FOOT_Y * scale),
            fill=(80, 255, 130, 255),
            width=1,
        )
        draw.rectangle(
            (x, 28, x + CELL_W * scale - 1, 28 + CELL_H * scale - 1),
            outline=(110, 125, 128, 255),
        )
    review.convert("RGB").save(args.output / "review-4x.png", quality=95)


if __name__ == "__main__":
    main()
