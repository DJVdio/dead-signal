# 物品图标素材出处 / Item Icon Credits

本目录下的全部物品图标（32×32 PNG）都由 **game-icons.net** 的矢量图标渲染而来。

- **来源**：https://game-icons.net/ （仓库镜像：https://github.com/game-icons/icons ）
- **授权 / License**：**CC BY 3.0**（Creative Commons Attribution 3.0 Unported）
  —— 可商用、可修改、可再分发，**但必须署名**。本文件即为署名。
  （仓库内少量图标为 CC0；本项目取用的图标一律按更严的 CC-BY 3.0 对待。）
- **修改说明**（CC-BY 要求标明改动）：原图为 512×512 SVG（黑底白图形）。我们剥掉黑色底板，
  缩放到 32×32，把 alpha 阈值化成硬边（1-bit，得到像素风轮廓），并统一填成米白 `#E8E4D8`。
  渲染脚本：`tools/icons/build_icons.sh`。
- **取用日期**：2026-07-13。

## 署名 / Attribution

Icons made by **Lorc**, **Delapouite**, **Skoll**, **Carl Olsen**, **John Colburn**,
**Willdabeast**, **Lucas** (lucasms), **Irongamer**, **Sbed** and other contributors,
available on https://game-icons.net — licensed under CC BY 3.0.

完整贡献者名单见 https://github.com/game-icons/icons/blob/master/license.txt 。
每一张图标具体取自哪位作者的哪张原图，记录在 `godot/scripts/ItemIcons.cs` 映射表的 `Source` 字段
（形如 `lorc/plain-dagger`，即 game-icons 仓库中的 `lorc/plain-dagger.svg`）。

## 怎么补图标

1. 在 `godot/scripts/ItemIcons.cs` 的映射表里给物品加一行（引用键 → 分区/slug/素材出处）。
2. 跑 `tools/icons/build_icons.sh`（默认只补缺；`--all` 全量重建）。
3. PNG 落在 `godot/assets/items/<分区>/<slug>.png`，UI 侧无需改代码——`ItemIconTextures` 按路径自动加载。

映射表当前登记 180 个稳定图标 slug，PNG 已于 2026-07-21 全量补齐；测试会逐张验证文件存在且为
32×32。未来新增物品若尚未生成 PNG，运行时仍会安全显示 `placeholder.png`，但资源完整性测试会明确报出缺图路径。
