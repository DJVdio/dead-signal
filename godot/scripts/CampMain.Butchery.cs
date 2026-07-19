using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource / SystemRandomSource
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>宰杀设施的消费层接线</b>（T67）—— 把「纯逻辑绿」的宰杀系统真正接进 Godot 运行时。
///
/// <para>
/// 🔴 <b>此前这一整条接线是缺的</b>：<see cref="ButcheryLogic"/> / <see cref="ButcherStation"/> 纯逻辑早就在盘上，
/// 但 ① 简易宰杀点不在 <see cref="PlaceableItems"/>、<c>OnStashPlaceRequested</c> 无分支、完工分流也没 butcher 分支
/// ⇒ 造出来<b>静默变库存杂物、永不进 <c>_furniture</c></b> ⇒ <c>HasButcherPoint</c> 恒 false ⇒ 连"升级宰杀台"的门槛也永不满足；
/// ② <b>没有任何交互调用 <see cref="ButcheryRuntime.Butcher"/></b> ⇒ 玩家根本宰不了。
/// <b>连锁塌方</b>：羽毛（弓线源头）与碎皮革（皮革线源头）双双断供。本单把这两层焊上。
/// </para>
///
/// <para>
/// <b>范式零发明</b>：简易宰杀点走<b>菜园/陷阱那条链</b>（配方 → 库存「摆放」→ 室内落位，实心 ⇒ 落位后重烘焙导航）；
/// 宰杀台走<b>烹饪台/改装台那条完工分流</b>（不进库存，完工即在简易点原地顶替它）；
/// 宰杀动作走<b>既有 <see cref="CraftingJob"/> 工时队列</b>（同 <c>cook:</c>/<c>plant:</c>/<c>salvage:</c>）；
/// 产物入库走纯编排 <see cref="ButcheryRuntime.Butcher"/>（消费层与单测同一段代码，随机走可注入源）。
/// </para>
/// </summary>
public sealed partial class CampMain : Node2D
{
    /// <summary>宰杀掷点（双倍产出，项目铁律：随机一律走可注入源；测试侧用 SequenceRandomSource 复现）。</summary>
    private readonly IRandomSource _butcherRng = new SystemRandomSource();

    /// <summary>全营那处宰杀设施的刀槽装配态（匕首/骨刀；field 初始化，早于读档就绪）。</summary>
    private readonly ButcherStationState _butcherStation = new();

    private ButcheryPanel _butcheryPanel = null!;
    private bool _butcheryOpen;          // 面板是否开着（与其它模态一样持有时标冻结）
    private int _prevButcherySpeed;      // 开面板前的时钟速度档（关闭时还原）
    private bool _prevButcheryPaused;    // 开面板前世界是否暂停（还原保真）
    private bool _placingButcherPoint;   // 正处于"摆放简易宰杀点"模式（左键落位、右键取消）

    /// <summary>宰杀工时任务 id 前缀（同 <c>cook:</c>/<c>plant:</c>：不是配方，是"把一只猎物宰成肉+副产物"）。</summary>
    private const string ButcherJobPrefix = "butcher:";

    /// <summary>当前营地宰杀设施的档位（有宰杀台 ⇒ Table，否则简易点；决定速度加成与双倍产出）。</summary>
    private ButcherTier CurrentButcherTier => HasButcherTable ? ButcherTier.Table : ButcherTier.SimplePoint;

    /// <summary>建面板 + 接事件（在 _Ready 里调，同其它面板）。</summary>
    private void SetupButcheryPanel()
    {
        _butcheryPanel = new ButcheryPanel { Layer = 20 };
        AddChild(_butcheryPanel);
        _butcheryPanel.Visible = false;
        _butcheryPanel.KnifeInstallRequested += OnButcherKnifeInstall;
        _butcheryPanel.KnifeRemoveRequested += OnButcherKnifeRemove;
        _butcheryPanel.ButcherRequested += OnButcherRequested;
        _butcheryPanel.Closed += CloseButchery;
    }

    // ──────────────────────────────── 建造：简易宰杀点 → 室内落位 ────────────────────────────────

    /// <summary>库存面板点「摆放」一处简易宰杀点 → 进入放置模式。由 <c>CampMain.cs</c> 的 <c>OnStashPlaceRequested</c> 一行分发过来。</summary>
    private void BeginButcherPointPlacement()
    {
        if (_inventory.MaterialCount(ButcherStation.PointItemKey) <= 0)
        {
            _campToast.Show("库里没有简易宰杀点——先去工作台钉一块案板。", CampToast.Bad);
            return;
        }
        _placingButcherPoint = true;
        BeginFurniturePlacement(ButcherStation.PointPlaceSpec);   // 绿/红落位预览（impl-placement 白送的）
        CloseStash();
    }

