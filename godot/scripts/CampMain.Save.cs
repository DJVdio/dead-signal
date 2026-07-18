using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

// CampMain 的**存档面**（partial，独立文件——不动 CampMain.cs 那 8000 行主体）。
//
// 职责边界：
//   · 本文件：把营地运行时状态**取出来**装进 SaveData / 把 SaveData **摆回**营地。
//   · SaveMapper（纯逻辑）：每个子结构的实际字段映射，能脱 Godot 单测。
//   · SaveManager（Godot）：落盘 / 读盘 / 列表。
//   · SaveCodec（纯逻辑）：编解码 + 版本闸门。

public sealed partial class CampMain
{
    /// <summary>
    /// 战斗进行中（袭营 / 尸潮围攻 / 夜袭 / 教学关）。
    /// <para>
    /// 自动存档要看它：<b>战斗中不存</b>——不是为了防 S/L（玩家根本没有存档动作），
    /// 而是因为**存档里没有敌人**（它们是刷出来的运行时节点，不是玩家状态）。详见 <see cref="AutosaveOnPhaseChange"/>。
    /// </para>
    /// </summary>
    private bool InCombat => _raidActive || _siegeActive || _nightRaidActive || _tutorialActive;

    // ---- 导出 ----

    /// <summary>
    /// 把整个营地拍成一份存档。
    /// <para>
    /// <b>只拍"玩家做过的选择留下的痕迹"</b>。派生量一律不拍——掩体场（从建筑+沙袋重建）、导航图（从地图烘焙）、
    /// 光照场（从光源重算）、护甲数值层（从穿戴态+护甲表推）、噪音（瞬时的，压根没有持久态）。
    /// 存派生量只会制造"两份真相不一致"的 bug：改一次护甲表，旧存档里的甲就和新表打架。
    /// </para>
    /// </summary>
    public SaveData CaptureSave(string label, bool isAutosave)
    {
        var data = new SaveData
        {
            Meta = new SaveMeta
            {
                Label = label,
                SavedAtUtc = SaveManager.NowUtc(),
                Day = _clock.Day,
                Phase = _clock.CurrentPhase,
                SurvivorsAlive = _survivors.Count(p => p.Alive),
                IsAutosave = isAutosave,
            },
            World = new WorldSave
            {
                Day = _clock.Day,
                Phase = _clock.CurrentPhase,
                PhaseElapsed = _clock.PhaseElapsed,
                TravelElapsed = _clock.TravelElapsed,
                WarningFired = _clock.WarningFired,
                SpeedIndex = _clock.SpeedIndex,
            },
            // 剧情/发现/提示/搜刮完成度——半个存档都在这一个字典里（那些系统自身零字段）。
            StoryFlags = _storyFlags.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value),
            // [T57] 网状解锁的「去过哪些点」名单。**永远写一份真列表**（哪怕空的）——
            // null 是留给老档的信号（见 SaveData.VisitedDestinations：null ⇒ 老档 ⇒ 全解锁兜底）。
            VisitedDestinations = _visitedDestinations.ToList(),
            Survivors = _survivors.Select(p => p.CaptureSave()).ToList(),
            Dog = CaptureDog(),
            Corpses = CaptureCorpses(),
            Merchant = new MerchantSave
            {
                NextVisitDay = _merchantSchedule.NextVisitDay,
                Present = _merchant is not null,
                Shelf = CaptureMerchantShelf(),
            },
            Expedition = new ExpeditionSave
            {
                PendingDestination = string.IsNullOrEmpty(_pendingDestination) ? null : _pendingDestination,
                PendingTravelTime = _pendingTravelTime,
                TodaysExpeditionIds = _todaysExpeditionIds.ToList(),
                Bag = _bag?.Contents.ToList() ?? new List<LootItem>(),
                BruceAlong = _bruceExpedition,
            },
            Bonds = new BondSave
            {
                BondDaysBothAlive = _bondDaysBothAlive,
            },
        };

