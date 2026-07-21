# 头像素材出处 / Portrait Credits

- **素材名**：Survivor Portraits (Post-Apocalyptic Survival)
- **来源 URL**：https://opengameart.org/content/survivor-portraits-post-apocalyptic-survival
- **直接下载**：https://opengameart.org/sites/default/files/post_apocalyptic_portraits.zip
- **作者 / Author**：见来源页署名（OpenGameArt 提交者）；如需精确署名以来源页为准。
- **授权 / License**：CC0 1.0 Universal（公共领域捐赠，无署名义务，可自由用于任何用途，含商用）。
- **下载日期**：2026-07-08（如有出入以本目录文件在 git 中的首次提交时间为准）。

## 文件清单（13 张泛用头像 + 7 张具名派生头像，365×564 RGBA PNG）

- femaleportrait1.png … femaleportrait7.png（7 张女性）
- maleportrait1.png … maleportrait6.png（6 张男性）
- `named/sam.png`、`notty.png`、`christine.png`、`rat.png`、`doug.png`、`nightingale.png`、`pete.png`：
  从项目原创正式角色动画的南向站立帧确定性裁切，加入统一暗色卡框；生成脚本为
  `tools/build_named_portraits.py`，不继承 OpenGameArt 素材内容。

## 用法说明

`godot/scripts/SurvivorCardBar.cs` 对七名具名角色优先使用专属头像，其他幸存者按 `Pawn.Id` 稳定映射到
13 张泛用头像之一（`SurvivorCardVisuals.PortraitIndexForId`）。
CC0 无署名强制要求，此文件仅为工程内溯源留痕。