    /// <summary>放置模式下左键落位。拒绝时**不退出放置模式**（换个地方接着点，同烹饪台之外的可摆家具）。</summary>
    private void TryPlaceButcherPoint(Vector2 cart)
    {
        // 64px 禁建带 + 边界 + 压实心物 + 摞家具 + 室内约束，全由这一行管（规则在 PlacementRules）。
        if (!CheckFurniturePlacement(ButcherStation.PointPlaceSpec, cart))
        {
            return;   // 拒绝提示已由 CheckFurniturePlacement 弹过
        }
        // 一座就够（配方 AbsentGate 已灰掉重复建造，此为双保险）。
        if (HasButcherStation)
        {
            _campToast.Show("营地已经有一处宰杀设施了。", CampToast.Bad);
            EndButcherPointPlacement();
            return;
        }
        if (!_inventory.TrySpendMaterial(ButcherStation.PointItemKey, 1))
        {
            _campToast.Show("库里没有简易宰杀点。", CampToast.Bad);
            EndButcherPointPlacement();
            return;
        }

        var size = new Vector2(ButcherStation.Width, ButcherStation.Height);
        var rect = new Rect2(cart - size / 2f, size);
        SpawnButcherPoint(rect);
        RebakeNavigation();   // 它实心、挖了导航洞、不可跨越 —— 寻路图得知道
        EndButcherPointPlacement();
        _campToast.Show("简易宰杀点支好了。刀槽里放把匕首或骨刀，就能把老鼠、兔子、鸟宰成肉和皮。", CampToast.Ok);
    }

    /// <summary>退出摆放简易宰杀点模式（落位成功 / 右键作罢 都走这儿）。</summary>
    private void EndButcherPointPlacement()
    {
        _placingButcherPoint = false;
        EndFurniturePlacement();
    }

    // ──────────────────────────────── 立到场上（新造 / 升级 / 读档共用）────────────────────────────────

    /// <summary>把简易宰杀点立到场上（<b>实心</b>：碰撞 + 视觉 + 导航洞 + 可拆登记 + 可点击容器 role="butcher"）。</summary>
    private void SpawnButcherPoint(Rect2 rect) =>
        SpawnButcherFacility(rect, ButcherStation.PointFurnitureKey, seed: 41,
            new PixelStyle { color = new[] { 0.34, 0.20, 0.18 }, jitter = 0.16 });   // 血渍斑斑的板子

    /// <summary>把宰杀台立到场上（同上，只是更正经的一张案子）。</summary>
    private void SpawnButcherTable(Rect2 rect) =>
        SpawnButcherFacility(rect, ButcherStation.TableFurnitureKey, seed: 43,
            new PixelStyle { color = new[] { 0.30, 0.17, 0.15 }, jitter = 0.12 });

    /// <summary>
    /// 把一处宰杀设施立到场上（新造 / 升级 / 读档共用）：<b>实心</b>（与烹饪台/改装台同构 —— AddSolid 挖导航洞）。
    /// 读档的导航重烘焙由 <c>RestorePlacedFurniture</c> 统一做；新造/升级各自在调用点重烘焙。
    /// </summary>
    private void SpawnButcherFacility(Rect2 rect, string key, int seed, PixelStyle style)
    {
        var visuals = new List<Node2D>();
        StaticBody2D body = AddSolid(rect, style, seed, (float)_heights.prop, cell: 200f, visuals);
        _furniture[key] = new FurnitureInstance { Rect = rect, Body = body, Visuals = visuals };
        // 可点击：选中角色右键前往 → 开【宰杀】面板（见 ExecuteContainerInteract 的 "butcher" 分支）。
        _containers.Add(new ContainerRef { Name = key, Rect = rect, Role = "butcher" });
    }

    /// <summary>
    /// 配方「宰杀台」完工（由 <see cref="CompleteActiveCraftingJob"/> 一行分流过来）：<b>不进库存</b>，
    /// 在简易宰杀点<b>原地顶掉它、立起宰杀台</b>（用户："简易宰杀点可以升级为宰杀台" —— 升级不新开引擎轴）。
    /// <para>UpgradeGate 要求开工时已有简易宰杀点，而全营单任务队列使升级期间无从拆点 ⇒ 完工时简易点必在场。</para>
    /// </summary>
    private void CompleteButcherTableBuild()
    {
        if (HasButcherTable)
        {
            return;   // 一张就够（UpgradeGate 已挡重复，此为双保险）
        }
        if (!_furniture.TryGetValue(ButcherStation.PointFurnitureKey, out FurnitureInstance? point))
        {
            // 理论到不了（UpgradeGate 保证简易点在场、单任务队列保证升级期间拆不掉它）。真到了不静默吞：如实报。
            _campToast.Show("找不到要升级的简易宰杀点——宰杀台没处落。", CampToast.Bad);
            GD.Print("[宰杀台] 完工但简易宰杀点不在场，升级落空");
            return;
        }

        Rect2 rect = point.Rect;
        RemoveFurniture(ButcherStation.PointFurnitureKey);   // 顶掉简易点（连带碰撞/视觉/导航洞/容器登记，唯一出口）
        SpawnButcherTable(rect);
        RebakeNavigation();   // 顶替前后都实心，位置未变但走唯一重烘焙路径最稳
        int tableSpeedPct = (int)(ButcheryLogic.SpeedBonusOf(ButcherTier.Table) * 100);
        int tableDoublePct = (int)(ButcheryLogic.TableDoubleYieldChance * 100);
        _campToast.Show($"简易宰杀点升级成了宰杀台：手上快了 {tableSpeedPct}%，有 {tableDoublePct}% 机会一刀出双份。", CampToast.Ok);
        GD.Print($"[宰杀台] 完工，于简易点原地 {rect.Position} 顶替升级");
    }