        data.Camp = CaptureCamp();
        return data;
    }

    private CampSave CaptureCamp()
    {
        var camp = new CampSave
        {
            Food = _resources.Food,
            Inventory = SaveMapper.ToSave(_inventory),   // 白银也在里头（一条 material item）
            WorkbenchTools = SaveMapper.ToSave(_workbench),
            CookwareInstalled = SaveMapper.ToSave(_cookStation),   // [批次21·T14] 烹饪台上装了锅没有、装了烤架没有
            ButcherKnife = _butcherStation.Slotted,   // [T67] 宰杀设施刀槽里那把刀（刀已离库，不存就凭空蒸发）
            CraftingJob = SaveMapper.ToSave(_craftingJob, _craftingJobWorker?.Id ?? -1),
            SandbagSeq = _sandbagSeq,
            TrapSeq = _trapSeq,   // [批次21·T26] 陷阱命名序号（数量不存——从 PlacedFurniture 数出来，见 SaveData.TrapSeq）
            BirdTrapSeq = _birdTrapSeq,   // [T75] 捕鸟陷阱命名序号（同圈套：数量从 PlacedFurniture 数出来，只存序号防重名）

            // 改装武器的**身份**（"步枪（刺刀型）" = 步枪 + 刺刀型）。不存这张表，读档后那把枪
            // 就是个查不到定义的空名字 —— 装不上、也没数值（见 ModdedWeaponRegistry 类注）。
            ModdedWeapons = SaveMapper.CaptureModdedWeapons(),

            // 玩家**自己摆到地上**的家具（改装台）：位置是玩家定的，不存就找不回来了。
            PlacedFurniture = CapturePlacedFurniture(),
            Structures = _structures
                .Where(s => !s.Removed)   // 已摧毁清场的不存——读档时它本来就该是个缺口
                .Select(CaptureStructure)
                .ToList(),
        };

        // 容器藏物 + 已搜/搜了一半——「逐件搜刮到一半退出」的进度天然就在这三份账里。
        SaveMapper.CaptureContainerLoot(_containerLoot, camp);

        // [批次21·impl-bedrest] 谁躺在哪张床上 + 床的命名序号（正文在 CampMain.Bedrest.cs）。
        CaptureBedSave(camp);
        return camp;
    }

    /// <summary>
    /// 玩家自己摆到地上的家具（**改装台** + 玩家造的**床** + 玩家垒的**沙袋**）。
    /// camp.json 预置的家具（工作台/柜子/开局那两张床/建图时就摆好的那几垛沙袋）**不进这张表**——
    /// 它们每次建图都在原地长出来，不需要记位置。
    /// </summary>
    // [impl-furniture-registry] ⚠️ 本方法**刻意留在注册表外**（不套 _placeables 遍历）：它的顺序敏感
    //（锚定实心设施 modbench/烹饪台/宰杀点/宰杀台优先，再单遍 _furniture 收 free 家具），
    // 套表遍历会按类型分组、改变存档 JSON 的条目顺序——结果一致但非逐字节。加新家具时**这一处仍需手动补一笔**。
    private List<PlacedFurnitureSave> CapturePlacedFurniture()
    {
        var list = new List<PlacedFurnitureSave>();
        if (_furniture.TryGetValue(WeaponModLogic.BenchFurnitureKey, out FurnitureInstance? bench))
        {
            list.Add(new PlacedFurnitureSave
            {
                Key = WeaponModLogic.BenchFurnitureKey,
                X = bench.Rect.Position.X,
                Y = bench.Rect.Position.Y,
                W = bench.Rect.Size.X,
                H = bench.Rect.Size.Y,
            });
        }

        // [批次21·T14] 烹饪台：同改装台，营地里独一份、按类型名索引，位置由玩家定 ⇒ 必须存。
        if (_furniture.TryGetValue(CookStation.PropName, out FurnitureInstance? stove))
        {
            list.Add(new PlacedFurnitureSave
            {
                Key = CookStation.PropName,
                X = stove.Rect.Position.X,
                Y = stove.Rect.Position.Y,
                W = stove.Rect.Size.X,
                H = stove.Rect.Size.Y,
            });
        }

        // [T67] 宰杀设施：营地里独一份、按类型名索引（简易宰杀点玩家自己摆的；宰杀台升级后顶替其位）。
        // 位置由玩家定 ⇒ 不存就找不回来了、HasButcherPoint/Table 读档后恒 false。两个键各存各的（同一时刻只会有其一在场）。
        foreach (string butcherKey in new[] { ButcherStation.PointFurnitureKey, ButcherStation.TableFurnitureKey })
        {
            if (_furniture.TryGetValue(butcherKey, out FurnitureInstance? bf))
            {
                list.Add(new PlacedFurnitureSave
                {
                    Key = butcherKey,
                    X = bf.Rect.Position.X,
                    Y = bf.Rect.Position.Y,
                    W = bf.Rect.Size.X,
                    H = bf.Rect.Size.Y,
                });
            }
        }

        // [批次21·impl-bedrest] 玩家造的床（"床#3" 起）。开局那两张（床#1/床#2）在 camp.json 里，建图自会长出来，
        // 故按**序号**过滤而非按名字：Key 存实例名（床位占用表 CampSave.BedOccupancy 按它对号入座）。
        //
        // [批次21·impl-modbench] 玩家垒的沙袋（"沙袋#N"）。**此前是漏的**：CampSave.Sandbags 那个字段虽然一直存在，
        // 但 CaptureCamp 从没往里填过 ⇒ 摆好的沙袋读档后**整片消失**（只剩 _sandbagSeq 这个空号）。
        // 沙袋和床、改装台一样是"位置由玩家定"的东西，走同一张 PlacedFurniture 表即可，不必再开一张。
        //
        // [批次21·T26·impl-traps] 玩家摆的陷阱（"陷阱#N"）。同沙袋/床：位置由玩家定 ⇒ 必须存。
        // ⚠️ **陷阱的"数量"不必单独存**：捕获几率按"场上第 n 个"递减（TrapLogic.ChanceOf），
        // 而 n 是**数出来的**（TrapCount 数 _furniture 里的 "陷阱#" 前缀）——把它们逐个摆回场上，
        // 数量就自动回来了。只有**命名序号** _trapSeq 得单独存（CampSave.TrapSeq），
        // 否则读档后新造的陷阱会从 #1 重新编号、与场上已有的撞名。
        foreach ((string key, FurnitureInstance f) in _furniture)
        {
            // [批次21·T25] 桌子（"桌子#N"）：同床/沙袋/陷阱——位置由玩家定 ⇒ 不存就找不回来了。
            // [T72] 菜园（"菜园#N"）：同床/沙袋/陷阱/桌子——位置由玩家定 ⇒ 必须存（生长计时器另在 StoryFlags 里，天然存）。
            if (!IsPlayerPlacedBed(key)
                && !SandbagSpec.IsSandbagFurniture(key)
                && !TrapSpec.IsTrapFurniture(key)
                && !BirdTrapSpec.IsBirdTrapFurniture(key)   // [T75] 捕鸟陷阱（"捕鸟陷阱#N"）：同圈套，位置由玩家定 ⇒ 必须存
                && !TableSpec.IsTableFurniture(key)
                && !CropPlotSpec.IsCropPlotFurniture(key))
            {
                continue;
            }
            list.Add(new PlacedFurnitureSave
            {
                Key = key,
                X = f.Rect.Position.X,
                Y = f.Rect.Position.Y,
                W = f.Rect.Size.X,
                H = f.Rect.Size.Y,
            });
        }
        return list;
    }

    /// <summary>
    /// 这张床是玩家造的（要存位置），还是 camp.json 预置的（建图自会长出来，不必存）？
    /// 按序号分：床#1/床#2 是开局那两张，床#3 起是玩家造的。
    /// </summary>
    private static bool IsPlayerPlacedBed(string key)
        => key.StartsWith(BedSpec.FurnitureKey + "#")
           && int.TryParse(key[(BedSpec.FurnitureKey.Length + 1)..], out int n)
           && n > 2;

    /// <summary>
    /// 一处结构。<b>用几何位置当标识</b>（不是列表下标）——下标会随建图逻辑变动（比如围栏切格的粒度改了）而错位，
    /// 而"南墙第 7 格在哪儿"是稳定的语义。
    /// </summary>
    private static StructureSave CaptureStructure(CampStructureInstance s) => new()
    {
        Id = StructureIdOf(s.Rect),
        Tier = s.State.Tier,
        Hp = s.State.Hp,
        DoorState = s.Door,
        LockTier = s.Lock,
    };

    /// <summary>结构的稳定标识：它占的那块矩形。</summary>
    private static string StructureIdOf(Rect2 r)
        => $"{r.Position.X:0.##},{r.Position.Y:0.##},{r.Size.X:0.##},{r.Size.Y:0.##}";

    private DogSave? CaptureDog()
    {
        if (_bruce is not Dog d)
        {
            return null;
        }
        return new DogSave
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            X = d.Position.X,
            Y = d.Position.Y,
            Body = d.CaptureBody(),
            Hunger = d.Hunger.Value,
            Apparel = SaveMapper.ToSave(d.Apparel),
            GuardStationing = d.GuardStationing,
        };
    }

    /// <summary>
    /// 场上尸体。<b>相位计数必须一起存</b>——尸体"还剩几个相位就烂没"是它和 <c>SpawnPhaseTick</c> 的差值，
    /// 计数丢了，一地尸体要么读档就全烂光、要么永远不烂。
    /// </summary>
    private CorpseYardSave CaptureCorpses() => new()
    {
        PhaseTick = _corpseYard.PhaseTick,
        NextId = _corpseYard.NextId,
        Corpses = _corpseYard.Live.Select(c => new CorpseSave
        {
            ContainerId = c.ContainerId,
            X = c.CartPosition.X,        // cartesian——Corpse.Position 是 iso 屏幕坐标，别存那个
            Y = c.CartPosition.Y,
            CellX = c.Cell.X,
            CellY = c.Cell.Y,
            SpawnPhaseTick = c.SpawnPhaseTick,
            Loot = c.Loot.ToList(),      // 身上还没被扒走的（穿什么扒什么）
            TintR = c.BodyTint.R,
            TintG = c.BodyTint.G,
            TintB = c.BodyTint.B,
            Radius = c.BodyRadius,
        }).ToList(),
    };

    /// <summary>
    /// 商人的货架。<b>库存要一起存</b>——不然 S/L 就能把买空的货架刷回满货。
    /// </summary>
    private List<OfferSave> CaptureMerchantShelf()
        => _merchantShelf?.Offers.Select(o => new OfferSave
        {
            Good = SaveMapper.ToSave(o.Good),
            Price = o.Price,     // 分（白银 2dp 分制）
            Stock = o.Stock,
        }).ToList() ?? new List<OfferSave>();

    /// <summary>读档：把货架就地摆回来（含剩余库存）。</summary>
    private void RestoreMerchantShelf(List<OfferSave> saved)
        => _merchantShelf.Restore(saved.Select(o =>
            new MerchantOffer(SaveMapper.FromSave(o.Good), o.Price, o.Stock)));

    // ---- 冷启动读档（从主菜单直接读，TODO 21①）----

    /// <summary>
    /// 冷启动读档请求槽：上层（主菜单 / 存档选择界面）在 <b>切换到 CampMain 场景之前</b> 写入要读的存档槽名，
    /// <c>_Ready</c> 会在建好世界后据此走冷启动读档分支——<b>跳过开局物资与商人起步白银</b>（否则会在存档物资之上
    /// 再叠一份，见 <see cref="ApplySave"/> 的调用前提），改由 <see cref="ApplySave"/> 灌档。
    /// <para>
    /// 用 static 做跨场景传参是 Godot 的惯用手法（无需 autoload）；<see cref="TakeColdLoadRequest"/> 读一次即清空，
    /// 保证下一次正常新开局不会误触。默认 <c>null</c> ⇒ 分支不入 ⇒ 既有新开局路径逐字节不变。
    /// </para>
    /// <para>
    /// <b>生产者＝主菜单</b>（<c>MainMenu.cs</c>，已落地）：<c>project.godot</c> 的 <c>main_scene</c> 现在是
    /// <c>MainMenu.tscn</c>，「新开局」<b>不</b>设此槽直接切场景；「读取存档」点某槽 ⇒ 设本槽再切场景 ⇒ 走冷启动读档。
    /// 消费侧＝<c>_Ready</c> 的 <see cref="TakeColdLoadRequest"/> 分支 → <see cref="StartFromColdLoad"/>。
    /// </para>
    /// </summary>
    public static string? PendingColdLoadSlot;

    /// <summary>本次 _Ready 已消费到的冷启动存档（null = 非冷启动，走正常新开局）。</summary>
    private SaveData? _coldLoadData;

    /// <summary>
    /// 消费 <see cref="PendingColdLoadSlot"/>：读一次即清空静态槽（避免污染下次新开局），读盘成功返回存档，
    /// 失败（无请求 / 版本闸门拒 / 损坏）返回 <c>null</c> —— 由调用方退回正常新开局，绝不把玩家丢进空营。
    /// </summary>
    private SaveData? TakeColdLoadRequest()
    {
        string? slot = PendingColdLoadSlot;
        PendingColdLoadSlot = null;   // 消费一次即清
        if (string.IsNullOrEmpty(slot))
            return null;

        SaveLoadResult result = SaveManager.Read(slot);
        if (!result.Ok)
        {
            // 版本闸门拒了 / 文件损坏：明说并退回新开局，不"尽力而为"读半个世界。
            GD.PushWarning($"冷启动读档失败（{result.Error}），改为新开局。");
            return null;
        }
        return result.Data;
    }

    /// <summary>
    /// 冷启动读档入口：由 <c>_Ready</c> 尾部 <c>CallDeferred</c>（时序与 <c>StartFirstDay</c> 同契约），
    /// 把 <see cref="_coldLoadData"/> 灌回营地。走的是与「游戏内就地覆盖读档」完全相同的 <see cref="ApplySave"/>，
    /// 因此人/物资/结构/剧情等 <b>玩法状态全部正确</b>。
    /// <para>
    /// <see cref="ApplySave"/> 的 <c>_clock.Restore</c> 刻意不发 <see cref="OnGamePhaseChanged"/>，
    /// 避免重复结算聚餐/健康日推进/关卡加载。恢复后只调用 <c>RefreshPhaseVisuals</c>，
    /// 使冷启动直读夜晚档时的环境色与视野遮暗当帧对齐，不带玩法副作用。
    /// </para>
    /// </summary>
    private void StartFromColdLoad()
    {
        ApplySave(_coldLoadData!);
        _coldLoadData = null;
    }

    // ---- 恢复 ----

    /// <summary>
    /// 把一份存档摆回营地。
    /// <para>
    /// <b>调用前提</b>：世界已按 <c>camp.json</c> 建好（<c>BuildWorld</c> 跑过，结构/家具/容器都在，且是初始态），
    /// 且开局物资（<c>ApplyStorageInitialStock</c>）与商人起步白银**没有发放**——否则读档会在存档物资之上
    /// 再叠一份开局物资。
    /// </para>
    /// </summary>
    public void ApplySave(SaveData s)
    {
        // 1) 时钟先摆。尸体腐化/商人日程/剧情条件都按天数与相位算，钟不对，后面全错。
        //    Restore 刻意不发 OnPhaseChanged——读档不是"相位切换"，世界是被摆回去的，不是走过去的；
        //    发事件会让订阅方把一个已经结算过的相位再结算一遍。
        _clock.Restore(s.World.Day, s.World.Phase, s.World.PhaseElapsed,
                       s.World.TravelElapsed, s.World.WarningFired, s.World.SpeedIndex);
        // Restore 刻意不发 OnPhaseChanged：读档不得重结算聚餐/健康日/关卡切换。
        // 但冷启动的 VisionMask 仍是 _Ready 的白天默认，环境色也应在灌档当帧对齐；
        // 因此只走纯表现再入点，不广播任何玩法事件。
        RefreshPhaseVisuals(_clock.CurrentPhase);

        // 2) 剧情 flag（半个存档）。
        RestoreStoryFlags(s.StoryFlags);

        // 2.5) [T57] 网状解锁：去过哪些调查点。
        // 🔴 **null ⇒ 这是 T57 之前的存档**（那时候全图一开局就都能去）⇒ 一律视为「全部已解锁」，
        //    不去剥夺玩家已经打下来的进度。空列表 [] ⇒ 新档，只有两个起点开着。
        _legacyFullUnlock = s.VisitedDestinations is null;
        _visitedDestinations.Clear();
        if (s.VisitedDestinations is { } visited)
        {
            foreach (string d in visited)
                _visitedDestinations.Add(d);
        }

        // 3) 营地物资与结构。
        RestoreCamp(s.Camp);

        // 4) 人。
        RestoreSurvivors(s.Survivors);
        RestoreDog(s.Dog);

        // 5) 尸体（相位计数先于尸体——见 CorpseYard.RestorePhaseTick 的注释）。
        RestoreCorpses(s.Corpses);

        // 6) 商人日程：**不重滚**（存档时后天来，读回来还是后天来，否则 S/L 成了刷商人日程的作弊器）。
        //    货架连同剩余库存一起摆回来——否则 S/L 能把买空的货架刷回满货。
        _merchantSchedule = MerchantSchedule.Restore(_healthRng, s.Merchant.NextVisitDay);
        RestoreMerchantShelf(s.Merchant.Shelf);

        // 7) 远征与羁绊。
        _pendingDestination = s.Expedition.PendingDestination ?? "";
        _pendingTravelTime = (int)s.Expedition.PendingTravelTime;
        _todaysExpeditionIds.Clear();
        foreach (int id in s.Expedition.TodaysExpeditionIds)
        {
            _todaysExpeditionIds.Add(id);
        }
        _bruceExpedition = s.Expedition.BruceAlong;
        _bondDaysBothAlive = s.Bonds.BondDaysBothAlive;

        // 8) 派生量重建（不进存档的那些）：掩体场从结构+沙袋、导航从地图、护甲层已在 Pawn.ApplySave 里重建。
        RebuildDerivedAfterLoad();
    }

    private void RestoreStoryFlags(Dictionary<string, string> flags)
    {
        foreach (string key in _storyFlags.Snapshot().Keys.ToList())
        {
            _storyFlags.Set(key, null);   // null = 清除
        }
        foreach (var kv in flags)
        {
            _storyFlags.Set(kv.Key, kv.Value);
        }
    }

    private void RestoreCamp(CampSave camp)
    {
        _resources = new CampResources(camp.Food);
        // ⚠️ **必须在复原库存/持械之前**：改装武器只是一个名字（"步枪（刺刀型）"），
        // 得先把它的身份注册回去，后面按名回查（库存物品、Pawn 持械）才认得出这把枪。
        SaveMapper.RestoreModdedWeapons(camp.ModdedWeapons);

        SaveMapper.RestoreInventory(_inventory, camp.Inventory);
        SaveMapper.RestoreWorkbench(_workbench, camp.WorkbenchTools);
        SaveMapper.RestoreCookStation(_cookStation, camp.CookwareInstalled);   // [批次21·T14]
        _butcherStation.Restore(camp.ButcherKnife);   // [T67] 刀槽复位（刀在存档里"住在"案板上，不经库存）
        SaveMapper.RestoreContainerLoot(_containerLoot, camp);

        // 玩家自己摆的家具（改装台）：按存档里的位置重新立起来（含碰撞/导航洞/可点击容器）。
        RestorePlacedFurniture(camp.PlacedFurniture);

        _craftingJob = SaveMapper.FromSave(camp.CraftingJob);
        _craftingJobWorker = camp.CraftingJob is { WorkerId: >= 0 } cj
            ? _survivors.FirstOrDefault(p => p.Id == cj.WorkerId)
            : null;

        _sandbagSeq = camp.SandbagSeq;

        // [批次21·T26] 陷阱命名序号。
        // ⚠️ **必须 Max、不能直接赋值**：本行跑在 RestorePlacedFurniture **之后**，而那里的 RespawnTrap 已经把
        // _trapSeq 推到了"场上实名的最大号"。直接赋值会把它盖回去 —— 读一个手改过/旧版的档（TrapSeq=0，
        // 但场上摆着 陷阱#1..#3）就会让下一个新陷阱又叫 陷阱#1，**与场上那个直接撞名**（字典里一个顶掉另一个）。
        // 取 Max ⇒ 两个来源谁大听谁的，撞名在结构上不可能发生。
        _trapSeq = Mathf.Max(_trapSeq, camp.TrapSeq);

        // [T75] 捕鸟陷阱命名序号：同圈套陷阱——必须 Max（RespawnBirdTrap 已在 RestorePlacedFurniture 里把它推到场上实名最大号，
        // 直接赋值会把手改/旧档的 0 盖回去 ⇒ 下一个新陷阱撞名）。取 Max ⇒ 撞名结构上不可能。
        _birdTrapSeq = Mathf.Max(_birdTrapSeq, camp.BirdTrapSeq);

        // [批次21·impl-bedrest] 床位占用（须在 RestorePlacedFurniture 之后——玩家造的床得先立回场上、
        // AddBed 进登记册，占用关系才对得上号）。卧床令与休养流水账跟着各人走（PawnSave）。
        ApplyBedSave(camp);

        // 结构：按几何位置对号入座，把血量/档位/门态盖回去。
        var byId = camp.Structures.ToDictionary(x => x.Id);
        foreach (CampStructureInstance inst in _structures)
        {
            string id = StructureIdOf(inst.Rect);
            if (!byId.TryGetValue(id, out StructureSave? saved))
            {
                // 存档里没有这一格 = 它当时已经被砸没了。摆回"缺口"状态。
                DestroyStructureForLoad(inst);
                continue;
            }
            inst.State = CampStructureState.Restore(saved.Tier, saved.Hp);
            inst.Door = saved.DoorState;
            inst.Lock = saved.LockTier;
        }
    }

    /// <summary>
    /// 读档：把玩家摆过的家具（**改装台** + 玩家造的**床** + 玩家垒的**沙袋**）重新立回世界。
    /// 已经在场的（同名）跳过——不摆两件。
    /// <para>
    /// 只有**实心**家具（改装台）会挖导航洞 ⇒ 摆过才需重烘焙导航。
    /// 床与沙袋都是**非实心**的（床要让人走上去躺下；沙袋恒不挡路——那正是它获准自由摆放的全部理由），
    /// 立回去不动寻路图。
    /// </para>
    /// </summary>
    private void RestorePlacedFurniture(List<PlacedFurnitureSave> placed)
    {
        // ⚠️ 先清场，再复原。**读档是就地覆盖世界，不是重载场景**（见 ApplySave）——
        // 不清场的话，本局摆下、而存档里并没有的家具会**赖在场上不走**：
        // 你垒了三垛沙袋、造好了改装台，然后读一个"还没造改装台"的旧档 —— 台子和沙袋照样杵在那儿，
        // `HasModBench` 还是 true，等于读档读出一个**存档里从不存在的营地**。
        // （这是"读档丢家具"的对称面：一个少东西，一个多东西，根因是同一个。）
        ClearPlayerPlacedFurniture();

        bool solidPlaced = false;
        foreach (PlacedFurnitureSave p in placed ?? new List<PlacedFurnitureSave>())
        {
            // 存档里的键可能是 null（手改过的档 / 旧版档）——跳过，别把 null 喂进字典。
            if (p.Key is not { Length: > 0 } key || _furniture.ContainsKey(key))
            {
                continue;   // 无键，或已在场则不重复摆
            }

            var rect = new Rect2((float)p.X, (float)p.Y, (float)p.W, (float)p.H);

            // [impl-furniture-registry] 分派收进注册表（正文在 CampMain.Placeables.cs）：哪一种家具就调它登记的
            // Respawn（= 既有 RespawnX/SpawnX，一字未改）。非实心家具（床/沙袋/陷阱/捕鸟/桌子/菜园）立回去不动寻路图；
            // 实心设施（烹饪台/改装台/宰杀点/宰杀台）挖了导航洞 ⇒ IsSolid 记一笔、末尾统一重烘焙一次。
            // 此前这里是一串逐类型的平行 if——漏接一条就是"读档后这种家具凭空消失"。
            foreach (PlaceableFurnitureDef def in _placeables)
            {
                if (!def.Match(key))
                {
                    continue;
                }
                def.Respawn(key, rect);
                if (def.IsSolid)
                {
                    solidPlaced = true;
                }
                break;
            }
        }
        if (solidPlaced)
        {
            RebakeNavigation();   // 实心家具挡路 ⇒ 寻路图得知道
        }
    }

    /// <summary>
    /// 读档前清场：把**本局玩家摆下的**家具（改装台 / 玩家造的床 / 沙袋）从世界上抹干净，
    /// 好让 <see cref="RestorePlacedFurniture"/> 按存档重新摆一遍。
    /// <para>
    /// camp.json 预置的家具（工作台/柜子/开局那两张床）**不动**——它们不由存档管，建图时就在原地。
    /// </para>
    /// <para>
    /// 走 <c>RemoveFurniture</c> 这个**唯一出口**，故连带清干净：碰撞体 / 视觉块 / 可点击登记 /
    /// 半身掩体场 / 导航洞（+ 重烘焙）/ 床位册占用。自己手动删字典会漏掉后面这一串。
    /// </para>
    /// </summary>
    private void ClearPlayerPlacedFurniture()
    {
        // [impl-furniture-registry] "本局玩家摆下的家具"= 命中注册表任一登记项的 Match（正文在 CampMain.Placeables.cs）。
        // 此前是一串逐类型 OR 谓词——漏一条就"读档后这种旧家具赖在场上不走"（HasModBench 等状态跟着错）。
        // 先拍快照：RemoveFurniture 会改 _furniture，不能边遍历边删。camp.json 预置的家具（工作台/柜子/开局那两张床）
        // 不命中任何 Match（床按序号 >2 才算玩家造），故天然不动。
        var doomed = _furniture.Keys
            .Where(k => _placeables.Any(def => def.Match(k)))
            .ToList();

        foreach (string key in doomed)
        {
            RemoveFurniture(key);
        }
    }

    /// <summary>
    /// 读档：把一垛沙袋按存档里的**实例名 + 原位置**立回世界（镜像 <c>PlaceSandbagAt</c> 的登记，但不扣库存、不改流水号、不弹提示）。
    /// <para>
    /// <b>刻意不 AddSolid、刻意不进 <c>_navHoles</c></b> —— 沙袋恒不挡路（<see cref="SandbagSpec.IsSolid"/>=false），
    /// 这是它获准自由摆放的全部理由；读档复原当然也不能偷偷把它变成实心的。
    /// </para>
    /// <para>
    /// 视觉种子取自实例名里的流水号（"沙袋#3" → 3），与当初摆下时用的是同一个 ⇒ 读档后那垛沙袋
    /// **长得跟存档前一模一样**，而不是换了一副随机面孔。
    /// </para>
    /// </summary>
    private void RespawnSandbag(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new[] { 0.56, 0.51, 0.36 }, jitter = 0.18 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 19 + SandbagSeqOf(name), height: CoverPropHeight, cell: 48f, collect: visuals);

        // 半身掩体登记：贴着它的双方都按 Wiki 配置获得远程无效概率（读档后这份收益必须还在，否则玩家的工事白垒了）。
        _coverField.Add(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y,
            SandbagSpec.CoverChance, SandbagSpec.BlocksMelee);

        // 可拆句柄（Body=null：它压根没有碰撞体）+ 可点击登记 ⇒ 读档后照样能 Shift+右键拆走重摆。
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "sandbag" });
    }

    /// <summary>从实例名里取流水号（"沙袋#3" → 3）；取不到给 0（只影响视觉种子，不影响玩法）。</summary>
    private static int SandbagSeqOf(string name)
    {
        int hash = name.IndexOf('#');
        return hash >= 0 && int.TryParse(name[(hash + 1)..], out int n) ? n : 0;
    }

    private void RestoreSurvivors(List<PawnSave> saved)
    {
        // 场上的人按 Id 与存档对上号，就地覆盖状态。
        foreach (PawnSave ps in saved)
        {
            Pawn? p = _survivors.FirstOrDefault(x => x.Id == ps.Id);
            if (p is not null)
            {
                p.ApplySave(ps);
            }
        }

        // 存档里没有的人 = 他在存档那一刻已经不在名单上了（死透并清走）。从场上摘掉。
        var keep = saved.Select(x => x.Id).ToHashSet();
        foreach (Pawn gone in _survivors.Where(p => !keep.Contains(p.Id)).ToList())
        {
            _survivors.Remove(gone);
            gone.QueueFree();
        }
    }

    private void RestoreDog(DogSave? s)
    {
        if (s is null)
        {
            return;   // 存档里没有布鲁斯 = 他没在营地（还没入队 / 已身故）
        }
        if (_bruce is not Dog d)
        {
            return;   // 场上没有狗可摆——狗的入队是剧情事件，由 StoryFlags 驱动重放
        }
        d.Position = new Vector2((float)s.X, (float)s.Y);
        d.RestoreBody(s.Body);
        d.Hunger.Restore(s.Hunger);
        SaveMapper.RestoreDogApparel(d.Apparel, s.Apparel);
        d.GuardStationing = s.GuardStationing;
    }

    private void RestoreCorpses(CorpseYardSave s)
    {
        _corpseYard.ClearAll();
        _corpseYard.RestorePhaseTick(s.PhaseTick);   // 必须先于落尸
        _corpseYard.RestoreNextId(s.NextId);

        foreach (CorpseSave c in s.Corpses)
        {
            Corpse? restored = _corpseYard.RestoreCorpse(
                new Vector2((float)c.X, (float)c.Y),
                new CorpseCell(c.CellX, c.CellY),
                new Color(c.TintR, c.TintG, c.TintB),
                c.Radius,
                c.ContainerId,
                c.SpawnPhaseTick,
                c.Loot);
            // 读档后的尸体必须重新进入可点击容器表；否则装备（包括断肢遗落物）虽在 CorpseSave
            // 里恢复了，玩家却永远点不到，形成“数据在、消费链断”的静默丢失。
            if (restored is { Loot.Count: > 0 } && !string.IsNullOrEmpty(restored.ContainerId))
            {
                RegisterCorpseContainer(restored);
            }
        }
    }

    /// <summary>
    /// 读档时把一处结构摆成"已被砸没"：撤碰撞、撤视觉、撤掩体登记，并重烘焙导航开出缺口。
    /// 走的是与正常摧毁同一条路——不另开一条，免得两条路日后走偏。
    /// </summary>
    private void DestroyStructureForLoad(CampStructureInstance inst)
    {
        if (inst.Removed)
        {
            return;
        }
        inst.State = CampStructureState.Restore(inst.State.Tier, 0);
        DestroyStructure(inst);
    }

    /// <summary>
    /// 重建那些**不进存档**的派生量。它们全都能从已恢复的状态算回来，所以存它们只是在给自己找不一致。
    /// </summary>
    private void RebuildDerivedAfterLoad()
    {
        BakeNavPoly();                    // 导航：门态/缺口变了，重烘焙
        _breachCandidatesDirty = true;    // 破防候选池：结构存亡/门态变了，下帧重算
    }

    // ---- 存档时机：只有自动存档，一天两次 ----

    /// <summary>
    /// 战斗跨过了存档相位 ⇒ 存档欠着，等仗打完再补。见 <see cref="AutosaveOnPhaseChange"/>。
    /// </summary>
    private bool _autosavePending;

    /// <summary>
    /// 自动存档：<b>清晨聚餐 / 黄昏聚餐 两个相位切换时各存一次</b>（一天两次，用户拍板）。
    /// <para>
    /// <b>没有手动存档</b>——玩家没有"存档"这个动作，也就没法在冲进去之前先存一个。
    /// S/L 大法是从<b>源头</b>被堵死的，而不是靠在存档按钮上加限制。
    /// 打崩了想重来？只能回到上一顿饭，那半天的决策（派谁出门、搜了哪些点、开没开门）全部重做。
    /// </para>
    /// <para>
    /// 挑这两顿饭的理由见 <see cref="SaveRotation.ShouldAutosaveAt"/>：它们是一天里仅有的两个
    /// 模态静止点，且正好把一天切成白天段与夜晚段。
    /// </para>
    /// </summary>
    private void AutosaveOnPhaseChange(DayPhase phase)
    {
        if (_gameOver || !SaveRotation.ShouldAutosaveAt(phase))
        {
            return;
        }

        // ⚠️ 战斗中**绝不能存**——但不是为了防 S/L（玩家本来就没有存档动作），而是因为
        // **存档里根本没有敌人**：丧尸/劫掠者是刷出来的运行时节点，不是玩家状态，不在 SaveData 里。
        // 真在袭营打到一半存下去，读回来就是一个"敌人凭空全没了"的世界——玩家白赚一次清场，
        // 这比 S/L 还狠。所以欠着，等仗打完再补一次。
        if (InCombat)
        {
            _autosavePending = true;
            return;
        }

        DoAutosave();
    }

    /// <summary>
    /// 补上欠下的自动存档（战斗结束的那一刻）。由 <c>_Process</c> 每帧问一次——
    /// 战斗结束没有统一的出口事件（袭营/围攻/夜袭/教学关各自收尾），轮询是这里最老实的做法。
    /// </summary>
    private void TickPendingAutosave()
    {
        if (!_autosavePending || InCombat || _gameOver)
        {
            return;
        }
        _autosavePending = false;
        DoAutosave();
    }

    private void DoAutosave()
    {
        string slot = SaveRotation.SlotNameFor(_clock.Day, _clock.CurrentPhase);
        SaveData data = CaptureSave(DisplayNames.Of(_clock.CurrentPhase), isAutosave: true);

        if (!SaveManager.Write(slot, data, out string? err))
        {
            GD.PushWarning($"自动存档失败：{err}");
            return;
        }

        // 轮转：只留最近 6 个（≈ 三天）。**留一串历史档是这个设计的前提**——
        // 玩家没有手动存档可以兜底，单槽覆盖会把他永久锁死在一个已经输了的局面里。
        SaveManager.PruneAutosaves();
        _campToast?.Show("已自动存档", UiStyle.Success);
    }

    // ---- 读档面板（UI）：随时可读 ----

    private SavePanel _savePanel = null!;
    private bool _saveOpen;
    private bool _prevSavePaused;

    /// <summary>造读档面板并接线。由 <c>_Ready</c> 调一次。</summary>
    private void SetupSavePanel()
    {
        _savePanel = new SavePanel();
        AddChild(_savePanel);

        _savePanel.LoadRequested += slot =>
        {
            SaveLoadResult result = SaveManager.Read(slot);
            if (!result.Ok)
            {
                // 版本闸门拒了 / 文件损坏 —— 明说，绝不"尽力而为"读进半个世界。
                _savePanel.ReportError(result.Error);
                return;
            }
            ApplySave(result.Data!);
            CloseSavePanel();
            _campToast?.Show($"已读取：第 {result.Data!.Meta.Day} 天", UiStyle.Success);
        };

        _savePanel.Closed += CloseSavePanel;
    }

    /// <summary>
    /// 开/关读档面板（F5）。<b>随时可读</b>（用户拍板）——包括战斗中：
    /// 玩家有"随时离开"的自由，只是没有"选择何时存档"的自由。
    /// 开着时暂停世界——读档界面不该让丧尸继续啃门。
    /// </summary>
    private void ToggleSavePanel()
    {
        if (_saveOpen)
        {
            CloseSavePanel();
            return;
        }

        _saveOpen = true;
        _prevSavePaused = _clock.Paused;
        _clock.SetPaused(true);
        _savePanel.Open();
    }

    private void CloseSavePanel()
    {
        if (!_saveOpen)
        {
            return;
        }
        _saveOpen = false;
        _clock.SetPaused(_prevSavePaused);
    }
}
