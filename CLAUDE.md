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

- `src/DeadSignal.Combat`：战斗规则引擎，**零依赖纯 C# 类库**，Godot 只做消费方。空间问题（弹道飞行/碰撞/噪音广播/门实体）归 Godot 实时层，引擎只出纯函数（如 Ballistics 锥形采样）。引擎已覆盖：部位血量/切除、护甲按部位覆盖+三段判定、**护甲后的乘算伤害减免层**（`CombatResolver.Resolve(…, incomingDamageReduction)`，默认 0＝零回归）、持握 GripMode（单/双手/双持）、远程射程+距离衰减、贴脸枪托、攻击冷却状态机、负重惩罚、**多弹丸齐射** `Weapon.PelletCount`·`CombatResolver.ResolveVolley`（霰弹枪一发 8 颗弹丸，**逐颗独立选部位、独立三段判定**）、**弹药** `Ammo`·`AmmoLogic`（短/中/长子弹＋鹿弹＋箭，`AmmoPerAttack` 由连发数派生、`PelletCount` 不参与相乘；打空则走 `MeleeProfile` 降级成枪托近战）、**弓弩** `Archery`（5 弓＋3 弩＋4 种箭；`Archery.Combine(弓,箭)` 纯函数——**箭反过来改写弓的伤害/穿透/射程/冷却/散布**；箭矢回收 25%，读《弓与箭之道》后 50%）、武器噪音半径 `Weapon.NoiseRadius`（判定在消费层 `NoiseLogic`）、**丧尸着装** `ZombieOutfit`（丧尸穿生前的日常着装；**精英丧尸＝authored 具名预设，`Weight=0` 永不进随机池**）。
- **纯逻辑外挂在 godot 消费层**：营地/装备/制作/守卫等子系统的规则写成 `godot/scripts` 里**不引 Godot 类型**的文件，以 **Link 方式编进 `DeadSignal.Combat.Tests`** 单测（先红后绿），空间执行（寻路/碰撞/伤害施加/导航重烘焙）才落 Godot 运行时层。这批纯逻辑含：装备 11 槽 `ApparelSlots`（**成对装备＝一件占一槽、两件才护全，卸下按槽不按名**）、左右手持械 `WeaponLoadout`、材料 `Materials`（纺织物只有单一「布」，破布已合并退役）、配方/工作台/制作 `Recipe`·`Workbench`·`CraftingLogic`、生产工时化 `CraftingJob`、武器改装 `WeaponMod`、三方阵营+敌对矩阵 `Factions`、袭营破防 `BreachLogic`、可破坏结构 `CampStructure`、**噪音** `NoiseLogic`、**弹药源** `AmmoSource`、**门** `DoorLogic`、库存 `InventoryStore`·`Item`、**物品重量单一登记入口** `ItemDef`·`ItemRegistry`（`ItemDef.cs`，武器/材料/护甲三张重量字典合一按类别分区；护甲重量投影自引擎 `ArmorTable.Weight` 不复制数值，`ItemWeights` 三字段降为同实例薄别名；护甲登记焊死反射护栏防漏登记）、白银分制 `Silver`、饥饿 `HungerState`、感染竞速 `InfectionCourseLogic`、剧情态 `StoryFlags`·`ChristineRequestLogic`·`GoldfingerDiscovery`、电台主线 `RadioMainline`、叙事调查点 `NarrativeSpot`、全灭判定 `GameOverCondition`、尸潮时限 `HordeTimeline`·`LookoutSighting`、光照与锥形视野 `VisionLogic`·`VisionField`·`LightField`·`HeldLightState`·`LightSource`、夜防对抗+双班 `NightWatchContest`·`ShiftSchedule`、authored 专属效果与背景 `SurvivorPerks`·`SurvivorBackstory`、道格与布鲁斯 `DougBruceBond`·`DogApparel`·`DogHungerState`、神秘商人 `MerchantTrade`·`MerchantLineage`·`MerchantSchedule` 等。判定与结算走纯函数（如 `CraftingLogic` 出「能不能做/扣什么产什么」，由调用方去 `InventoryStore` 实扣实产）。
- **能力由 authored 专属效果 + 读过的书 + 装备效果通路承载**：通用技能系统已删除，配方/撬锁一类门槛只看 工具/书/材料（`Recipe.cs`、`DoorLogic`）。**穿戴品现在也能给能力加成**——`ApparelDef.Effects`·`EquipEffect`（`godot/scripts/ApparelSlots.cs`，如平光眼镜挂 +5% 阅读速度），效果**只挂消费层、绝不进零依赖战斗引擎 `ArmorLayer`**，由 `ApparelEffectMultiplier` 从**真实穿戴品名**乘算汇总（禁手写常数，否则登记成摆设＝静默失效）。
- 随机必须走可注入的 `IRandomSource`（测试用 SequenceRandomSource 复现）。
- 数据驱动：武器/护甲/部位/配置是数据（如 godot/data/daynight.json），代码只写规则。

## 数值与仿真纪律