    // ──────────────────────────────── 面板 ────────────────────────────────

    /// <summary>开（或刷新）宰杀面板：首次打开冻结时标。</summary>
    private void OpenButchery()
    {
        if (!_butcheryOpen)
        {
            CapturePanelTimeState(out _prevButcherySpeed, out _prevButcheryPaused);
            _butcheryOpen = true;
        }
        RefreshButchery();
        _butcheryPanel.Visible = true;
    }

    /// <summary>重刷宰杀面板（装/卸刀、下单、完工后调）。</summary>
    private void RefreshButchery()
        => _butcheryPanel.ShowFor(
            _butcherStation,
            CurrentButcherTier,
            ControllableCrafters(),   // 掌刀的人：同制作/掌勺，取存活且可控的幸存者
            _inventory,
            JobAt(FacilityJobKeys.MainButcherStation));

    private void CloseButchery()
    {
        _butcheryPanel.Visible = false;
        _butcheryOpen = false;
        _campToast.Hide();
        RestorePanelTimeState(_prevButcherySpeed, _prevButcheryPaused);
    }

    // ──────────────────────────────── 刀槽：装 / 卸（把刀从库存拿走钉上案板）────────────────────────────────

    /// <summary>
    /// 装一把刀进刀槽：<b>从库存扣一把该武器、钉上案板</b>（同烹饪台的炊具槽语义）。
    /// 顶掉的旧刀返还库存。<b>刀离库后由存档记住装了哪把</b>（见 CampMain.Save.cs），读档不凭空蒸发。
    /// </summary>
    private void OnButcherKnifeInstall(ButcherKnife knife)
    {
        if (!HasButcherStation)
        {
            _campToast.Show("还没有宰杀设施，刀没处放。", CampToast.Bad);
            return;
        }
        if (knife == ButcherKnife.None)
        {
            return;
        }
        if (_butcherStation.Slotted == knife)
        {
            return;   // 已经装的就是它（按钮本该灰着，此为双保险）
        }

        string weaponName = ButcherStation.WeaponNameOf(knife);
        Item? found = _inventory.Weapons.FirstOrDefault(w => w.DisplayName == weaponName);
        if (found is null || !_inventory.Remove(found))
        {
            _campToast.Show($"库里没有{weaponName}——先做一把出来。", CampToast.Bad);
            return;
        }

        ButcherKnife prev = _butcherStation.Install(knife);
        if (prev != ButcherKnife.None)
        {
            _inventory.Add(Item.Weapon(ButcherStation.WeaponNameOf(prev)));   // 顶下来的旧刀还回库存
        }
        _campToast.Show($"{weaponName}钉上了案板（宰杀速度 +{(int)(ButcheryLogic.SpeedBonusOf(knife) * 100)}%）。", CampToast.Ok);
        RefreshButchery();
        if (_stashOpen) OpenStash(null);
    }

    /// <summary>把刀从刀槽取下来（还回库存，可再装回去；拆设施不在此列）。</summary>
    private void OnButcherKnifeRemove()
    {
        ButcherKnife prev = _butcherStation.Remove();
        if (prev == ButcherKnife.None)
        {
            return;   // 本来就没装
        }
        string weaponName = ButcherStation.WeaponNameOf(prev);
        _inventory.Add(Item.Weapon(weaponName));
        _campToast.Show($"{weaponName}从案板上取下来了。", CampToast.Ok);
        RefreshButchery();
        if (_stashOpen) OpenStash(null);
    }

    // ──────────────────────────────── 下单：起一条宰杀工时任务 ────────────────────────────────

