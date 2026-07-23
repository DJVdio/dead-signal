"""Build a reviewable 21x8 weapon-body atlas from generated key poses.

The generated inputs contain five authored directions per action. The remaining
three directions are exact horizontal mirrors. Wind-up and recovery deliberately
use the accepted idle frame; the middle frame is the weapon-specific body pose.
Weapons are never baked into this atlas and are drawn by the runtime layer.
"""

from __future__ import annotations

import argparse
from collections import deque
import importlib.util
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageOps

_TURNAROUND_PATH = Path(__file__).with_name("prepare-character-turnaround.py")
_TURNAROUND_SPEC = importlib.util.spec_from_file_location(
    "prepare_character_turnaround", _TURNAROUND_PATH
)
if _TURNAROUND_SPEC is None or _TURNAROUND_SPEC.loader is None:
    raise RuntimeError(f"cannot load {_TURNAROUND_PATH}")
_TURNAROUND = importlib.util.module_from_spec(_TURNAROUND_SPEC)
_TURNAROUND_SPEC.loader.exec_module(_TURNAROUND)

CELL_H = _TURNAROUND.CELL_H
CELL_W = _TURNAROUND.CELL_W
FOOT_Y = _TURNAROUND.FOOT_Y
checkerboard = _TURNAROUND.checkerboard
remove_magenta = _TURNAROUND.remove_magenta


ACTION_NAMES = (
    "one-hand-swing",
    "one-hand-thrust",
    "one-hand-shot",
    "two-hand-swing",
    "two-hand-thrust",
    "two-hand-shot",
    "bow-shot",
)
AUTHORED_DIRECTIONS = 5
ALL_DIRECTIONS = 8
TARGET_MEDIAN_HEIGHT = 132


def foreground_row_bounds(source: Image.Image, expected_rows: int) -> list[tuple[int, int]]:
    transparent = remove_magenta(source)
    alpha = np.asarray(transparent.getchannel("A"))
    occupied = np.where((alpha >= 16).sum(axis=1) >= 4)[0]
    if len(occupied) == 0:
        raise ValueError("source contains no foreground rows")

    runs: list[tuple[int, int]] = []
    start = previous = int(occupied[0])
    for row in occupied[1:]:
        row = int(row)
        if row - previous > 8:
            runs.append((start, previous + 1))
            start = row
        previous = row
    runs.append((start, previous + 1))
    if len(runs) != expected_rows:
        raise ValueError(
            f"expected {expected_rows} foreground row bands, found {len(runs)}"
        )
    return [
        (max(0, top - 4), min(source.height, bottom + 4))
        for top, bottom in runs
    ]


def significant_connected_components(alpha: np.ndarray) -> np.ndarray:
    foreground = alpha >= 16
    visited = np.zeros(foreground.shape, dtype=bool)
    components: list[list[tuple[int, int]]] = []
    height, width = foreground.shape
    for start_y, start_x in zip(*np.where(foreground & ~visited)):
        if visited[start_y, start_x]:
            continue
        queue = deque([(int(start_y), int(start_x))])
        visited[start_y, start_x] = True
        component: list[tuple[int, int]] = []
        while queue:
            y, x = queue.popleft()
            component.append((y, x))
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    if dx == 0 and dy == 0:
                        continue
                    ny, nx = y + dy, x + dx
                    if (
                        0 <= ny < height
                        and 0 <= nx < width
                        and foreground[ny, nx]
                        and not visited[ny, nx]
                    ):
                        visited[ny, nx] = True
                        queue.append((ny, nx))
        components.append(component)

    kept = np.zeros_like(alpha)
    if components:
        largest = max(len(component) for component in components)
        minimum = max(24, round(largest * 0.01))
        for component in components:
            if len(component) < minimum:
                continue
            ys, xs = zip(*component)
            kept[np.asarray(ys), np.asarray(xs)] = alpha[
                np.asarray(ys), np.asarray(xs)
            ]
    return kept


