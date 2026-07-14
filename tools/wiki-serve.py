#!/usr/bin/env python3
"""Dead Signal 本地 wiki 小服务 —— 起一个只服务 docs/wiki/ 的静态站，并允许网页把改动 PUT 回 JSON 文件。

零第三方依赖（只用 python3 标准库）。跑法：

    python3 tools/wiki-serve.py            # 默认 http://127.0.0.1:8787
    python3 tools/wiki-serve.py --port 9000

只做三件事：
  1. GET  任意路径      → 从 docs/wiki/ 下发静态文件（图标从仓库根的 godot/assets/ 单独放行）
  2. PUT  /data/x.json  → 把请求体写回 docs/wiki/data/x.json（**唯一的写入口**）
  3. 每次写入后重新生成 data/bundle.js，好让 file:// 直接打开时看到的也是最新数据

写入是本服务唯一有副作用的操作，故门槛卡死：
  · 只认 PUT /data/<名字>.json，<名字> 必须匹配 ^[a-z0-9_-]+$ —— 连 '.' 和 '/' 都进不来，路径穿越无从谈起
  · 落盘前先 realpath 复核目标仍在 docs/wiki/data/ 内（软链接也糊弄不过去）
  · 请求体必须是能解析的 JSON，且结构上像一张分区表（有 columns / rows）——半截数据不许覆盖好数据
  · 先写临时文件再原子替换：写一半断电也不会留下一个坏掉的 json
  · 只监听 127.0.0.1（不对局域网开放）
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import tempfile
from http.server import HTTPServer, SimpleHTTPRequestHandler
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
WIKI_DIR = REPO_ROOT / "docs" / "wiki"
DATA_DIR = WIKI_DIR / "data"
ASSETS_DIR = REPO_ROOT / "godot" / "assets"

# 可写文件名白名单：只允许 data/ 下的 <名字>.json，名字里连点和斜杠都不许有。
SAFE_NAME = re.compile(r"^[a-z0-9_-]+$")

MAX_BODY_BYTES = 8 * 1024 * 1024  # 8MB：最大的分区表也就几十 KB，超了必是有鬼


class WikiHandler(SimpleHTTPRequestHandler):
    """静态下发 docs/wiki/，外加一个写回 data/*.json 的 PUT。"""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(WIKI_DIR), **kwargs)

    # ---------- 读 ----------

    def translate_path(self, path: str) -> str:
        """/assets/... → 仓库的 godot/assets/（物品图标），其余照常在 docs/wiki/ 下找。

        父类已经做了 '..' 归一化与根目录夹紧，这里只是把图标目录额外挂进来。
        """
        clean = path.split("?", 1)[0].split("#", 1)[0]
        if clean.startswith("/assets/"):
            rel = clean[len("/assets/"):]
            target = (ASSETS_DIR / rel).resolve()
            if not _is_within(target, ASSETS_DIR):
                return str(WIKI_DIR / "__forbidden__")
            return str(target)
        return super().translate_path(path)

    def do_GET(self) -> None:
        if self.path.split("?", 1)[0] == "/api/icons":
            return self._ok({"icons": scan_icons()})
        super().do_GET()

    def end_headers(self) -> None:
        # 改完数值刷新就要看见新的，别让浏览器拿缓存糊弄人。
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    # ---------- 写 ----------

    def do_PUT(self) -> None:
        path = self.path.split("?", 1)[0]
        prefix = "/data/"
        if not path.startswith(prefix) or not path.endswith(".json"):
            return self._fail(403, "只能写 /data/<名字>.json")

        name = path[len(prefix):-len(".json")]
        if not SAFE_NAME.match(name):
            return self._fail(403, f"非法文件名：{name!r}（只允许小写字母/数字/下划线/连字符）")

        target = (DATA_DIR / f"{name}.json").resolve()
        if not _is_within(target, DATA_DIR):
            return self._fail(403, "目标不在 docs/wiki/data/ 内")

        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return self._fail(400, "Content-Length 不是数字")
        if length <= 0:
            return self._fail(400, "空请求体")
        if length > MAX_BODY_BYTES:
            return self._fail(413, f"请求体过大（{length} 字节）")

        raw = self.rfile.read(length)
        try:
            payload = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as e:
            return self._fail(400, f"请求体不是合法 JSON：{e}")

        # 结构闸门：写回来的必须还是一张分区表。防的是"网页出 bug 把空对象存上去"这类事故。
        if not isinstance(payload, dict) or "columns" not in payload or "rows" not in payload:
            return self._fail(400, "JSON 结构不像分区表（缺 columns / rows），拒绝覆盖")
        if not isinstance(payload["rows"], list) or not payload["rows"]:
            return self._fail(400, "rows 为空，拒绝覆盖（不接受把一张表清空）")

        try:
            _write_atomic(target, json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
            regenerate_bundle()
        except OSError as e:
            return self._fail(500, f"落盘失败：{e}")

        rel = target.relative_to(REPO_ROOT)
        print(f"  [保存] {rel}  ({len(payload['rows'])} 条)")
        self._ok({"ok": True, "file": str(rel), "rows": len(payload["rows"])})

    # ---------- 杂项 ----------

    def _ok(self, obj: dict) -> None:
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _fail(self, code: int, msg: str) -> None:
        body = json.dumps({"ok": False, "error": msg}, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
        print(f"  [拒绝 {code}] {msg}", file=sys.stderr)

    def log_message(self, fmt: str, *args) -> None:
        # 默认那行 access log 太吵；写入/拒绝我们自己打。
        pass


def scan_icons() -> list[str]:
    """磁盘上现有的图标，列成相对 items/ 的路径（不带扩展名）：["weapons/dagger", "materials/wood", …]。

    网页拿它来决定「这条有没有图」——只给真有图的条目发 <img>，缺图的直接画占位框。
    否则每开一次页面就是几十个 404，把真正的报错淹了。素材是分批加的，故每次请求现扫。
    （每行的图标名在 JSON 的 _icon 里，来源是 godot/scripts/ItemIcons.cs 这张单一事实源表。）
    """
    root = ASSETS_DIR / "items"
    if not root.is_dir():
        return []
    return sorted(str(p.relative_to(root).with_suffix("")) for p in root.glob("*/*.png"))