    /// <summary>
    /// 面板「宰一只」→ 判定（有刀 / 库里有这只猎物 / 台上没别的活）→ 起一条 <c>butcher:&lt;猎物&gt;</c> 工时任务，
    /// 挤进全营那条单任务队列。产物（肉 + 副产物 + 可能的双倍）留待完工由 <see cref="CompleteButcherJob"/> 结算。
    /// <para>⚠️ 猎物<b>在完工那一刻才扣</b>（<see cref="ButcheryRuntime.Butcher"/> 原子扣+产）——全营单任务队列保证宰杀期间
    /// 无从并发消耗这只猎物；万一被卖/被吃掉，完工时如实报"料没了"，不半吞。</para>
    /// </summary>
    private void OnButcherRequested(string quarryKey, Pawn worker)
    {
        if (!CanStartFacilityJob(FacilityJobKeys.MainButcherStation, worker, out string busyWhy))
        {
            _campToast.Show(busyWhy, CampToast.Bad);
            RefreshButchery();
            return;
        }
        if (!_butcherStation.HasKnife)
        {
            _campToast.Show("刀槽空着——徒手撕不开一只老鼠。先装把匕首或骨刀。", CampToast.Bad);
            RefreshButchery();
            return;
        }
        if (!ButcheryLogic.CanButcher(CurrentButcherTier, _butcherStation.Slotted, quarryKey)
            || _inventory.MaterialCount(quarryKey) <= 0)
        {
            _campToast.Show("库里没有这只猎物可宰。", CampToast.Bad);
            RefreshButchery();
            return;
        }

        int minutes = ButcheryLogic.MinutesFor(CurrentButcherTier, _butcherStation.Slotted);
        StartFacilityJob(FacilityJobKeys.MainButcherStation,
            new CraftingJob(ButcherJobPrefix + quarryKey, minutes), worker);
        _craftLastMinuteKey = -1;

        string quarryName = Materials.Find(quarryKey)?.DisplayName ?? quarryKey;
        string work = CraftingPanelFormat.FormatWorkDuration(minutes);
        _campToast.Show($"{worker.DisplayName} 上案板宰 {quarryName}（工时 {work}，夜间生产）。", CampToast.Ok);
        GD.Print($"[宰杀] {worker.DisplayName} 起单 {quarryKey}（{minutes} 分，{CurrentButcherTier}+{_butcherStation.Slotted}）");
        RefreshButchery();
        if (_stashOpen) OpenStash(null);
    }

    /// <summary>
    /// 宰杀工时满、完工（由 <see cref="CompleteActiveCraftingJob"/> 的 <c>butcher:</c> 分流一行调过来）：
    /// 走 <see cref="ButcheryRuntime.Butcher"/> 原子结算（扣 1 只猎物 → 出肉 + 副产物 → 双倍掷点 → 逐样入库）。
    /// </summary>
    private void CompleteButcherJob(string quarryKey)
    {
        ButcherTier tier = CurrentButcherTier;
        ButcherYield? y = ButcheryRuntime.Butcher(tier, _butcherStation.Slotted, quarryKey, _inventory, _butcherRng);
        if (y is null)
        {
            // 开工后规则变了（猎物被卖/被吃、或刀被取下）：料没成，如实报，不静默。
            _campToast.Show("这一刀落了空——要宰的东西没了，或刀被取下了。", CampToast.Bad);
            GD.Print($"[宰杀] 完工但结算落空：{quarryKey}（tier={tier}, knife={_butcherStation.Slotted}）");
            if (_butcheryOpen) RefreshButchery();
            return;
        }

        string meatName = Materials.Find(y.Value.MeatKey)?.DisplayName ?? y.Value.MeatKey;
        string byName = Materials.Find(y.Value.ByproductKey)?.DisplayName ?? y.Value.ByproductKey;
        string dbl = y.Value.Doubled ? "（宰杀台双倍！）" : "";
        _campToast.Show(
            $"宰好了：{meatName}×{y.Value.MeatQuantity} + {byName}×{y.Value.ByproductQuantity}{dbl}", CampToast.Ok);
        GD.Print($"[宰杀] 完工 {quarryKey} → {meatName}×{y.Value.MeatQuantity} + {byName}×{y.Value.ByproductQuantity} doubled={y.Value.Doubled}");
        if (_butcheryOpen) RefreshButchery();
        if (_stashOpen) OpenStash(null);
    }

    // ──────────────────────────────── 悬停提示 ────────────────────────────────

    /// <summary>悬停提示（由 <c>CampMain.cs</c> 的 role switch 一行分发过来）：报出档位 + 刀槽现状。</summary>
    private string ButcherHoverText(ContainerRef c)
    {
        string tierName = HasButcherTable ? ButcherStation.TableFurnitureKey : ButcherStation.PointFurnitureKey;
        string knife = _butcherStation.HasKnife ? ButcherStation.WeaponNameOf(_butcherStation.Slotted) : "空槽";
        return $"{tierName} · 刀槽：{knife} · 老鼠→肉+碎皮革、鸟→肉+羽毛 · 选中角色右键前往开案板 · Shift+右键拆走";
    }
}
