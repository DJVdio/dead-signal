#!/usr/bin/env python3
"""Build deterministic 7-action × 3-frame × 8-direction attack atlases.

The authored 12-column actor atlas owns the empty-hand windup/recovery frames.
The seven-action atlas owns each weapon-family delivery pose.  Combining those
sources keeps character identity stable while giving every family an explicit
three-frame sequence.  Runtime paper-doll art remains the source of truth for
the weapon actually held.
"""

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ANIMATION_ROOT = ROOT / "godot/assets/world/animations"
ATTACK_ROOT = ROOT / "godot/assets/world/attacks"
ACTORS = (
    "sam",
    "notty",
    "christine",
    "rat",
    "doug",
    "nightingale",
    "pete",
    "survivor",
    "raider",
)

CELL_W = 128
CELL_H = 147
DIRECTIONS = 8
ACTIONS = 7
FRAMES = 3
BASE_COLUMNS = 12
WINDUP_COLUMN = 4
RECOVERY_COLUMN = 5


def cell(image: Image.Image, column: int, row: int) -> Image.Image:
    left = column * CELL_W
    top = row * CELL_H
    return image.crop((left, top, left + CELL_W, top + CELL_H))


def keep_primary_figure(frame: Image.Image) -> Image.Image:
    """Remove disconnected generation debris without repainting authored pixels."""
    alpha = frame.getchannel("A")
    solid = alpha.point(lambda value: 255 if value >= 12 else 0)
    width, height = frame.size
    pixels = solid.load()
    seen: set[tuple[int, int]] = set()
    components: list[list[tuple[int, int]]] = []
    for y in range(height):
        for x in range(width):
            if not pixels[x, y] or (x, y) in seen:
                continue
            stack = [(x, y)]
            seen.add((x, y))
            component: list[tuple[int, int]] = []
            while stack:
                px, py = stack.pop()
                component.append((px, py))
                for nx, ny in ((px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1)):
                    if 0 <= nx < width and 0 <= ny < height and pixels[nx, ny] and (nx, ny) not in seen:
                        seen.add((nx, ny))
                        stack.append((nx, ny))
            components.append(component)
    if not components:
        return frame

    primary = max(components, key=len)
    left = min(x for x, _ in primary) - 5
    right = max(x for x, _ in primary) + 5
    top = min(y for _, y in primary) - 5
    bottom = max(y for _, y in primary) + 5
    keep = Image.new("L", frame.size)
    keep_pixels = keep.load()
    for component in components:
        if any(left <= x <= right and top <= y <= bottom for x, y in component):
            for x, y in component:
                keep_pixels[x, y] = 255
    cleaned = frame.copy()
    cleaned.putalpha(Image.composite(alpha, Image.new("L", frame.size), keep))
    return cleaned


def ensure_cell_padding(frame: Image.Image, padding: int = 2) -> Image.Image:
    """Prevent edge-touching pixels from bleeding into adjacent atlas cells."""
    bbox = frame.getchannel("A").getbbox()
    if bbox is None:
        return frame
    left, top, right, bottom = bbox
    if left >= padding and top >= padding and right <= CELL_W - padding and bottom <= CELL_H - padding:
        return frame

    figure = frame.crop(bbox)
    scale = min(
        (CELL_W - padding * 2) / figure.width,
        (CELL_H - padding * 2) / figure.height,
        1.0,
    )
    if scale < 1.0:
        figure = figure.resize(
            (max(1, round(figure.width * scale)), max(1, round(figure.height * scale))),
            Image.Resampling.NEAREST,
        )
    output = Image.new("RGBA", frame.size)
    x = (CELL_W - figure.width) // 2
    y = CELL_H - padding - figure.height
    output.paste(figure, (x, y))
    return output


def build(name: str) -> None:
    base_path = ANIMATION_ROOT / f"{name}.png"
    attack_path = ATTACK_ROOT / f"{name}.png"
    with Image.open(base_path).convert("RGBA") as base, Image.open(attack_path).convert("RGBA") as attacks:
        assert base.size == (BASE_COLUMNS * CELL_W, DIRECTIONS * CELL_H), (name, base.size)
        source_columns = attacks.width // CELL_W
        assert source_columns in (ACTIONS, ACTIONS * FRAMES), (name, attacks.size)

        output = Image.new("RGBA", (ACTIONS * FRAMES * CELL_W, DIRECTIONS * CELL_H))
        for row in range(DIRECTIONS):
            windup = ensure_cell_padding(keep_primary_figure(cell(base, WINDUP_COLUMN, row)))
            recovery = ensure_cell_padding(keep_primary_figure(cell(base, RECOVERY_COLUMN, row)))
            for action in range(ACTIONS):
                output.paste(windup, ((action * FRAMES) * CELL_W, row * CELL_H))
                delivery_column = action if source_columns == ACTIONS else action * FRAMES + 1
                delivery = ensure_cell_padding(keep_primary_figure(cell(attacks, delivery_column, row)))
                output.paste(delivery, ((action * FRAMES + 1) * CELL_W, row * CELL_H))
                output.paste(recovery, ((action * FRAMES + 2) * CELL_W, row * CELL_H))

        output.save(attack_path, optimize=True)
        print(f"{name}: {output.size[0]}x{output.size[1]} RGBA")


def main() -> None:
    for name in ACTORS:
        build(name)


if __name__ == "__main__":
    main()