def isolate_subject(cell: Image.Image) -> tuple[Image.Image, tuple[int, int, int, int]]:
    transparent = remove_magenta(cell)
    alpha = np.asarray(transparent.getchannel("A"))
    column_counts = (alpha >= 16).sum(axis=0)
    occupied = np.where(column_counts >= 4)[0]
    if len(occupied) == 0:
        raise ValueError("action cell contains no foreground")

    runs: list[tuple[int, int]] = []
    start = previous = int(occupied[0])
    for column in occupied[1:]:
        column = int(column)
        if column - previous > 5:
            runs.append((start, previous))
            start = column
        previous = column
    runs.append((start, previous))
    left, right = max(
        runs, key=lambda run: int(column_counts[run[0] : run[1] + 1].sum())
    )
    left = max(0, left - 4)
    right = min(alpha.shape[1] - 1, right + 4)

    filtered = np.zeros_like(alpha)
    filtered[:, left : right + 1] = alpha[:, left : right + 1]
    filtered = significant_connected_components(filtered)
    rows, columns = np.where(filtered >= 16)
    if len(columns) == 0:
        raise ValueError("isolated action subject is empty")
    bbox = (
        int(columns.min()),
        int(rows.min()),
        int(columns.max()) + 1,
        int(rows.max()) + 1,
    )
    transparent.putalpha(Image.fromarray(filtered))
    return transparent.crop(bbox), bbox


