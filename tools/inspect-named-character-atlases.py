"""Generate deterministic review sheets for the seven named survivor atlases.

This is a visual QA helper, not a production-asset transformer. It never edits
source art. Each source cell is copied onto an opaque checkerboard with gutters,
labels, and a red warning border when opaque pixels touch a cell boundary.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = ROOT / "godot" / "assets" / "world"
OUTPUT_ROOT = (
    ROOT
    / "artifacts"
    / "playtest"
    / "2026-07-24-equipment-visual-fix"
    / "atlas-review"
)

CHARACTERS = (
    ("sam", "山姆"),
    ("notty", "诺蒂"),
    ("christine", "克莉丝汀"),
    ("rat", "耗子"),
    ("doug", "道格"),
    ("nightingale", "南丁格尔"),
    ("pete", "皮特"),
)

DIRECTIONS = ("南", "西南", "西", "西北", "北", "东北", "东", "东南")
NORMAL_COLUMNS = (
    "待机",
    "走路1",
    "走路2",
    "走路3",
    "旧攻击1",
    "旧攻击2",
    "受击",
    "工作",
    "站读",
    "坐读",
    "坐下",
    "躺卧",
)
ATTACK_ACTIONS = (
    "单手挥砍",
    "单手戳刺",
    "单手射击",
    "双手挥砍",
    "双手戳刺",
    "双手射击",
    "拉弓射箭",
)

CELL_W = 128
CELL_H = 147
LABEL_H = 30
GUTTER = 8
SCALE = 2


def checkerboard(size: tuple[int, int]) -> Image.Image:
    image = Image.new("RGBA", size, (38, 42, 43, 255))
    draw = ImageDraw.Draw(image)
    tile = 16
    colors = ((38, 42, 43, 255), (55, 60, 61, 255))
    for y in range(0, size[1], tile):
        for x in range(0, size[0], tile):
            draw.rectangle(
                (x, y, x + tile - 1, y + tile - 1),
                fill=colors[(x // tile + y // tile) % 2],
            )
    return image


def touches_boundary(cell: Image.Image) -> tuple[bool, tuple[int, int, int, int]]:
    alpha = cell.getchannel("A")
    top = sum(1 for value in alpha.crop((0, 0, CELL_W, 1)).getdata() if value)
    bottom = sum(
        1 for value in alpha.crop((0, CELL_H - 1, CELL_W, CELL_H)).getdata() if value
    )
    left = sum(1 for value in alpha.crop((0, 0, 1, CELL_H)).getdata() if value)
    right = sum(
        1 for value in alpha.crop((CELL_W - 1, 0, CELL_W, CELL_H)).getdata() if value
    )
    counts = (top, bottom, left, right)
    return any(counts), counts


def draw_cell(
    sheet: Image.Image,
    source: Image.Image,
    source_column: int,
    source_row: int,
    destination_column: int,
    destination_row: int,
    label: str,
) -> tuple[bool, tuple[int, int, int, int]]:
    cell = source.crop(
        (
            source_column * CELL_W,
            source_row * CELL_H,
            (source_column + 1) * CELL_W,
            (source_row + 1) * CELL_H,
        )
    )
    warning, counts = touches_boundary(cell)
    cell = cell.resize((CELL_W * SCALE, CELL_H * SCALE), Image.Resampling.NEAREST)

    x = GUTTER + destination_column * (CELL_W * SCALE + GUTTER)
    y = LABEL_H + GUTTER + destination_row * (CELL_H * SCALE + LABEL_H + GUTTER)
    sheet.alpha_composite(checkerboard(cell.size), (x, y))
    sheet.alpha_composite(cell, (x, y))

    draw = ImageDraw.Draw(sheet)
    border = (255, 72, 72, 255) if warning else (122, 138, 138, 255)
    draw.rectangle((x - 1, y - 1, x + cell.width, y + cell.height), outline=border, width=2)
    draw.text((x + 4, y - LABEL_H + 5), label, fill=(235, 232, 216, 255))
    return warning, counts


def make_sheet(columns: int, rows: int, title: str) -> Image.Image:
    width = GUTTER + columns * (CELL_W * SCALE + GUTTER)
    height = LABEL_H + GUTTER + rows * (CELL_H * SCALE + LABEL_H + GUTTER)
    sheet = Image.new("RGBA", (width, height), (18, 21, 22, 255))
    ImageDraw.Draw(sheet).text((GUTTER, 7), title, fill=(245, 210, 104, 255))
    return sheet


def main() -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    report_lines = [
        "# 七名角色原始图集机械检查",
        "",
        "红框只表示该格有非透明像素触及边界；它是需要人工复核的风险信号，"
        "不自动等同于美术错误。最终结论必须结合逐格目视检查。",
        "",
        "| 角色 | 常规动作触边格 / 96 | 持械动作触边格 / 168 |",
        "|---|---:|---:|",
    ]

    for slug, display_name in CHARACTERS:
        normal = Image.open(SOURCE_ROOT / "animations" / f"{slug}.png").convert("RGBA")
        if normal.size != (CELL_W * 12, CELL_H * 8):
            raise ValueError(f"{slug} normal atlas has unexpected size {normal.size}")
        normal_sheet = make_sheet(12, 8, f"{display_name} / 常规动作 / 红框=触边风险")
        normal_warnings = 0
        for row, direction in enumerate(DIRECTIONS):
            for column, action in enumerate(NORMAL_COLUMNS):
                warning, _ = draw_cell(
                    normal_sheet,
                    normal,
                    column,
                    row,
                    column,
                    row,
                    f"{direction}-{action}",
                )
                normal_warnings += int(warning)
        normal_sheet.convert("RGB").save(
            OUTPUT_ROOT / f"{slug}-normal-review.png", quality=95
        )

        attack = Image.open(SOURCE_ROOT / "attacks" / f"{slug}.png").convert("RGBA")
        if attack.size != (CELL_W * 21, CELL_H * 8):
            raise ValueError(f"{slug} attack atlas has unexpected size {attack.size}")
        attack_warnings = 0
        for action_index, action in enumerate(ATTACK_ACTIONS):
            action_sheet = make_sheet(
                3, 8, f"{display_name} / {action} / 三关键帧 / 红框=触边风险"
            )
            for row, direction in enumerate(DIRECTIONS):
                for frame in range(3):
                    warning, _ = draw_cell(
                        action_sheet,
                        attack,
                        action_index * 3 + frame,
                        row,
                        frame,
                        row,
                        f"{direction}-帧{frame + 1}",
                    )
                    attack_warnings += int(warning)
            action_sheet.convert("RGB").save(
                OUTPUT_ROOT / f"{slug}-attack-{action_index + 1}.png", quality=95
            )

        report_lines.append(
            f"| {display_name} | {normal_warnings} | {attack_warnings} |"
        )

    (OUTPUT_ROOT / "mechanical-check.md").write_text(
        "\n".join(report_lines) + "\n", encoding="utf-8"
    )


if __name__ == "__main__":
    main()
