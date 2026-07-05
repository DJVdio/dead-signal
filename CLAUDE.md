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

- `src/DeadSignal.Combat`：战斗规则引擎，**零依赖纯 C# 类库**，Godot 只做消费方。空间问题（弹道飞行/碰撞）归 Godot 实时层，引擎只出纯函数（如 Ballistics 锥形采样）。
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
- 本机 Clash 代理（env 7897）会导致 git 访问 github.com 报 SSL_ERROR_SYSCALL：push/fetch 前清代理 `env -u HTTPS_PROXY -u https_proxy -u HTTP_PROXY -u http_proxy git ...`，直连是通的。
