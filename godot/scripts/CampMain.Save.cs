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
            CraftingJob = SaveMapper.ToSave(_craftingJob, _craftingJobWorker?.Id ?? -1),
            SandbagSeq = _sandbagSeq,
            Structures = _structures
                .Where(s => !s.Removed)   // 已摧毁清场的不存——读档时它本来就该是个缺口
                .Select(CaptureStructure)
                .ToList(),
        };

        // 容器藏物 + 已搜/搜了一半——「逐件搜刮到一半退出」的进度天然就在这三份账里。
        SaveMapper.CaptureContainerLoot(_containerLoot, camp);
        return camp;
    }

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

        // 2) 剧情 flag（半个存档）。
        RestoreStoryFlags(s.StoryFlags);

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
        SaveMapper.RestoreInventory(_inventory, camp.Inventory);
        SaveMapper.RestoreWorkbench(_workbench, camp.WorkbenchTools);
        SaveMapper.RestoreContainerLoot(_containerLoot, camp);

        _craftingJob = SaveMapper.FromSave(camp.CraftingJob);
        _craftingJobWorker = camp.CraftingJob is { WorkerId: >= 0 } cj
            ? _survivors.FirstOrDefault(p => p.Id == cj.WorkerId)
            : null;

        _sandbagSeq = camp.SandbagSeq;

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
            _corpseYard.RestoreCorpse(
                new Vector2((float)c.X, (float)c.Y),
                new CorpseCell(c.CellX, c.CellY),
                new Color(c.TintR, c.TintG, c.TintB),
                c.Radius,
                c.ContainerId,
                c.SpawnPhaseTick,
                c.Loot);
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
