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
from socketserver import ThreadingMixIn
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


class WikiHTTPServer(ThreadingMixIn, HTTPServer):
    """每个浏览器连接独立处理，避免一条未完成请求卡住整个 Wiki。"""

    daemon_threads = True
    allow_reuse_address = True


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
        # 一张表可能牵动多个 config 文件（弹药：ammo.json + archery.json）⇒ 按文件分别载入/投影。
        cfgs = _load_cfgs(payload)
        plan = plan_projection(payload, cfgs)
        if plan["conflict"]:
            # 某个牵动的 config 在本页加载之后被别处改过（Python/别的编辑），且本次要改它里面的值。
            # 字段级 last-write-wins + 乐观 base-version：撞了就让用户 reload，绝不闷头覆盖。
            return self._fail(409, "config json 已被其它改动更新——请刷新页面(reload)后再存，避免覆盖对方的改动。")

        try:
            # 顺序要紧：先把数值投影写回 config（cfgs 就地更新到新值），再用新 cfgs 重算版本号盖回
            # 展示 json 才落盘 —— 否则展示表的 _configVersion 仍是旧值，下次保存必假 409。
            if plan["pending"]:
                apply_projection(cfgs, plan["pending"])
            _stamp_config_version(payload, cfgs)
            _write_atomic(target, json.dumps(payload, ensure_ascii=False, indent=2) + "\n")
            regenerate_bundle()
        except OSError as e:
            return self._fail(500, f"落盘失败：{e}")

        rel = target.relative_to(REPO_ROOT)
        n_proj = sum(len(v) for v in plan["pending"].values())
        files = "，".join(sorted(plan["pending"])) if plan["pending"] else ""
        tail = f"  → 投影写回 {files}：{n_proj} 个数值" if n_proj else ""
        print(f"  [保存] {rel}  ({len(payload['rows'])} 条){tail}")
        if plan["unsupported"]:
            print(f"  [双向] ⚠ 有 config 文件缺失或坏损，部分 config-backed 改动未投影"
                  f"（仅落了 wiki 展示 json）", file=sys.stderr)
        # 回传回刷后的版本号，让前端更新内存 _configVersion，下次保存不假 409（config-backed 表才有）。
        self._ok({"ok": True, "file": str(rel), "rows": len(payload["rows"]), "projected": n_proj,
                  "configVersion": payload.get("_configVersion")})

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


def _is_config_backed(obj: dict) -> bool:
    """这张展示表是否 config-backed（有表级 configFile ⇒ 参与双向）。"""
    return isinstance(obj, dict) and isinstance(obj.get("configFile"), str) and bool(obj.get("configFile"))


def _column_specs(obj: dict):
    """把一张展示表的 config-backed 列解析成规格清单，让双向搬运零硬编码。

    每个 spec：
      · key   —— 展示列 key（如 damageMin）
      · file  —— 写回哪个 config 文件（列级 configFile 覆盖表级；一表多源靠它）。
                 **可再被行级 `_configFile` 覆盖**（单例设置表一表跨多源：同一列不同行落不同 config 文件）。
      · root  —— id-字典在 config 里的嵌套路径（点分，如 "Arrows"）；顶层 ⇒ None
      · field —— config 条目里的字段名（configKey）；标量条目 ⇒ None
      · scalar—— True ⇒ config 条目本身就是数值（Dict<id→数值>，无字段层）。
                 **单例设置对象**（扁平 {字段:值}，如 perks.json）走这条：row 的 `_configId` = 字段名 ⇒ cfg[字段名] 即值。
      · vmap  —— 枚举显示变换 {wiki值: config值}；None ⇒ 恒等
      · percent—— True ⇒ 数值百分比变换：config 存分数(0.1)、wiki 显示 ×100(10)。列级 `percentTransform`。
                 **可再被行级 `_configPercent` 追加**（单例表里同一 value 列，有的行是比例%、有的是原始计数）。
      · dict  —— True ⇒ **嵌套字典条目**：config 字段是 Dict<料key→数量>，wiki 单元格是「中文名*量、中文名*量」人话串。
                 列级 `configDict`；转换用 `dictmap`（中文名↔key）。
      · dictmap—— {中文名: config key}（configDict 列专用），双向查料名↔key。
    非 config-backed 表 / 无 config-backed 列 ⇒ []。
    """
    if not _is_config_backed(obj):
        return []
    table_file = obj["configFile"]
    specs = []
    for col in obj.get("columns", []):
        if not isinstance(col, dict):
            continue
        key = col.get("key")
        cfgkey = col.get("configKey")
        scalar = bool(col.get("configScalar"))
        # config-backed 列 = 有 configKey（普通字段/嵌套字典）或 configScalar（标量条目）
        if not key or (not cfgkey and not scalar):
            continue
        file = col.get("configFile") or table_file
        vmap = col.get("valueMap")
        dictmap = col.get("dictNameMap")
        specs.append({
            "key": key,
            "file": file,
            "root": col.get("configRoot"),
            "field": cfgkey,
            "scalar": scalar,
            "vmap": vmap if isinstance(vmap, dict) and vmap else None,
            "percent": bool(col.get("percentTransform")),
            "dict": bool(col.get("configDict")),
            "dictmap": dictmap if isinstance(dictmap, dict) else {},
        })
    return specs


def _row_file(spec: dict, row: dict) -> str:
    """这一行这一列实际写回哪个 config 文件：行级 `_configFile` 覆盖列级 spec['file']。"""
    return row.get("_configFile") or spec["file"]