def _is_within(target: Path, parent: Path) -> bool:
    """target 是否确实落在 parent 之内（realpath 之后比，软链接也绕不过去）。"""
    try:
        target.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def _write_atomic(target: Path, text: str) -> None:
    """先写同目录临时文件再 os.replace —— 要么是旧的完整文件，要么是新的完整文件，没有中间态。"""
    fd, tmp = tempfile.mkstemp(dir=str(target.parent), prefix=".wiki-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, target)
    except BaseException:
        Path(tmp).unlink(missing_ok=True)
        raise


def regenerate_bundle() -> None:
    """按 data/ 下现有的 json 重新生成 bundle.js（file:// 打开时的降级数据源）。

    抽取器也会生成同一个文件；两边都写是刻意的——否则用户在网页上改完、
    换成双击 index.html 打开，会看到一份过期的旧数据，还以为改丢了。
    """
    index_path = DATA_DIR / "index.json"
    if not index_path.exists():
        return
    index = json.loads(index_path.read_text(encoding="utf-8"))

    lines = [
        "// 自动生成，勿手改（tools/WikiExtract 重跑，或本地服务保存时刷新）。",
        "// 用途：以 file:// 直接打开 index.html 时的降级数据源（浏览器不允许 fetch 本地文件）。",
        "window.WIKI_BUNDLE = {",
        f"  index: {json.dumps(index, ensure_ascii=False, indent=2)},",
        "  data: {",
    ]
    for cat in index.get("categories", []):
        f = DATA_DIR / cat["file"]
        if not f.exists():
            continue
        obj = json.loads(f.read_text(encoding="utf-8"))
        lines.append(f"    {json.dumps(cat['id'])}: {json.dumps(obj, ensure_ascii=False, indent=2)},")
    lines += ["  }", "};", ""]
    _write_atomic(DATA_DIR / "bundle.js", "\n".join(lines))


def main() -> int:
    ap = argparse.ArgumentParser(description="Dead Signal 本地 wiki 服务（可编辑数值表）")
    ap.add_argument("--port", type=int, default=8787, help="端口（默认 8787）")
    ap.add_argument("--host", default="127.0.0.1", help="监听地址（默认只听本机）")
    args = ap.parse_args()

    if not (WIKI_DIR / "index.html").exists():
        print(f"找不到 {WIKI_DIR / 'index.html'}", file=sys.stderr)
        return 1
    if not (DATA_DIR / "index.json").exists():
        print("data/index.json 不存在——先跑一次抽取器：", file=sys.stderr)
        print("  export DOTNET_ROOT=$HOME/.dotnet && ~/.dotnet/dotnet run --project tools/WikiExtract", file=sys.stderr)
        return 1

    regenerate_bundle()  # 起服务时先对齐一次，免得 bundle.js 是旧的

    try:
        httpd = HTTPServer((args.host, args.port), WikiHandler)
    except OSError as e:
        print(f"端口 {args.port} 起不来：{e}\n换一个：python3 tools/wiki-serve.py --port 8788", file=sys.stderr)
        return 1

    url = f"http://{args.host}:{args.port}/"
    print("Dead Signal 数值 wiki")
    print(f"  打开：{url}")
    print(f"  数据：{DATA_DIR.relative_to(REPO_ROOT)}/*.json（网页点「保存」直接写回这里）")
    print("  停止：Ctrl-C\n")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n已停止。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
