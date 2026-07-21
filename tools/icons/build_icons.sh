#!/usr/bin/env bash
# 物品图标生成脚本 —— 从 game-icons.net 拉 SVG，渲染成 32×32 单色硬边 PNG，落到 godot/assets/items/<分区>/<slug>.png
#
# 单一事实源是 godot/scripts/ItemIcons.cs 的那张映射表（物品引用键 → 分区/slug/素材出处），
# 本脚本只是"照着表去取图、去渲染"，不自己维护第二份清单——避免两处清单漂移。
#
# 用法：
#   tools/icons/build_icons.sh                # 只补缺（已存在的 PNG 跳过），默认行为
#   tools/icons/build_icons.sh --all          # 全量重建（改了渲染参数时用）
#   tools/icons/build_icons.sh dagger wood    # 只生成指定 slug
#
# 依赖：curl + ImageMagick(magick)。素材授权 CC-BY 3.0，署名见 godot/assets/items/CREDITS.md。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MAP="$ROOT/godot/scripts/ItemIcons.cs"
OUT="$ROOT/godot/assets/items"
REPO="https://raw.githubusercontent.com/game-icons/icons/master"
CACHE="${TMPDIR:-/tmp}/dead-signal-icons-svg"

# 渲染参数：32×32；alpha 阈值化成 1-bit（硬边＝像素感，不留半透明毛边）；填成米白，与 UI 底色对齐。
SIZE=32
TINT='#E8E4D8'
ALPHA_THRESHOLD='40%'

command -v magick >/dev/null || { echo "缺 ImageMagick：brew install imagemagick" >&2; exit 1; }
mkdir -p "$CACHE"

FORCE=0
FILTER=()
for a in "$@"; do
  case "$a" in
    --all) FORCE=1 ;;
    *) FILTER+=("$a") ;;
  esac
done

# 从 ItemIcons.cs 里抽出 (分区, slug, 素材出处) 三元组（BSD sed 不认 \w，交给 python 解析更稳）
mapfile -t ENTRIES < <(python3 -c '
import re,sys
src=open(sys.argv[1],encoding="utf8").read()
for cat,slug,source in re.findall(r"new\((Weapons|Armor|Mats|Lights|Books|Food|Furniture|Tools), \"([a-z0-9_]+)\", \"([^\"]+)\"\)", src):
    print(cat,slug,source)
' "$MAP")

declare -A DIR=( [Weapons]=weapons [Armor]=armor [Mats]=materials [Lights]=lights [Books]=books [Food]=food [Furniture]=furniture [Tools]=tools )

made=0; skipped=0; failed=0
for e in "${ENTRIES[@]}"; do
  read -r cat slug source <<<"$e"
  dir="$OUT/${DIR[$cat]}"
  png="$dir/$slug.png"

  if [ ${#FILTER[@]} -gt 0 ] && [[ ! " ${FILTER[*]} " =~ " $slug " ]]; then continue; fi
  if [ -f "$png" ] && [ "$FORCE" -eq 0 ]; then skipped=$((skipped+1)); continue; fi

  mkdir -p "$dir"
  svg="$CACHE/$(echo "$source" | tr '/' '_').svg"
  if [ ! -s "$svg" ]; then
    # Clash 链路偶发 SSL_ERROR_SYSCALL，重试三次
    for i in 1 2 3; do curl -fsS --max-time 25 -o "$svg" "$REPO/$source.svg" && break; sleep 2; done
  fi
  if [ ! -s "$svg" ]; then echo "  ✗ ${slug}（拉不到 ${source}）"; failed=$((failed+1)); continue; fi

  # game-icons 的 SVG 自带一整块黑色底板（首个 path 就是 512×512 的方块），
  # 直接渲染会得到一张黑底方图——先把它剥掉，才剩下真正的图形。
  clean="$CACHE/clean_$(basename "$svg")"
  sed -E 's#<path d="M0 0h512v512H0z"/>##' "$svg" > "$clean"

  magick -background none "$clean" -resize "${SIZE}x${SIZE}" \
    -channel A -threshold "$ALPHA_THRESHOLD" +channel \
    -fill "$TINT" -colorize 100 "$png"
  made=$((made+1))
done

echo "图标生成：新建 $made / 跳过 $skipped / 失败 $failed → $OUT"