def _row_root(spec: dict, row: dict):
    """这一行这一列的 id-字典嵌套路径：行级 `_configRoot` 覆盖列级 spec['root']。

    让同一列不同行落在 config 的不同嵌套子对象下（如致残 3 行落 body.json 的 Disability 段，
    而同表其它行落各自顶层单例）。同 `_row_file` 的行级覆盖范式。
    """
    return row.get("_configRoot") or spec["root"]


def _row_percent(spec: dict, row: dict) -> bool:
    """这一行这一列是否走百分比变换：列级 percentTransform 或行级 `_configPercent` 任一为真即是。"""
    return spec["percent"] or bool(row.get("_configPercent"))


def _dict_c2w(dictmap: dict, cfg_dict) -> str:
    """config 嵌套字典 {key: qty} → wiki 人话串「中文名*量、中文名*量」（按 config 字典顺序，key→名反向查）。"""
    if not isinstance(cfg_dict, dict):
        return ""
    key2name = {k: n for n, k in dictmap.items()}
    parts = []
    for k, qty in cfg_dict.items():
        name = key2name.get(k, k)   # 映射缺失 ⇒ 回退原 key（不静默丢条目）
        parts.append(f"{name}*{qty}")
    return "、".join(parts)


def _dict_w2c(dictmap: dict, wiki_str):
    """wiki 人话串「中文名*量、中文名*量」→ config 嵌套字典 {key: qty}。

    解析失败（格式坏 / 中文名不在映射 / 量非整数）⇒ 返回 None（调用方跳过投影，绝不拿半解析的脏字典覆盖 config）。
    空串 ⇒ {}（空材料）。
    """
    if not isinstance(wiki_str, str):
        return None
    s = wiki_str.strip()
    if not s:
        return {}
    out = {}
    for part in s.split("、"):
        part = part.strip()
        if not part or "*" not in part:
            return None
        name, _, qty_s = part.rpartition("*")
        name = name.strip()
        key = dictmap.get(name)
        if key is None:
            return None
        try:
            qty = int(qty_s.strip())
        except ValueError:
            return None
        out[key] = qty
    return out


def _involved_files(obj: dict):
    """这张表牵动的 config 文件集合（去重排序）。

    含列级 configFile（spec['file']）**与行级 `_configFile` 覆盖**——单例设置表一表跨多源时，
    某些源只在行里出现（如 character-stats 的商人价率行落 merchant.json），乐观锁/载入都得算上它。
    """
    specs = _column_specs(obj)
    if not specs:
        return []
    files = {s["file"] for s in specs}
    for row in obj.get("rows", []):
        rf = row.get("_configFile")
        if rf:
            files.add(rf)
    return sorted(files)


def _load_config_dict(config_file):
    """godot/data/config/<file> → dict。缺失 / 坏 json / 非对象 ⇒ None（不支持投影）。

    config-purejson 后全部 config 为纯 JSON；仍保留坏文件护栏（缺失/损坏不碰）。
    """
    if not config_file:
        return None
    target = (CONFIG_DIR / config_file).resolve()
    if not _is_within(target, CONFIG_DIR) or not target.exists():
        return None
    try:
        data = json.loads(target.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeDecodeError, OSError):
        return None
    return data if isinstance(data, dict) else None


def _load_cfgs(obj: dict) -> dict:
    """载入这张表牵动的所有 config 文件 ⇒ {file: cfg_or_None}。"""
    return {f: _load_config_dict(f) for f in _involved_files(obj)}


def _id_dict(cfg, root):
    """取 config 里的「id→条目」字典。root=None ⇒ 顶层；否则按点分路径下钻（如 "Arrows"）。取不到 ⇒ None。"""
    if cfg is None:
        return None
    node = cfg
    if root:
        for part in str(root).split("."):
            if not isinstance(node, dict) or part not in node:
                return None
            node = node[part]
    return node if isinstance(node, dict) else None


