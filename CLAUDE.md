# Dead Signal — Claude 工作约定

## 项目定位

中短流程剧情向丧尸生存经营游戏（对标《这是我的战争》），俯视角 2D 像素风。个人项目，不考虑发行。
**单一事实源：`docs/superpowers/specs/2026-07-04-dead-signal-design.md`** —— 所有玩法/战斗规则以它为准；规则变更必须同步回该文档（通常只动 §5/§6）。

## 环境与命令

- .NET 8 SDK 装在用户目录，**不在 PATH**：一律用 `~/.dotnet/dotnet`，并 `export DOTNET_ROOT=$HOME/.dotnet`
- Godot 4.7 .NET 版：`/Applications/Godot_mono.app/Contents/MacOS/Godot`
  - headless 跑 C# 必须 `export PATH="$HOME/.dotnet:$PATH"`（只设 DOTNET_ROOT 会报 dotnet: command not found）
- 构建：`~/.dotnet/dotnet build DeadSignal.sln -c Release`（根 sln 不含 godot 工程，另跑 `build godot/DeadSignal.Godot.csproj`）
- 测试：`~/.dotnet/dotnet test`（全绿是硬门禁，改战斗规则必须先红后绿）
- 运行游戏：`/Applications/Godot_mono.app/Contents/MacOS/Godot --path godot`（加 `-e` 进编辑器）
- 模拟器：`~/.dotnet/dotnet run --project src/DeadSignal.Sim`（无参=聚合蒙特卡洛；`duel [路径]`=逐回合对决战报）

## 架构

- `src/DeadSignal.Combat`：战斗规则引擎，**零依赖纯 C# 类库**，Godot 只做消费方。空间问题（弹道飞行/碰撞）归 Godot 实时层，引擎只出纯函数（如 Ballistics 锥形采样）。引擎已覆盖：部位血量/切除、护甲按部位覆盖+三段判定、持握 GripMode（单/双手/双持）、远程射程+距离衰减、贴脸枪托、攻击冷却状态机、负重惩罚。
- **纯逻辑外挂在 godot 消费层**：营地/装备/制作/守卫等子系统的规则写成 `godot/scripts` 里**不引 Godot 类型**的文件，以 **Link 方式编进 `DeadSignal.Combat.Tests`** 单测（先红后绿），空间执行（寻路/碰撞/伤害施加）才落 Godot 运行时层。这批纯逻辑含：装备 11 槽 `ApparelSlots`、左右手持械 `WeaponLoadout`、材料 `Materials`、技能 `SkillSet`、配方/工作台/制作 `Recipe`·`Workbench`·`CraftingLogic`、武器改装 `WeaponMod`、三方阵营+敌对矩阵 `Factions`、袭营破防 `BreachLogic`、可破坏结构 `CampStructure`、库存 `InventoryStore`·`Item`、饥饿 `HungerState`、剧情态 `StoryFlags`·`ChristineRequestLogic`·`GoldfingerDiscovery`、全灭判定 `GameOverCondition` 等。判定与结算走纯函数（如 `CraftingLogic` 出「能不能做/扣什么产什么」，由调用方去 `InventoryStore` 实扣实产）。
- 随机必须走可注入的 `IRandomSource`（测试用 SequenceRandomSource 复现）。
- 数据驱动：武器/护甲/部位/配置是数据（如 godot/data/daynight.json），代码只写规则。
- 数值原则：具体数值皆"拟定待调"，用 Sim 拉表校准方向；规则形态才需要用户拍板。

## Git 纪律（重要）

- 本仓库用私人账号：local 已配 `DJVdio <126043810+DJVdio@users.noreply.github.com>`。**严禁改全局 git 配置**（公司工作用别的身份）。
- `credential.helper` 仓库级置空是刻意的（屏蔽系统 osxkeychain 旧凭证），不要恢复。
- remote URL 内嵌 PAT（待办：换 fine-grained/洗掉）。push 前 fetch，禁 force push。
- commit：conventional commits 中文，结尾 `Co-Authored-By` 按会话规范。

## 用户协作偏好

- 战斗/玩法规则含糊处**必须上抛询问用户**，不许自行引申（转述口径用原话，引申要标注"待确认"）。
- 切除率高致残、结局黑暗向等"狠辣"设定是**有意为之**，不要当平衡问题"修复"。
- 待办清单在 `docs/TODO.md`（完成一项删一项）。
