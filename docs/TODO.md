# 待办（完成一项删一项）

1. **Godot 场景接入新战斗系统**（纯消费侧，verify 已查明缺口）：godot/scripts/CombatData.cs 仍用旧扁平部位表（MaxHp=0 绕过新机制），应换 `HumanBody.Parts()` + `Body` 状态；手枪未标 IsRanged/误差角/攻速，弹道从直线占位换 `Ballistics` 锥形采样（Actor.cs 有 TODO 锚点）。
2. **数值平衡观察**（战报得出，仅调参不改规则）：纯锤手 vs 重甲长剑三场全败（锤攻速 1.8s 过慢）——"钝器克甲不克人"是否需补强待定；双持手枪对无甲 0.7~5s 速杀，接入误差角弹道后复测。
3. **PAT 安全**：classic token（全仓库权限）明文存在 .git/config remote URL，建议换只授权本仓库的 fine-grained token，或洗掉 URL 中 token 改存凭证。
4. 后续开发候选（未排期）：防御战 tick 集成、昼夜循环/世界地图骨架、角色信件链路（蒂诺/克莉丝汀）。