- **百分比加成一律乘算，禁止加算**（用户拍板的通则）：缺两指的山姆是 `0.86 × 1.03 = 0.8858`，不是加算的 `0.89`——加算会让**没有手的人**（操作能力 0）凭空获得 3% 操作能力。新写加成时一律连乘；历史加算残留清单见 journal `[HANDOFF] impl-sam-perk`。
- **既有 Sim 基线零漂移是新机制的硬要求**：加字段/加系统不得改变既有武器×护甲的 Sim 输出。首选**结构性证明**——新字段不被 `CombatResolver`/`Duel`/`Ballistics`/`Arena` 引用 ⇒ Sim 的结算路径根本读不到它（再配一条"该字段不参与结算"的单测护栏）；辅以受控 A/B 实跑（同一棵树剥掉新字段赋值两跑，输出逐字节/MD5 一致）。新武器一律**追加末尾不插队**，否则打乱随机流。
- 🔴 **胜率不是成本 —— 做经济/掉落分析前必读**（用户原话：「一趟白捡……什么意思，**战斗难道不是成本吗**」）。胜率只说"你能不能站着走出这一场"，它**一个字都没说**你为此付了什么：打光的弹药、要愈合 **7 昼夜**的骨折（占床、不能干活、不能站岗）、**永久**断掉的手、感染竞速、手术耗材与失败率、不可复活的死人。
  - **绝不要用「胜率 × 敌人数」估收益**。反例（`docs/research/2026-07-14-combat-cost.md`，Sim `cost` 模式，[T58/T59] 重跑）：**持棍棒劫掠者胜率 78.7%，但 74.3% 的胜场留下骨折**（每处卧床 7 昼夜、占床、不能干活、不能站岗）；**持破甲锤劫掠者胜率只有 40.0%，且 85.9% 骨折**。反过来**丧尸 1v1 胜率 100%**，却仍有 25.9% 的胜场毫发无伤——**另外 74% 的场次你是带着伤口回来的，每一道都要一台手术。** 按胜率排和按成本排，顺序完全不同。
  - 🔴 **围攻不能拿 1v1 胜率去想（平方律，`docs/research/2026-07-14-lanchester.md`）**：丧尸 **1 只 100% → 2 只 84.4% → 3 只 24.1% → 4 只 1.3% → 5 只 0%**。**是断崖，不是斜坡。**「打赢 8 个劫掠者白捡 8 把武器」这个场景根本不存在。
  - 正确框架：**「这场仗要拿多少伤病和人命去换」**。数在 `docs/research/2026-07-14-combat-cost.md`，harness = `src/DeadSignal.Sim/CombatCostCalibration.cs`。
- **Sim 测不了什么，用它的数字前必须知道**：`Duel` 是 **1v1、无距离、无走位、无多目标**的引擎级对决，也**不建模噪音与弹药**。所以霰弹枪的短射程代价（射程 90 + 全表最重距离衰减）和"一发散射同时打中多只丧尸"的清群优势，Sim **一个都测不出**（它白送贴脸）；枪械胜率（如步枪 vs 长剑手·中甲 98.9%）只能读作**「无限弹药 / 贴脸 / 单挑」下的杀伤力天花板**，不是"能打多久"——枪的真实战力由弹药供给决定，而供给在 loot/配方数值里，不在 Sim 里。空间侧机制（噪音/门/多目标/潜行）只能实机校准。
- 数值原则：具体数值皆"拟定待调"，用 Sim 拉表校准方向；规则形态才需要用户拍板。

## Git 纪律（重要）

- 本仓库用私人账号：local 已配 `DJVdio <126043810+DJVdio@users.noreply.github.com>`。**严禁改全局 git 配置**（公司工作用别的身份）。
- `credential.helper` 仓库级置空是刻意的（屏蔽系统 osxkeychain 旧凭证），不要恢复。
- remote URL 内嵌 PAT（待办：换 fine-grained/洗掉）。push 前 fetch，禁 force push。
- commit：conventional commits 中文，结尾 `Co-Authored-By` 按会话规范。

## 用户协作偏好

- 战斗/玩法规则含糊处**必须上抛询问用户**，不许自行引申（转述口径用原话，引申要标注"待确认"）。
- 切除率高致残、结局黑暗向等"狠辣"设定是**有意为之**，不要当平衡问题"修复"。
- 剧情/角色关系/性格/精英丧尸预设是**用户手写的 authored 内容**，代码只做"按条件播放"的框架，不做程序化引申。
- **README 禁剧透**：对外只讲工程与机制框架，结局/反转/地点的剧情作用一律只留设计文档。
- 数值表已从 xlsx 迁到本地 wiki（`docs/wiki`）：**xlsx 已删除，wiki 是唯一设计源、代码向它看齐**。给用户看的表一律**中文名主键、人话说明，不出现类名/英文 id/引擎术语**（内部 id 收进置灰"勿改"列）。
- 待办清单在 `docs/TODO.md`（完成一项删一项）。
