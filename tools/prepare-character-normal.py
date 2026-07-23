"""Build reviewable 12x8 normal-state atlases from authored key-pose sheets."""

from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


_ACTIONS_PATH = Path(__file__).with_name("prepare-character-actions.py")
_ACTIONS_SPEC = importlib.util.spec_from_file_location(
    "prepare_character_actions", _ACTIONS_PATH
)
if _ACTIONS_SPEC is None or _ACTIONS_SPEC.loader is None:
    raise RuntimeError(f"cannot load {_ACTIONS_PATH}")
_ACTIONS = importlib.util.module_from_spec(_ACTIONS_SPEC)
_ACTIONS_SPEC.loader.exec_module(_ACTIONS)

CELL_W = _ACTIONS.CELL_W
CELL_H = _ACTIONS.CELL_H
FOOT_Y = _ACTIONS.FOOT_Y
DIRECTIONS = 8
COLUMNS = 12
AUTHORED_DIRECTIONS = 5


def extract_rows(source: Image.Image, rows: int) -> list[list[Image.Image]]:
    crops: list[list[Image.Image]] = []
    row_bounds = _ACTIONS.foreground_row_bounds(source, rows)
    for row, (top, bottom) in enumerate(row_bounds):
        row_crops: list[Image.Image] = []
        for column in range(AUTHORED_DIRECTIONS):
            left = round(column * source.width / AUTHORED_DIRECTIONS)
            right = round((column + 1) * source.width / AUTHORED_DIRECTIONS)
            subject, _ = _ACTIONS.isolate_subject(
                source.crop((left, top, right, bottom))
            )
            row_crops.append(subject)
        crops.append(row_crops)

    reference_height = float(np.median([image.height for image in crops[0]]))
    base_scale = 132 / reference_height
    normalized: list[list[Image.Image]] = []
    for row_crops in crops:
        normalized_row: list[Image.Image] = []
        for subject in row_crops:
            scale = min(
                base_scale,
                (CELL_W - 8) / subject.width,
                (FOOT_Y - 4) / subject.height,
            )
            width = max(1, round(subject.width * scale))
            height = max(1, round(subject.height * scale))
            resized = subject.resize((width, height), Image.Resampling.LANCZOS)
            frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
            frame.alpha_composite(
                resized,
                ((CELL_W - width) // 2, FOOT_Y - height + 1),
            )
            normalized_row.append(frame)
        normalized.append(_ACTIONS.complete_directions(normalized_row))
    return normalized


def attack_key_frames(atlas_path: Path, action_index: int) -> list[Image.Image]:
    atlas = Image.open(atlas_path).convert("RGBA")
    column = action_index * 3 + 1
    return [
        atlas.crop(
            (
                column * CELL_W,
                direction * CELL_H,
                (column + 1) * CELL_W,
                (direction + 1) * CELL_H,
            )
        )
        for direction in range(DIRECTIONS)
    ]


def normal_columns(
    idle: list[Image.Image],
    group_a: list[list[Image.Image]],
    group_b: list[list[Image.Image]],
    attack_atlas: Path | None,
    dog: bool,
) -> list[list[Image.Image]]:
    if dog:
        return [
            idle,
            group_a[0],
            group_a[1],
            group_a[2],
            group_b[0],
            group_b[1],
            group_a[3],
            group_b[2],
            group_b[2],
            group_b[3],
            group_b[3],
            group_b[4],
        ]
    if attack_atlas is None:
        raise ValueError("human normal atlas requires an attack atlas")
    return [
        idle,
        group_a[0],
        group_a[1],
        group_a[2],
        attack_key_frames(attack_atlas, 0),
        attack_key_frames(attack_atlas, 1),
        group_a[3],
        group_b[0],
        group_b[1],
        group_b[2],
        group_b[3],
        group_b[4],
    ]


def write_review(columns: list[list[Image.Image]], output: Path) -> None:
    scale = 2
    header = 28
    canvas = Image.new(
        "RGBA",
        (COLUMNS * CELL_W * scale, DIRECTIONS * CELL_H * scale + header),
        (16, 19, 20, 255),
    )
    draw = ImageDraw.Draw(canvas)
    draw.text((6, 7), "12 states x 8 directions", fill=(240, 214, 132, 255))
    for column, frames in enumerate(columns):
        for direction, frame in enumerate(frames):
            enlarged = frame.resize(
                (CELL_W * scale, CELL_H * scale), Image.Resampling.NEAREST
            )
            x = column * CELL_W * scale
            y = header + direction * CELL_H * scale
            canvas.alpha_composite(_ACTIONS.checkerboard(enlarged.size), (x, y))
            canvas.alpha_composite(enlarged, (x, y))
            draw.rectangle(
                (
                    x,
                    y,
                    x + CELL_W * scale - 1,
                    y + CELL_H * scale - 1,
                ),
                outline=(110, 125, 128, 255),
            )
    canvas.convert("RGB").save(output, quality=95)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("idle_directory", type=Path)
    parser.add_argument("group_a", type=Path)
    parser.add_argument("group_b", type=Path)
    parser.add_argument("output_directory", type=Path)
    parser.add_argument("--attack-atlas", type=Path)
    parser.add_argument("--dog", action="store_true")
    args = parser.parse_args()

    idle = _ACTIONS.load_idle_frames(args.idle_directory)
    group_a = extract_rows(Image.open(args.group_a).convert("RGBA"), 4)
    group_b = extract_rows(Image.open(args.group_b).convert("RGBA"), 5)
    columns = normal_columns(idle, group_a, group_b, args.attack_atlas, args.dog)

    args.output_directory.mkdir(parents=True, exist_ok=True)
    atlas = Image.new("RGBA", (COLUMNS * CELL_W, DIRECTIONS * CELL_H))
    for column, frames in enumerate(columns):
        for direction, frame in enumerate(frames):
            atlas.alpha_composite(
                frame,
                (column * CELL_W, direction * CELL_H),
            )
    atlas.save(args.output_directory / "normal-atlas.png")
    write_review(columns, args.output_directory / "review-all-normal-poses.png")


if __name__ == "__main__":
    main()
