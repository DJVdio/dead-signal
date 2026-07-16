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
import hashlib
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
# 游戏真读的数值配置（wiki↔config 双向联动的另一端）。抽取器在展示表里埋下 configFile/configKey/_configId
# 三个锚点，本服务照它把数值双向搬运：wiki 改数值→投影写回这里；这里改了→GET/启动按它重算展示表。
CONFIG_DIR = REPO_ROOT / "godot" / "data" / "config"

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
        path = self.path.split("?", 1)[0]
        if path == "/api/icons":
            return self._ok({"icons": scan_icons()})
        # config-backed 展示表：下发前先按 config json 重算（config→wiki 实时互现）。
        m = re.match(r"^/data/([a-z0-9_-]+)\.json$", path)
        if m and self._serve_reconciled(m.group(1)):
            return
        super().do_GET()

    def _serve_reconciled(self, name: str) -> bool:
        """把 config 值拉进展示表再下发。非 config-backed 表返回 False，交回父类走静态下发。"""
        try:
            obj = reconcile_data_file(name)
        except OSError:
            return False
        if obj is None or not obj.get("configFile"):
            return False
        body = json.dumps(obj, ensure_ascii=False, indent=2).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
        return True

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

        # ── wiki→config 双向：算出要投影写回 config 的数值 + 乐观锁冲突判定 ──
        # （非 config-backed 表 plan 为空，行为与从前完全一致——只落 wiki 展示 json。）
        config_file, _ = _config_mapping(payload)
        cfg = _load_config_dict(config_file) if config_file else None
        plan = plan_projection(payload, cfg)
        if plan["conflict"]:
            # config 在本页加载之后被别处改过（Python/别的编辑），且本次要改同一个 config 文件里的值。
            # 字段级 last-write-wins + 乐观 base-version：撞了就让用户 reload，绝不闷头覆盖。
            return self._fail(409, "config json 已被其它改动更新——请刷新页面(reload)后再存，避免覆盖对方的改动。")

        try:
            _write_atomic(target, json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
            if plan["pending"]:
                apply_projection(config_file, cfg, plan["pending"])
            regenerate_bundle()
        except OSError as e:
            return self._fail(500, f"落盘失败：{e}")

        rel = target.relative_to(REPO_ROOT)
        n_proj = len(plan["pending"])
        tail = f"  → 投影写回 {config_file}：{n_proj} 个数值" if n_proj else ""
        print(f"  [保存] {rel}  ({len(payload['rows'])} 条){tail}")
        if plan["unsupported"]:
            print(f"  [双向] ⚠ {config_file} 含注释(JSONC)或缺失，config-backed 改动未投影"
                  f"（仅落了 wiki 展示 json）", file=sys.stderr)
        self._ok({"ok": True, "file": str(rel), "rows": len(payload["rows"]), "projected": n_proj})

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


# ══════════════════════════ wiki ↔ config 数值双向联动 ══════════════════════════
#
# 两份 json：
#   · config json（godot/data/config/*.json）—— 游戏真读的数值，扁平 {id: {字段: 值}}，事实源。
#   · 展示 json（docs/wiki/data/*.json）—— 网页看的分区表，抽取器生成。
# 抽取器给 config-backed 的表埋了三个自描述锚点，让本服务无需硬编码任何映射即可双向搬运：
#   · 表级 configFile：这张表镜像哪个 config json。
#   · 列级 configKey ：这一列 = config 条目的哪个字段（如 damageMin → DamageMin）。
#   · 行级 _configId ：这一行 = config json 里的哪个条目键（如 Dagger 行 → "dagger"）。
# 只搬**数值/gameplay 字段**（带 configKey 的列）；简介/flavor/备注没有 configKey，永不写 config ⇒ 天然无冲突。
#
# 冲突模型 = 字段级 last-write-wins + 乐观 base-version：展示表带一个 _configVersion（config 投影的哈希）；
# PUT 携带它回来，若与当前 config 哈希不符且本次确有 config-backed 改动 ⇒ 409 让用户 reload（不闷头覆盖）。
#
# ⚠️ 安全护栏：只对**纯 JSON** 的 config 文件投影写回。含注释(JSONC)的 config（materials/furniture/… 那批）
#   json.loads 会失败 ⇒ 本服务判为「暂不支持」，跳过投影（绝不 strip 注释再写回——那会毁掉设计者的注释）。


def _config_mapping(obj: dict):
    """从一张展示表读出 (configFile, {展示列key: config字段名})。非 config-backed 表 ⇒ (None, {})。"""
    config_file = obj.get("configFile")
    if not isinstance(config_file, str) or not config_file:
        return None, {}
    col_map = {}
    for col in obj.get("columns", []):
        if isinstance(col, dict) and col.get("configKey") and col.get("key"):
            col_map[col["key"]] = col["configKey"]
    return config_file, col_map


def _load_config_dict(config_file):
    """godot/data/config/<file> → {id: {字段: 值}}。缺失 / 含注释(JSONC) / 坏 json / 非对象 ⇒ None（不支持投影）。"""
    if not config_file:
        return None
    target = (CONFIG_DIR / config_file).resolve()
    if not _is_within(target, CONFIG_DIR) or not target.exists():
        return None
    try:
        data = json.loads(target.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError, OSError):
        return None  # JSONC 或坏文件：不碰（写回会毁注释）
    return data if isinstance(data, dict) else None


def _config_version(cfg: dict) -> str:
    """config 内容的稳定短哈希（乐观锁 base-version 用）。规范化序列化 ⇒ 与格式/键序无关。"""
    canon = json.dumps(cfg, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha1(canon.encode("utf-8")).hexdigest()[:12]


def reconcile_from_config(obj: dict, cfg) -> bool:
    """config → wiki：把 config-backed 单元格重算成 config 值，并盖上 _configVersion。返回是否有改动。

    描述/flavor/备注（无 configKey 的列）一律不碰 ⇒ 数值取 config、文案保留 wiki 原值（merge）。
    """
    config_file, col_map = _config_mapping(obj)
    if not config_file or not col_map or cfg is None:
        return False
    changed = False
    for row in obj.get("rows", []):
        cid = row.get("_configId")
        if not cid or cid not in cfg or not isinstance(cfg[cid], dict):
            continue
        entry = cfg[cid]
        for wiki_key, field in col_map.items():
            if field in entry and row.get(wiki_key) != entry[field]:
                row[wiki_key] = entry[field]
                changed = True
    version = _config_version(cfg)
    if obj.get("_configVersion") != version:
        obj["_configVersion"] = version
        changed = True
    return changed


def plan_projection(payload: dict, cfg):
    """wiki → config：算出要写回 config 的 (id, 字段, 值) 清单 + 冲突判定。

    返回 {"pending": [(id, field, value), ...], "conflict": bool, "unsupported": bool}。
      · pending 只含**确实与当前 config 不同**的 config-backed 单元格（字段级最小写入，不 clobber 未改字段）。
      · conflict：本次有 config-backed 改动，且 payload 带的 base-version 与当前 config 哈希不符。
      · unsupported：表是 config-backed 的，但 config 文件缺失/JSONC ⇒ 不投影（照旧只落 wiki）。
    """
    config_file, col_map = _config_mapping(payload)
    if not config_file or not col_map:
        return {"pending": [], "conflict": False, "unsupported": False}
    if cfg is None:
        return {"pending": [], "conflict": False, "unsupported": True}
    pending = []
    for row in payload.get("rows", []):
        cid = row.get("_configId")
        if not cid or cid not in cfg or not isinstance(cfg[cid], dict):
            continue
        entry = cfg[cid]
        for wiki_key, field in col_map.items():
            if wiki_key in row and field in entry and entry[field] != row[wiki_key]:
                pending.append((cid, field, row[wiki_key]))
    conflict = False
    if pending:
        base = payload.get("_configVersion")
        if base is not None and base != _config_version(cfg):
            conflict = True
    return {"pending": pending, "conflict": conflict, "unsupported": False}


def apply_projection(config_file, cfg: dict, pending) -> None:
    """把 pending 写进 cfg 并原子落盘 config json（调用方须已确保 cfg 来自纯 JSON 文件）。"""
    for cid, field, value in pending:
        cfg[cid][field] = value
    _write_atomic(CONFIG_DIR / config_file,
                  json.dumps(cfg, ensure_ascii=False, indent=2) + "\n")


def reconcile_data_file(name: str):
    """读 data/<name>.json，config→wiki 重算，有改动就写回，返回重算后的 obj（GET 直接下发）。

    非 config-backed 表原样返回；坏 json / 缺失返回 None。
    """
    path = DATA_DIR / f"{name}.json"
    if not path.exists():
        return None
    try:
        obj = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None
    config_file, _ = _config_mapping(obj)
    if not config_file:
        return obj
    cfg = _load_config_dict(config_file)
    if reconcile_from_config(obj, cfg):
        _write_atomic(path, json.dumps(obj, ensure_ascii=False, indent=2) + "\n")
    return obj


def reconcile_all() -> None:
    """启动 / --reconcile：把所有 config-backed 展示表按 config 重算一遍（config→wiki 批量入口）。"""
    for path in sorted(DATA_DIR.glob("*.json")):
        if path.name in ("index.json", "icon-manifest.json"):
            continue
        try:
            obj = json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        config_file, _ = _config_mapping(obj)
        if not config_file:
            continue
        cfg = _load_config_dict(config_file)
        if cfg is None:
            print(f"  [双向] 跳过 {path.name}：config {config_file} 缺失或含注释(JSONC)，暂不支持投影",
                  file=sys.stderr)
            continue
        unmatched = [r.get("_id") for r in obj.get("rows", [])
                     if r.get("_configId") and r["_configId"] not in cfg]
        if reconcile_from_config(obj, cfg):
            _write_atomic(path, json.dumps(obj, ensure_ascii=False, indent=2) + "\n")
            print(f"  [双向] {path.name} ← {config_file}（已按 config 重算 config-backed 单元格）")
        if unmatched:
            print(f"  [双向] ⚠ {path.name} 有 {len(unmatched)} 行 _configId 不在 config（跳过）：{unmatched}",
                  file=sys.stderr)


def _selftest() -> bool:
    """双向联动自测（纯内存 + 真文件冒烟），不起服务、不依赖 git 树状态。全过返回 True。"""
    ok = True

    def check(cond, msg):
        nonlocal ok
        status = "PASS" if cond else "FAIL"
        if not cond:
            ok = False
        print(f"  [{status}] {msg}")

    # ── 内存往返：一张两列（一数值一描述）两行的 config-backed 表 ──
    def fresh():
        obj = {
            "configFile": "weapons.json",
            "columns": [
                {"key": "name", "label": "名称", "type": "text", "primary": True},
                {"key": "damageMin", "label": "伤害下限", "type": "number", "configKey": "DamageMin"},
                {"key": "twoHanded", "label": "双手", "type": "bool", "configKey": "TwoHanded"},
                {"key": "description", "label": "简介", "type": "longtext"},
                {"key": "_id", "label": "内部id", "internal": True},
                {"key": "_configId", "label": "config键", "internal": True},
            ],
            "rows": [
                {"name": "匕首", "damageMin": 0, "twoHanded": False, "description": "旧文案", "_id": "Dagger", "_configId": "dagger"},
                {"name": "孤儿", "damageMin": 0, "twoHanded": False, "description": "", "_id": "Orphan", "_configId": "not_in_config"},
            ],
        }
        return obj
    cfg = {"dagger": {"DamageMin": 7, "TwoHanded": True, "Description": "config里的文案"}}

    # config → wiki：数值被拉成 config 值，描述保留 wiki 原值，未匹配行跳过，盖上版本。
    obj = fresh()
    changed = reconcile_from_config(obj, cfg)
    check(changed, "reconcile 报告有改动")
    check(obj["rows"][0]["damageMin"] == 7, "config→wiki：damageMin 0→7")
    check(obj["rows"][0]["twoHanded"] is True, "config→wiki：twoHanded False→True")
    check(obj["rows"][0]["description"] == "旧文案", "config→wiki：描述保留 wiki 原值（不被 config 覆盖）")
    check(obj["rows"][1]["damageMin"] == 0, "config→wiki：_configId 不在 config 的行不动")
    check(obj.get("_configVersion") == _config_version(cfg), "盖上了正确的 _configVersion")
    check(reconcile_from_config(fresh_reconciled(cfg), cfg) is False, "已对齐后再 reconcile 无改动（幂等）")

    # wiki → config：改一个数值 → pending 精确到该字段；无改动 → pending 空。
    obj = fresh()
    reconcile_from_config(obj, cfg)         # 先对齐（模拟页面加载拿到的 obj）
    check(plan_projection(obj, cfg)["pending"] == [], "对齐后 PUT：pending 为空（无副作用）")
    obj["rows"][0]["damageMin"] = 9         # 用户改数值
    obj["rows"][0]["description"] = "用户新写的文案"  # 用户改描述（不该进 config）
    plan = plan_projection(obj, cfg)
    check(plan["pending"] == [("dagger", "DamageMin", 9)], f"wiki→config：pending 精确={plan['pending']}")
    check(not plan["conflict"], "版本一致 ⇒ 无冲突")
    import copy
    cfg2 = copy.deepcopy(cfg)   # 手动应用 pending 验证语义（不落盘，apply_projection 的纯逻辑等价）
    for cid, field, val in plan["pending"]:
        cfg2[cid][field] = val
    check(cfg2["dagger"]["DamageMin"] == 9, "apply：config DamageMin 7→9")
    check(cfg2["dagger"]["Description"] == "config里的文案", "apply：描述字段永不被写")

    # 乐观锁：base-version 陈旧 + 有 config-backed 改动 ⇒ conflict。
    obj = fresh()
    reconcile_from_config(obj, cfg)
    obj["rows"][0]["damageMin"] = 5
    obj["_configVersion"] = "staleeeeeeee"
    check(plan_projection(obj, cfg)["conflict"] is True, "乐观锁：陈旧版本+改数值 ⇒ 409 冲突")
    # 只改描述（无 config-backed 改动）即便版本陈旧也不冲突。
    obj2 = fresh()
    reconcile_from_config(obj2, cfg)
    obj2["rows"][0]["description"] = "只改文案"
    obj2["_configVersion"] = "staleeeeeeee"
    p2 = plan_projection(obj2, cfg)
    check(p2["pending"] == [] and not p2["conflict"], "只改描述：无 pending、不 409（描述不碰 config）")

    # JSONC / 缺失 config ⇒ unsupported，不投影。
    obj = fresh()
    obj["rows"][0]["damageMin"] = 3
    p3 = plan_projection(obj, None)
    check(p3["unsupported"] and p3["pending"] == [], "config 缺失/JSONC ⇒ unsupported，不投影")

    # 非 config-backed 表：mapping 空、plan 空、reconcile 不动。
    plain = {"columns": [{"key": "x", "type": "text"}], "rows": [{"x": "y", "_id": "A"}]}
    check(_config_mapping(plain) == (None, {}), "非 config-backed 表：mapping 为空")
    check(plan_projection(plain, cfg)["pending"] == [], "非 config-backed 表：无投影")
    check(reconcile_from_config(plain, cfg) is False, "非 config-backed 表：reconcile 不动")

    # ── 真文件冒烟（抽取器已带 configFile 时才跑，否则跳过）──
    for name in ("weapons", "armor"):
        path = DATA_DIR / f"{name}.json"
        if not path.exists():
            continue
        disk = json.loads(path.read_text(encoding="utf-8"))
        cf, col_map = _config_mapping(disk)
        if not cf:
            print(f"  [跳过] {name}.json 尚无 configFile（抽取器未重跑）")
            continue
        real_cfg = _load_config_dict(cf)
        check(real_cfg is not None, f"真文件：{cf} 可解析为纯 JSON")
        if real_cfg is None:
            continue
        reconcile_from_config(disk, real_cfg)
        # 抽一行核对：wiki 单元格 == config 字段值（config→wiki 真的对齐了）。
        sample = next((r for r in disk["rows"] if r.get("_configId") in real_cfg), None)
        if sample is not None:
            entry = real_cfg[sample["_configId"]]
            mism = [(wk, disk_col_val, entry.get(field))
                    for wk, field in col_map.items()
                    if (disk_col_val := sample.get(wk)) != entry.get(field) and field in entry]
            check(not mism, f"真文件：{name}.json 行 {sample['_id']} 各 config-backed 单元格 == config（{cf}）")
        check(plan_projection(disk, real_cfg)["pending"] == [],
              f"真文件：{name}.json 对齐后 PUT 无副作用（pending 空）")

    print("  ——", "全部通过" if ok else "有失败", "——")
    return ok


def fresh_reconciled(cfg):
    """给幂等自测用的、已按 cfg 对齐过一次的干净 obj。"""
    obj = {
        "configFile": "weapons.json",
        "columns": [
            {"key": "damageMin", "type": "number", "configKey": "DamageMin"},
            {"key": "_configId", "internal": True},
        ],
        "rows": [{"damageMin": 7, "_id": "Dagger", "_configId": "dagger"}],
    }
    reconcile_from_config(obj, cfg)
    return obj


def main() -> int:
    ap = argparse.ArgumentParser(description="Dead Signal 本地 wiki 服务（可编辑数值表）")
    ap.add_argument("--port", type=int, default=8787, help="端口（默认 8787）")
    ap.add_argument("--host", default="127.0.0.1", help="监听地址（默认只听本机）")
    ap.add_argument("--selftest", action="store_true",
                    help="跑 wiki↔config 双向联动自测后退出（不起服务）")
    ap.add_argument("--reconcile", action="store_true",
                    help="按 config 重算一遍所有 config-backed 展示表（config→wiki）后退出，不起服务")
    args = ap.parse_args()

    if args.selftest:
        print("wiki ↔ config 双向联动自测：")
        return 0 if _selftest() else 1

    if not (WIKI_DIR / "index.html").exists():
        print(f"找不到 {WIKI_DIR / 'index.html'}", file=sys.stderr)
        return 1
    if not (DATA_DIR / "index.json").exists():
        print("data/index.json 不存在——先跑一次抽取器：", file=sys.stderr)
        print("  export DOTNET_ROOT=$HOME/.dotnet && ~/.dotnet/dotnet run --project tools/WikiExtract", file=sys.stderr)
        return 1

    if args.reconcile:
        reconcile_all()
        regenerate_bundle()
        print("已按 config 重算 config-backed 展示表（config→wiki）。")
        return 0

    reconcile_all()      # 起服务前先把 config 值拉进展示表（config→wiki）
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