def _config_version(cfg: dict) -> str:
    """单个 config 内容的稳定短哈希。规范化序列化 ⇒ 与格式/键序无关。"""
    canon = json.dumps(cfg, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha1(canon.encode("utf-8")).hexdigest()[:12]


def _combined_version(obj: dict, cfgs: dict) -> str:
    """这张表牵动的**全部** config 文件的合并哈希（乐观锁 base-version）。

    任一牵动文件被别处改过 ⇒ 合并哈希变 ⇒ PUT 若同时有 config-backed 改动就 409。
    """
    parts = []
    for f in _involved_files(obj):
        cfg = cfgs.get(f)
        parts.append(f + ":" + (_config_version(cfg) if cfg is not None else "none"))
    return hashlib.sha1("|".join(parts).encode("utf-8")).hexdigest()[:12]


def _c2w(vmap, percent, val):
    """config 值 → wiki 显示值：先百分比 ×100（percent 列），再枚举反向查（无 vmap 恒等）。

    百分比与枚举互斥（一个是数、一个是标签），故先后顺序不影响结果。
    """
    if percent and isinstance(val, (int, float)) and not isinstance(val, bool):
        val = round(val * 100, 4)
    if not vmap:
        return val
    for wiki_val, cfg_val in vmap.items():
        if cfg_val == val:
            return wiki_val
    return val


def _w2c(vmap, percent, val):
    """wiki 显示值 → config 值：先枚举正向查，再百分比 ÷100（percent 列，圆整到 6 位抹去浮点尾巴）。"""
    if vmap:
        val = vmap.get(val, val)
    if percent and isinstance(val, (int, float)) and not isinstance(val, bool):
        val = round(val / 100.0, 6)
    return val


def reconcile_from_config(obj: dict, cfgs: dict) -> bool:
    """config → wiki：把 config-backed 单元格重算成 config 值，并盖上 _configVersion。返回是否有改动。

    · 多源：每列按自己的 configFile/configRoot 各取各的 config（cfgs = {file: cfg}）。
    · 标量条目：取 cfg[root?][cid] 本身；普通字段：取 …[cid][field]。
    · 枚举：config 值经 valueMap 反向查成 wiki 显示值。
    · 描述/flavor/备注（无 configKey/configScalar 的列）一律不碰 ⇒ 数值取 config、文案保留 wiki 原值（merge）。
    """
    specs = _column_specs(obj)
    if not specs:
        return False
    changed = False
    for row in obj.get("rows", []):
        cid = row.get("_configId")
        if not cid:
            continue
        for s in specs:
            idd = _id_dict(cfgs.get(_row_file(s, row)), _row_root(s, row))
            if idd is None or cid not in idd:
                continue
            entry = idd[cid]
            if s["scalar"]:
                cval = entry
            elif isinstance(entry, dict) and s["field"] in entry:
                cval = entry[s["field"]]
            else:
                continue
            if s["dict"]:
                wval = _dict_c2w(s["dictmap"], cval)
            else:
                wval = _c2w(s["vmap"], _row_percent(s, row), cval)
            if row.get(s["key"]) != wval:
                row[s["key"]] = wval
                changed = True
    version = _combined_version(obj, cfgs)
    if obj.get("_configVersion") != version:
        obj["_configVersion"] = version
        changed = True
    return changed


def plan_projection(payload: dict, cfgs: dict):
    """wiki → config：算出要写回各 config 文件的改动清单 + 冲突判定。

    返回 {"pending": {file: [(root, id, field, scalar, value), ...]}, "conflict": bool, "unsupported": bool}。
      · pending 只含**确实与当前 config 不同**的 config-backed 单元格（字段级最小写入，不 clobber 未改字段）。
      · conflict：本次有 config-backed 改动，且 payload 带的 base-version 与当前（合并）config 哈希不符。
      · unsupported：表 config-backed，但某个牵动 config 文件缺失/坏损 ⇒ 那部分不投影（照旧只落 wiki）。
    """
    specs = _column_specs(payload)
    if not specs:
        return {"pending": {}, "conflict": False, "unsupported": False}
    unsupported = any(cfgs.get(f) is None for f in _involved_files(payload))
    pending = {}
    for row in payload.get("rows", []):
        cid = row.get("_configId")
        if not cid:
            continue
        for s in specs:
            file = _row_file(s, row)
            root = _row_root(s, row)
            idd = _id_dict(cfgs.get(file), root)
            if idd is None or cid not in idd or s["key"] not in row:
                continue
            entry = idd[cid]
            if s["dict"]:
                desired = _dict_w2c(s["dictmap"], row[s["key"]])
                if desired is None:      # 串解析失败 ⇒ 跳过投影（不拿脏字典覆盖 config）
                    continue
            else:
                desired = _w2c(s["vmap"], _row_percent(s, row), row[s["key"]])
            if s["scalar"]:
                if idd[cid] != desired:
                    pending.setdefault(file, []).append((root, cid, None, True, desired))
            elif isinstance(entry, dict) and s["field"] in entry:
                if entry[s["field"]] != desired:
                    pending.setdefault(file, []).append((root, cid, s["field"], False, desired))
    conflict = False
    if pending:
        base = payload.get("_configVersion")
        if base is not None and base != _combined_version(payload, cfgs):
            conflict = True
    return {"pending": pending, "conflict": conflict, "unsupported": unsupported}


def _stamp_config_version(payload: dict, cfgs: dict) -> None:
    """config-backed 展示表：用（apply_projection 之后的）cfgs 重算合并版本号盖回 payload。

    保存链的收尾一步：apply_projection 已把 config 哈希从 A 推到 B，展示 json 落盘前必须带上 B，
    否则下次带 config 改动的保存会拿旧 base-version(A) 与当前 config(B) 相撞 ⇒ 假 409。
    pending 空/只改文案时 cfgs 未变 ⇒ 盖回的就是当前合并版本（让前端对齐）。非 config-backed 表不碰。
    """
    if _is_config_backed(payload):
        payload["_configVersion"] = _combined_version(payload, cfgs)


def apply_projection(cfgs: dict, pending: dict) -> None:
    """把 pending 写进各 cfg 并逐文件原子落盘（调用方须已确保 cfg 来自纯 JSON 文件）。"""
    for file, items in pending.items():
        cfg = cfgs[file]
        for root, cid, field, scalar, value in items:
            idd = _id_dict(cfg, root)
            if scalar:
                idd[cid] = value
            else:
                idd[cid][field] = value
        _write_atomic(CONFIG_DIR / file,
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
    if not _is_config_backed(obj):
        return obj
    cfgs = _load_cfgs(obj)
    if reconcile_from_config(obj, cfgs):
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
        if not _is_config_backed(obj):
            continue
        cfgs = _load_cfgs(obj)
        missing = [f for f, c in cfgs.items() if c is None]
        if missing:
            print(f"  [双向] ⚠ {path.name}：config {missing} 缺失或坏损，那部分列暂不投影",
                  file=sys.stderr)
        # 各行 _configId 是否在**任一**牵动的 id-字典里（都不在才算真未匹配）。行级 _configFile 也算进来。
        specs = _column_specs(obj)
        def _matched(row):
            cid = row["_configId"]
            for s in specs:
                idd = _id_dict(cfgs.get(_row_file(s, row)), _row_root(s, row))
                if idd is not None and cid in idd:
                    return True
            return False
        unmatched = [r.get("_id") for r in obj.get("rows", [])
                     if r.get("_configId") and not _matched(r)]
        if reconcile_from_config(obj, cfgs):
            _write_atomic(path, json.dumps(obj, ensure_ascii=False, indent=2) + "\n")
            print(f"  [双向] {path.name} ← {'，'.join(_involved_files(obj))}（已按 config 重算 config-backed 单元格）")
        if unmatched:
            print(f"  [双向] ⚠ {path.name} 有 {len(unmatched)} 行 _configId 不在任何 config（跳过）：{unmatched}",
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

    def apply_inmem(cfgs_in, pending):
        """apply_projection 的**纯内存等价**（不落盘）——自测绝不能写真 config 目录。"""
        for file, items in pending.items():
            for root, cid, field, scalar, value in items:
                idd = _id_dict(cfgs_in[file], root)
                if scalar:
                    idd[cid] = value
                else:
                    idd[cid][field] = value

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
    cfgs = {"weapons.json": cfg}

    # config → wiki：数值被拉成 config 值，描述保留 wiki 原值，未匹配行跳过，盖上版本。
    obj = fresh()
    changed = reconcile_from_config(obj, cfgs)
    check(changed, "reconcile 报告有改动")
    check(obj["rows"][0]["damageMin"] == 7, "config→wiki：damageMin 0→7")
    check(obj["rows"][0]["twoHanded"] is True, "config→wiki：twoHanded False→True")
    check(obj["rows"][0]["description"] == "旧文案", "config→wiki：描述保留 wiki 原值（不被 config 覆盖）")
    check(obj["rows"][1]["damageMin"] == 0, "config→wiki：_configId 不在 config 的行不动")
    check(obj.get("_configVersion") == _combined_version(fresh(), cfgs), "盖上了正确的 _configVersion")
    check(reconcile_from_config(fresh_reconciled(cfgs), cfgs) is False, "已对齐后再 reconcile 无改动（幂等）")

    # wiki → config：改一个数值 → pending 精确到该字段；无改动 → pending 空。
    obj = fresh()
    reconcile_from_config(obj, cfgs)         # 先对齐（模拟页面加载拿到的 obj）
    check(plan_projection(obj, cfgs)["pending"] == {}, "对齐后 PUT：pending 为空（无副作用）")
    obj["rows"][0]["damageMin"] = 9         # 用户改数值
    obj["rows"][0]["description"] = "用户新写的文案"  # 用户改描述（不该进 config）
    plan = plan_projection(obj, cfgs)
    check(plan["pending"] == {"weapons.json": [(None, "dagger", "DamageMin", False, 9)]},
          f"wiki→config：pending 精确={plan['pending']}")
    check(not plan["conflict"], "版本一致 ⇒ 无冲突")
    import copy
    cfgs2 = copy.deepcopy(cfgs)   # 手动应用 pending 验证语义（不落盘，apply_projection 的纯逻辑等价）
    for _root, cid, field, _scalar, val in plan["pending"]["weapons.json"]:
        cfgs2["weapons.json"][cid][field] = val
    check(cfgs2["weapons.json"]["dagger"]["DamageMin"] == 9, "apply：config DamageMin 7→9")
    check(cfgs2["weapons.json"]["dagger"]["Description"] == "config里的文案", "apply：描述字段永不被写")

    # 乐观锁：base-version 陈旧 + 有 config-backed 改动 ⇒ conflict。
    obj = fresh()
    reconcile_from_config(obj, cfgs)
    obj["rows"][0]["damageMin"] = 5
    obj["_configVersion"] = "staleeeeeeee"
    check(plan_projection(obj, cfgs)["conflict"] is True, "乐观锁：陈旧版本+改数值 ⇒ 409 冲突")
    # 只改描述（无 config-backed 改动）即便版本陈旧也不冲突。
    obj2 = fresh()
    reconcile_from_config(obj2, cfgs)
    obj2["rows"][0]["description"] = "只改文案"
    obj2["_configVersion"] = "staleeeeeeee"
    p2 = plan_projection(obj2, cfgs)
    check(p2["pending"] == {} and not p2["conflict"], "只改描述：无 pending、不 409（描述不碰 config）")

    # 缺失/坏损 config ⇒ unsupported，不投影。
    obj = fresh()
    obj["rows"][0]["damageMin"] = 3
    p3 = plan_projection(obj, {"weapons.json": None})
    check(p3["unsupported"] and p3["pending"] == {}, "config 缺失/坏损 ⇒ unsupported，不投影")

    # 非 config-backed 表：specs 空、plan 空、reconcile 不动。
    plain = {"columns": [{"key": "x", "type": "text"}], "rows": [{"x": "y", "_id": "A"}]}
    check(_column_specs(plain) == [], "非 config-backed 表：specs 为空")
    check(plan_projection(plain, cfgs)["pending"] == {}, "非 config-backed 表：无投影")
    check(reconcile_from_config(plain, cfgs) is False, "非 config-backed 表：reconcile 不动")

    # ── 新能力①：枚举 value-map（伤害类型 锐/钝 ↔ Sharp/Blunt）──
    vm_obj = {
        "configFile": "weapons.json",
        "columns": [
            {"key": "damageType", "type": "chip", "configKey": "DamageType",
             "valueMap": {"锐": "Sharp", "钝": "Blunt"}},
            {"key": "_configId", "internal": True},
        ],
        "rows": [{"damageType": "钝", "_id": "Dagger", "_configId": "dagger"}],
    }
    vm_cfgs = {"weapons.json": {"dagger": {"DamageType": "Sharp"}}}
    reconcile_from_config(vm_obj, vm_cfgs)
    check(vm_obj["rows"][0]["damageType"] == "锐", "value-map config→wiki：Sharp→锐")
    check(plan_projection(vm_obj, vm_cfgs)["pending"] == {}, "value-map 对齐后无副作用")
    vm_obj["rows"][0]["damageType"] = "钝"   # 用户改成钝
    check(plan_projection(vm_obj, vm_cfgs)["pending"] == {"weapons.json": [(None, "dagger", "DamageType", False, "Blunt")]},
          "value-map wiki→config：钝→Blunt")

    # ── 新能力②：标量条目（materials.json 是 {id: 数值}）──
    sc_obj = {
        "configFile": "materials.json",
        "columns": [
            {"key": "weight", "type": "number", "configScalar": True},
            {"key": "_configId", "internal": True},
        ],
        "rows": [{"weight": 0, "_id": "stone", "_configId": "stone"}],
    }
    sc_cfgs = {"materials.json": {"stone": 3, "wood": 1}}
    reconcile_from_config(sc_obj, sc_cfgs)
    check(sc_obj["rows"][0]["weight"] == 3, "标量条目 config→wiki：weight 0→3")
    check(plan_projection(sc_obj, sc_cfgs)["pending"] == {}, "标量条目对齐后无副作用")
    sc_obj["rows"][0]["weight"] = 5
    sc_plan = plan_projection(sc_obj, sc_cfgs)
    check(sc_plan["pending"] == {"materials.json": [(None, "stone", None, True, 5)]}, "标量条目 wiki→config：3→5")
    sc_apply = copy.deepcopy(sc_cfgs)
    apply_inmem(sc_apply, sc_plan["pending"])
    check(sc_apply["materials.json"]["stone"] == 5 and sc_apply["materials.json"]["wood"] == 1,
          "标量条目 apply：stone 3→5，wood 不动")

    # ── 新能力③：一表多源 + 嵌套根（configRoot）──
    ms_obj = {
        "configFile": "ammo.json",
        "columns": [
            {"key": "yieldPerPart", "type": "number", "configKey": "YieldPerBulletPart"},
            {"key": "damageMult", "type": "number", "configKey": "DamageMult",
             "configFile": "archery.json", "configRoot": "Arrows"},
            {"key": "_configId", "internal": True},
        ],
        "rows": [
            {"yieldPerPart": 0, "damageMult": 0, "_id": "ammo_short", "_configId": "ammo_short"},
            {"yieldPerPart": 0, "damageMult": 0, "_id": "ammo_arrow_heavy", "_configId": "ammo_arrow_heavy"},
        ],
    }
    ms_cfgs = {
        "ammo.json": {"ammo_short": {"YieldPerBulletPart": 8}},
        "archery.json": {"MaxPenetration": 0.95, "Arrows": {"ammo_arrow_heavy": {"DamageMult": 1.25}}},
    }
    check(_involved_files(ms_obj) == ["ammo.json", "archery.json"], "多源：牵动两个 config 文件")
    reconcile_from_config(ms_obj, ms_cfgs)
    check(ms_obj["rows"][0]["yieldPerPart"] == 8, "多源 config→wiki：子弹 yieldPerPart←ammo.json")
    check(ms_obj["rows"][1]["damageMult"] == 1.25, "多源+嵌套根 config→wiki：箭 damageMult←archery.json/Arrows")
    check(ms_obj["rows"][0]["damageMult"] == 0, "多源：子弹行不在 Arrows ⇒ damageMult 不动")
    check(plan_projection(ms_obj, ms_cfgs)["pending"] == {}, "多源对齐后无副作用")
    ms_obj["rows"][0]["yieldPerPart"] = 10       # 改子弹（→ ammo.json）
    ms_obj["rows"][1]["damageMult"] = 1.5        # 改箭（→ archery.json/Arrows）
    ms_plan = plan_projection(ms_obj, ms_cfgs)
    check(ms_plan["pending"].get("ammo.json") == [(None, "ammo_short", "YieldPerBulletPart", False, 10)],
          "多源 wiki→config：子弹改动落 ammo.json")
    check(ms_plan["pending"].get("archery.json") == [("Arrows", "ammo_arrow_heavy", "DamageMult", False, 1.5)],
          "多源+嵌套根 wiki→config：箭改动落 archery.json/Arrows")
    ms_apply = copy.deepcopy(ms_cfgs)
    apply_inmem(ms_apply, ms_plan["pending"])
    check(ms_apply["ammo.json"]["ammo_short"]["YieldPerBulletPart"] == 10, "多源 apply：ammo.json 落 10")
    check(ms_apply["archery.json"]["Arrows"]["ammo_arrow_heavy"]["DamageMult"] == 1.5, "多源 apply：archery.json/Arrows 落 1.5")
    check(ms_apply["archery.json"]["MaxPenetration"] == 0.95, "多源 apply：archery.json 顶层设置不动")
    # 多源乐观锁：archery.json 被别处改过（合并哈希变）⇒ 改箭必 409。
    ms_stale = copy.deepcopy(ms_obj)
    reconcile_from_config(ms_stale, ms_cfgs)     # 拿到当前合并版本
    ms_cfgs_moved = copy.deepcopy(ms_cfgs)
    ms_cfgs_moved["archery.json"]["MaxPenetration"] = 0.90   # 别处改了 archery.json
    ms_stale["rows"][1]["damageMult"] = 2.0
    check(plan_projection(ms_stale, ms_cfgs_moved)["conflict"] is True, "多源乐观锁：任一牵动 config 被改 ⇒ 409")

    # ── 新能力④：单例设置对象 + 行级 configFile + percent 变换（character-stats 那类表）──
    # config 是扁平 {字段:值} 单例对象（perks.json/merchant.json），value 列 configScalar，
    # 每行 _configId=字段名；比例行 value 存 ×100（config 存分数），行级 _configPercent 标；
    # 商人价率行落 merchant.json，行级 _configFile 覆盖表级 perks.json（一表跨两源）。
    st_obj = {
        "configFile": "perks.json",
        "columns": [
            {"key": "value", "type": "number", "configScalar": True},
            {"key": "_configId", "internal": True},
        ],
        "rows": [
            {"value": 0, "_id": "sam_l2_pop", "_configId": "SamLevel2CampPopulation"},                 # 原始计数
            {"value": 0, "_id": "sam_l1_dr", "_configId": "SamLevel1DamageReduction", "_configPercent": True},  # 比例×100
            {"value": 0, "_id": "mbuy", "_configId": "BuyRatePercent", "_configFile": "merchant.json"},  # 落别的源
            {"value": 0, "_id": "doug", "_configId": None},   # 非 config-backed 行（无 _configId）：永不触碰
        ],
    }
    st_cfgs = {
        "perks.json": {"SamLevel2CampPopulation": 3, "SamLevel1DamageReduction": 0.1},
        "merchant.json": {"BuyRatePercent": 100, "SellRatePercent": 60},
    }
    check(_involved_files(st_obj) == ["merchant.json", "perks.json"],
          "单例多源：行级 _configFile 也算进牵动文件（merchant.json 只在行里出现）")
    reconcile_from_config(st_obj, st_cfgs)
    check(st_obj["rows"][0]["value"] == 3, "单例 config→wiki：原始计数 SamLevel2CampPopulation 0→3")
    check(st_obj["rows"][1]["value"] == 10, "percent config→wiki：分数 0.1 → ×100 显示 10")
    check(st_obj["rows"][2]["value"] == 100, "行级 configFile config→wiki：商人价率←merchant.json")
    check(st_obj["rows"][3]["value"] == 0, "无 _configId 的行永不被 reconcile 触碰")
    check(plan_projection(st_obj, st_cfgs)["pending"] == {}, "单例多源+percent 对齐后无副作用")
    st_obj["rows"][0]["value"] = 5      # 改计数 → perks.json
    st_obj["rows"][1]["value"] = 12     # 改比例 12% → perks.json 存 0.12
    st_obj["rows"][2]["value"] = 90     # 改商人买入价 → merchant.json
    st_plan = plan_projection(st_obj, st_cfgs)
    check(st_plan["pending"].get("perks.json") == [(None, "SamLevel2CampPopulation", None, True, 5),
                                                    (None, "SamLevel1DamageReduction", None, True, 0.12)],
          f"单例+percent wiki→config：计数落 5、比例 12→0.12（perks.json）={st_plan['pending'].get('perks.json')}")
    check(st_plan["pending"].get("merchant.json") == [(None, "BuyRatePercent", None, True, 90)],
          "行级 configFile wiki→config：商人价率落 merchant.json")
    st_apply = copy.deepcopy(st_cfgs)
    apply_inmem(st_apply, st_plan["pending"])
    check(st_apply["perks.json"]["SamLevel2CampPopulation"] == 5, "单例 apply：perks 计数 3→5")
    check(st_apply["perks.json"]["SamLevel1DamageReduction"] == 0.12, "percent apply：perks 分数 0.1→0.12")
    check(st_apply["merchant.json"]["BuyRatePercent"] == 90, "行级 apply：merchant 价率 100→90")
    check(st_apply["merchant.json"]["SellRatePercent"] == 60, "行级 apply：未改的 SellRatePercent 不动")
    # percent 往返稳定：config→wiki→config 抹去浮点尾巴（0.15 ↔ 15，不留 0.150000…2）。
    pr_obj = {"configFile": "perks.json",
              "columns": [{"key": "value", "type": "number", "configScalar": True},
                          {"key": "_configId", "internal": True}],
              "rows": [{"value": 0, "_id": "x", "_configId": "F", "_configPercent": True}]}
    pr_cfgs = {"perks.json": {"F": 0.15}}
    reconcile_from_config(pr_obj, pr_cfgs)
    check(pr_obj["rows"][0]["value"] == 15, "percent 往返：0.15 → 15")
    check(plan_projection(pr_obj, pr_cfgs)["pending"] == {}, "percent 往返：15 回写 == 0.15（无浮点漂移伪 pending）")

    # ── 新能力⑤：嵌套字典条目（configDict）——材料成本 dict{key:qty} ↔ 人话串「中文名*量」──
    dm = {"木料": "wood", "布": "cloth", "钉子": "nails"}
    dc_obj = {
        "configFile": "recipes.json",
        "columns": [
            {"key": "materials", "type": "text", "configKey": "MaterialCosts",
             "configDict": True, "dictNameMap": dm},
            {"key": "_configId", "internal": True},
        ],
        "rows": [{"materials": "", "_id": "bone_knife", "_configId": "bone_knife"}],
    }
    dc_cfgs = {"recipes.json": {"bone_knife": {"MaterialCosts": {"wood": 2, "cloth": 1}}}}
    reconcile_from_config(dc_obj, dc_cfgs)
    check(dc_obj["rows"][0]["materials"] == "木料*2、布*1", "configDict config→wiki：{wood:2,cloth:1} → 木料*2、布*1")
    check(plan_projection(dc_obj, dc_cfgs)["pending"] == {}, "configDict 对齐后无副作用")
    dc_obj["rows"][0]["materials"] = "木料*3、钉子*4"   # 用户改材料
    dc_plan = plan_projection(dc_obj, dc_cfgs)
    check(dc_plan["pending"] == {"recipes.json": [(None, "bone_knife", "MaterialCosts", False, {"wood": 3, "nails": 4})]},
          f"configDict wiki→config：串解析回 dict={dc_plan['pending']}")
    dc_apply = copy.deepcopy(dc_cfgs)
    apply_inmem(dc_apply, dc_plan["pending"])
    check(dc_apply["recipes.json"]["bone_knife"]["MaterialCosts"] == {"wood": 3, "nails": 4},
          "configDict apply：config MaterialCosts 落 {wood:3,nails:4}")
    # 顺序无关等价：重排串不产生伪 pending（dict == 忽略键序）。
    dc_obj["rows"][0]["materials"] = "布*1、木料*2"
    reconcile_from_config(dc_obj, dc_cfgs)   # 先对齐回 config 值
    dc_obj["rows"][0]["materials"] = "布*1、木料*2"   # 与 config 同内容、不同顺序
    check(plan_projection(dc_obj, dc_cfgs)["pending"] == {}, "configDict：重排料序不产生伪 pending（dict 忽略键序）")
    # 解析失败护栏：未知料名 / 坏格式 ⇒ 不投影（不拿脏字典覆盖 config）。
    bad = copy.deepcopy(dc_obj)
    bad["rows"][0]["materials"] = "不存在的料*9"
    check(plan_projection(bad, dc_cfgs)["pending"] == {}, "configDict：未知料名 ⇒ 跳过投影（不覆盖 config）")
    bad["rows"][0]["materials"] = "木料x2"   # 缺 * 分隔
    check(plan_projection(bad, dc_cfgs)["pending"] == {}, "configDict：坏格式 ⇒ 跳过投影")
    # 空串 ⇒ 空字典。
    empty = {"configFile": "recipes.json",
             "columns": [{"key": "materials", "type": "text", "configKey": "MaterialCosts",
                          "configDict": True, "dictNameMap": dm},
                         {"key": "_configId", "internal": True}],
             "rows": [{"materials": "", "_id": "x", "_configId": "x"}]}
    empty_cfgs = {"recipes.json": {"x": {"MaterialCosts": {"wood": 1}}}}
    empty["rows"][0]["materials"] = ""
    ep = plan_projection(empty, empty_cfgs)
    check(ep["pending"] == {"recipes.json": [(None, "x", "MaterialCosts", False, {})]}, "configDict：空串 → 空字典 {}")

    # ── 新能力⑥：行级 _configRoot（同一列不同行落 config 不同嵌套子对象）——致残那类表 ──
    rr_obj = {
        "configFile": "body.json",
        "columns": [
            {"key": "value", "type": "number", "configScalar": True},
            {"key": "_configId", "internal": True},
        ],
        "rows": [
            {"value": 0, "_id": "limb", "_configId": "SingleLimbPenalty",
             "_configRoot": "Disability", "_configPercent": True},           # 落 body.json 的 Disability 子对象，分数×100
            {"value": 0, "_id": "top", "_configId": "SomeTopLevel"},           # 落 body.json 顶层（无 root）
        ],
    }
    rr_cfgs = {"body.json": {"Disability": {"SingleLimbPenalty": 0.5}, "SomeTopLevel": 3}}
    reconcile_from_config(rr_obj, rr_cfgs)
    check(rr_obj["rows"][0]["value"] == 50, "行级 configRoot config→wiki：body.json/Disability.SingleLimbPenalty 0.5→50（percent）")
    check(rr_obj["rows"][1]["value"] == 3, "行级 configRoot：无 root 的行落顶层（SomeTopLevel←3）")
    check(plan_projection(rr_obj, rr_cfgs)["pending"] == {}, "行级 configRoot 对齐后无副作用")
    rr_obj["rows"][0]["value"] = 60      # 改致残惩罚 60% → Disability.SingleLimbPenalty 0.6
    rr_plan = plan_projection(rr_obj, rr_cfgs)
    check(rr_plan["pending"] == {"body.json": [("Disability", "SingleLimbPenalty", None, True, 0.6)]},
          f"行级 configRoot wiki→config：改动带正确嵌套根 Disability={rr_plan['pending']}")
    rr_apply = copy.deepcopy(rr_cfgs)
    apply_inmem(rr_apply, rr_plan["pending"])
    check(rr_apply["body.json"]["Disability"]["SingleLimbPenalty"] == 0.6, "行级 configRoot apply：落进 Disability 嵌套子对象")
    check(rr_apply["body.json"]["SomeTopLevel"] == 3, "行级 configRoot apply：顶层字段未误动")

    # ── 回归：保存后必须回刷 _configVersion，否则下次带 config 改动的保存假 409 ──
    # 复现 bug：do_PUT 落盘展示 json 时 _configVersion 还是保存前的旧值，而 apply_projection
    # 已把 config 哈希从 A 推到 B ⇒ 下次保存 base-version(A) 与当前 config(B) 不符 ⇒ 假冲突 409。
    # 修复：apply 后用新 cfgs 重算合并版本盖回展示 json（_stamp_config_version），并在响应回传。
    save_cfg = {"dagger": {"DamageMin": 7, "TwoHanded": True, "Description": "config里的文案"}}
    save_cfgs = {"weapons.json": save_cfg}
    disp = fresh()
    reconcile_from_config(disp, save_cfgs)        # 页面加载：展示表带上当前合并版本
    v0 = disp["_configVersion"]
    disp["rows"][0]["damageMin"] = 9              # 第一次保存：改数值
    plan1 = plan_projection(disp, save_cfgs)
    check(not plan1["conflict"], "回刷回归：第一次保存版本一致不冲突")
    apply_inmem(save_cfgs, plan1["pending"])      # 投影写回 config（哈希 A→B）
    _stamp_config_version(disp, save_cfgs)        # 修复点：用 apply 后的 cfgs 重算版本盖回展示 json
    check(disp["_configVersion"] == _combined_version(disp, save_cfgs),
          "回刷回归①：保存后展示表 _configVersion == apply 后 cfgs 的合并版本")
    check(disp["_configVersion"] != v0, "回刷回归：config 改了 ⇒ 版本号确实推进")
    disp["rows"][0]["damageMin"] = 11             # 第二次保存：再改数值，带回刷后的版本
    plan2 = plan_projection(disp, save_cfgs)
    check(not plan2["conflict"], "回刷回归②：带回刷后的版本再存 ⇒ 不再假 409（bug 修复）")
    # 边界：只改文案（pending 空）也回传当前版本对齐；config-backed 表版本恒等当前合并版本。
    txt = fresh()
    reconcile_from_config(txt, save_cfgs)
    txt["rows"][0]["description"] = "只改文案"
    _stamp_config_version(txt, save_cfgs)
    check(txt["_configVersion"] == _combined_version(txt, save_cfgs),
          "回刷回归：只改文案（pending 空）版本仍对齐当前合并版本")
    # 非 config-backed 表：不凭空塞 _configVersion。
    plain_stamp = {"columns": [{"key": "x", "type": "text"}], "rows": [{"x": "y", "_id": "A"}]}
    _stamp_config_version(plain_stamp, {})
    check("_configVersion" not in plain_stamp, "回刷回归：非 config-backed 表不加 _configVersion")

    # ── 真文件冒烟（抽取器已带 configFile 时才跑，否则跳过）──
    for name in ("weapons", "armor", "materials", "recipes", "furniture", "ammo",
                 "character-stats", "global-rules", "medical", "farming"):
        path = DATA_DIR / f"{name}.json"
        if not path.exists():
            continue
        disk = json.loads(path.read_text(encoding="utf-8"))
        if not _is_config_backed(disk):
            print(f"  [跳过] {name}.json 尚无 configFile（抽取器未重跑）")
            continue
        specs = _column_specs(disk)
        real_cfgs = _load_cfgs(disk)
        for f, c in real_cfgs.items():
            check(c is not None, f"真文件：{f} 可解析为纯 JSON（{name} 表牵动）")
        if any(c is None for c in real_cfgs.values()):
            continue
        reconcile_from_config(disk, real_cfgs)
        # 抽一行核对：wiki 单元格 == config 值（config→wiki 真的对齐了，含 value-map/嵌套/标量/百分比/行级多源）。
        def _row_matches(r):
            return any(_id_dict(real_cfgs.get(_row_file(s, r)), _row_root(s, r)) is not None
                       and r.get("_configId") in _id_dict(real_cfgs.get(_row_file(s, r)), _row_root(s, r)) for s in specs)
        sample = next((r for r in disk["rows"] if r.get("_configId") and _row_matches(r)), None)
        if sample is not None:
            mism = []
            for s in specs:
                idd = _id_dict(real_cfgs.get(_row_file(s, sample)), _row_root(s, sample))
                if idd is None or sample["_configId"] not in idd:
                    continue
                entry = idd[sample["_configId"]]
                cval = entry if s["scalar"] else (entry.get(s["field"]) if isinstance(entry, dict) else None)
                if s["scalar"] or (isinstance(entry, dict) and s["field"] in entry):
                    expect = _dict_c2w(s["dictmap"], cval) if s["dict"] else _c2w(s["vmap"], _row_percent(s, sample), cval)
                    if sample.get(s["key"]) != expect:
                        mism.append((s["key"], sample.get(s["key"]), cval))
            check(not mism, f"真文件：{name}.json 行 {sample['_id']} 各 config-backed 单元格 == config（{mism if mism else 'ok'}）")
        check(plan_projection(disk, real_cfgs)["pending"] == {},
              f"真文件：{name}.json 对齐后 PUT 无副作用（pending 空）")

    print("  ——", "全部通过" if ok else "有失败", "——")
    return ok


def fresh_reconciled(cfgs):
    """给幂等自测用的、已按 cfgs 对齐过一次的干净 obj。"""
    obj = {
        "configFile": "weapons.json",
        "columns": [
            {"key": "damageMin", "type": "number", "configKey": "DamageMin"},
            {"key": "_configId", "internal": True},
        ],
        "rows": [{"damageMin": 7, "_id": "Dagger", "_configId": "dagger"}],
    }
    reconcile_from_config(obj, cfgs)
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
        httpd = WikiHTTPServer((args.host, args.port), WikiHandler)
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