def extract_authored(source: Image.Image, rows: int) -> list[list[Image.Image]]:
    crops: list[list[Image.Image]] = []
    heights: list[int] = []
    widths: list[int] = []
    row_bounds = foreground_row_bounds(source, rows)
    for row, (top, bottom) in enumerate(row_bounds):
        row_crops: list[Image.Image] = []
        for column in range(AUTHORED_DIRECTIONS):
            left = round(column * source.width / AUTHORED_DIRECTIONS)
            right = round((column + 1) * source.width / AUTHORED_DIRECTIONS)
            subject, _ = isolate_subject(source.crop((left, top, right, bottom)))
            row_crops.append(subject)
            heights.append(subject.height)
            widths.append(subject.width)
        crops.append(row_crops)

    median_height = float(np.median(np.asarray(heights)))
    scale = min(
        TARGET_MEDIAN_HEIGHT / median_height,
        (FOOT_Y - 4) / max(heights),
        (CELL_W - 8) / max(widths),
    )
    normalized: list[list[Image.Image]] = []
    for row_crops in crops:
        normalized_row: list[Image.Image] = []
        for subject in row_crops:
            width = max(1, round(subject.width * scale))
            height = max(1, round(subject.height * scale))
            resized = subject.resize((width, height), Image.Resampling.LANCZOS)
            frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
            frame.alpha_composite(
                resized, ((CELL_W - width) // 2, FOOT_Y - height + 1)
            )
            normalized_row.append(frame)
        normalized.append(normalized_row)
    return normalized


def complete_directions(authored: list[Image.Image]) -> list[Image.Image]:
    if len(authored) != AUTHORED_DIRECTIONS:
        raise ValueError("expected five authored directions")
    # Image sheets are authored clockwise from south to north on the actor's
    # screen-right side: S, SE, E, NE, N. Runtime rows are counter-clockwise:
    # S, SW, W, NW, N, NE, E, SE. The old direct append silently swapped east
    # and west, so a west-facing body still punched and aimed to screen-right.
    return [
        authored[0],
        ImageOps.mirror(authored[1]),
        ImageOps.mirror(authored[2]),
        ImageOps.mirror(authored[3]),
        authored[4],
        authored[3],
        authored[2],
        authored[1],
    ]


def load_idle_frames(directory: Path) -> list[Image.Image]:
    paths = sorted(
        (path for path in directory.glob("*.png") if path.name != "review-4x.png"),
        key=lambda path: int(path.name.split("-", 1)[0]),
    )
    if len(paths) != ALL_DIRECTIONS:
        raise ValueError(f"expected eight idle frames in {directory}, found {len(paths)}")
    return [Image.open(path).convert("RGBA") for path in paths]


def make_action_review(
    action_name: str,
    idle: list[Image.Image],
    key_frames: list[Image.Image],
    output: Path,
) -> None:
    scale = 2
    frame_w = CELL_W * scale
    frame_h = CELL_H * scale
    canvas = Image.new(
        "RGBA", (ALL_DIRECTIONS * frame_w, 3 * frame_h + 28), (16, 19, 20, 255)
    )
    draw = ImageDraw.Draw(canvas)
    draw.text((6, 7), action_name, fill=(240, 214, 132, 255))
    for direction in range(ALL_DIRECTIONS):
        for phase, frame in enumerate((idle[direction], key_frames[direction], idle[direction])):
            enlarged = frame.resize((frame_w, frame_h), Image.Resampling.NEAREST)
            x = direction * frame_w
            y = 28 + phase * frame_h
            canvas.alpha_composite(checkerboard(enlarged.size), (x, y))
            canvas.alpha_composite(enlarged, (x, y))
            draw.rectangle(
                (x, y, x + frame_w - 1, y + frame_h - 1),
                outline=(110, 125, 128, 255),
            )
    canvas.convert("RGB").save(output, quality=95)


def make_key_pose_review(
    actions: list[list[Image.Image]],
    output: Path,
) -> None:
    scale = 2
    frame_w = CELL_W * scale
    frame_h = CELL_H * scale
    header_h = 28
    canvas = Image.new(
        "RGBA",
        (ALL_DIRECTIONS * frame_w, len(actions) * frame_h + header_h),
        (16, 19, 20, 255),
    )
    draw = ImageDraw.Draw(canvas)
    draw.text((6, 7), "7 actions x 8 directions - key poses", fill=(240, 214, 132, 255))
    for action_index, key_frames in enumerate(actions):
        for direction, frame in enumerate(key_frames):
            enlarged = frame.resize((frame_w, frame_h), Image.Resampling.NEAREST)
            x = direction * frame_w
            y = header_h + action_index * frame_h
            canvas.alpha_composite(checkerboard(enlarged.size), (x, y))
            canvas.alpha_composite(enlarged, (x, y))
            draw.rectangle(
                (x, y, x + frame_w - 1, y + frame_h - 1),
                outline=(110, 125, 128, 255),
            )
    canvas.convert("RGB").save(output, quality=95)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("idle_directory", type=Path)
    parser.add_argument("group_a", type=Path, help="4x5 image: first four actions")
    parser.add_argument("group_b", type=Path, help="3x5 image: final three actions")
    parser.add_argument("output_directory", type=Path)
    args = parser.parse_args()

    idle = load_idle_frames(args.idle_directory)
    source_a = Image.open(args.group_a).convert("RGBA")
    source_b = Image.open(args.group_b).convert("RGBA")
    authored_actions = extract_authored(source_a, 4) + extract_authored(source_b, 3)
    actions = [complete_directions(action) for action in authored_actions]

    args.output_directory.mkdir(parents=True, exist_ok=True)
    atlas = Image.new("RGBA", (CELL_W * 21, CELL_H * 8), (0, 0, 0, 0))
    for action_index, (action_name, key_frames) in enumerate(
        zip(ACTION_NAMES, actions)
    ):
        for direction in range(ALL_DIRECTIONS):
            frames = (idle[direction], key_frames[direction], idle[direction])
            for phase, frame in enumerate(frames):
                atlas.alpha_composite(
                    frame,
                    (
                        (action_index * 3 + phase) * CELL_W,
                        direction * CELL_H,
                    ),
                )
        make_action_review(
            action_name,
            idle,
            key_frames,
            args.output_directory / f"review-{action_index + 1}-{action_name}.png",
        )
    make_key_pose_review(actions, args.output_directory / "review-all-key-poses.png")
    atlas.save(args.output_directory / "attack-atlas.png")


if __name__ == "__main__":
    main()
