using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource / SystemRandomSource
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>菜园的消费层接线</b>（T72）—— 把「纯逻辑绿」的种植系统真正接进 Godot 运行时。
///
/// <para>
/// 规则/数值/编排在 <see cref="CropPlotSpec"/> / <see cref="CropPlotLogic"/> / <see cref="CropPlotRuntime"/>
/// （纯逻辑、零 Godot 依赖、<c>CropPlotRuntimeTests</c> 拿真 <see cref="InventoryStore"/>+<see cref="StoryFlags"/> 跑通两层）；
/// <b>本文件只做空间执行</b>：把菜园立到场上、把鼠标事件/每帧 delta 接到编排上、把收成塞进库存。
/// </para>
///
/// <para>
/// <b>交互范式零发明</b>：造菜园走<b>陷阱/沙袋那条链</b>（配方 → 库存「摆放」→ 左键落位 → Shift+右键拆走）；
/// 种/收走 <b>右键前往交互</b>那条链（同柜子/工作台）；种植动作走<b>既有 <see cref="CraftingJob"/> 工时队列</b>（同 <c>cook:</c>/<c>salvage:</c>）；
/// 生长计时走<b>既有 <see cref="StoryFlags"/></b>（不加存档字段）。<c>CampMain.cs</c> 里只留几个一行的调用点，正文全在这儿
/// —— 同 <c>CampMain.Traps.cs</c> 开的先例（CampMain.cs 是并发热点，短持快放）。
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>收获掷点（项目铁律：随机一律走可注入源；<b>不在运行时 new RNG</b>——测试侧用 SequenceRandomSource 复现）。</summary>
    private readonly IRandomSource _cropRng = new SystemRandomSource();

    /// <summary>玩家摆下的菜园命名序号（"菜园#1" 起）。存档要带上它，否则读档后新造的菜园会与旧的重名。</summary>
    private int _cropPlotSeq;

    /// <summary>场上菜园的数目（每帧生长的开销闸：一座都没有就整段跳过，空闲营地零开销）。</summary>
    private int _cropPlotCount;

    /// <summary>正处于"摆放菜园"模式（左键落位、右键取消）。同沙袋/陷阱/床。</summary>
    private bool _placingCropPlot;

    // ──────────────────────────────── 建造 → 自由摆放 ────────────────────────────────

    /// <summary>库存面板点「摆放」一座菜园 → 进入放置模式。由 <c>CampMain.cs</c> 的 <c>OnStashPlaceRequested</c> 一行分发过来。</summary>
    private void BeginCropPlotPlacement()
    {
        if (_inventory.MaterialCount(CropPlotSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里没有菜园——先去工作台翻一块地出来。", CampToast.Bad);
            return;
        }
        _placingCropPlot = true;
        BeginFurniturePlacement(CropPlotSpec.PlaceSpec);   // 绿/红落位预览（impl-placement 白送的）
        CloseStash();
    }

    /// <summary>放置模式下左键落位。拒绝时**不退出放置模式**（换个地方接着点，同沙袋/陷阱）。</summary>
    private void TryPlaceCropPlot(Vector2 cart)
    {
        // 64px 禁建带 + 边界 + 压实心物 + 摞家具，全由这一行管（规则在 PlacementRules，不自己写贴边判定）。
        if (!CheckFurniturePlacement(CropPlotSpec.PlaceSpec, cart))
        {
            return;   // 拒绝提示已由 CheckFurniturePlacement 弹过
        }
        if (!_inventory.TrySpendMaterial(CropPlotSpec.ItemKey, 1))
        {
            _campToast.Show("库里没有菜园——先去工作台翻一块地出来。", CampToast.Bad);
            EndCropPlotPlacement();
            return;
        }
        PlaceCropPlotAt(cart);
        EndCropPlotPlacement();
    }

    /// <summary>退出摆放菜园模式（落位成功 / 右键作罢 都走这儿）。</summary>
    private void EndCropPlotPlacement()
    {
        _placingCropPlot = false;
        EndFurniturePlacement();
    }

    /// <summary>菜园落位（新造）：分配序号、扣库存已在上游做过，这里只立到场上并报一句。</summary>
    private void PlaceCropPlotAt(Vector2 cart)
    {
        string name = $"{CropPlotSpec.FurnitureNamePrefix}{++_cropPlotSeq}";
        var size = new Vector2(CropPlotSpec.Width, CropPlotSpec.Height);
        var rect = new Rect2(cart - size / 2f, size);
        SpawnCropPlot(name, rect);
        _campToast.Show(
            $"{name} 翻好了。选中角色右键前往下种——种一颗吃 {CropPlotLogic.SeedCost} 土豆，" +
            $"{CropPlotLogic.MaturesInDayNightCycles:0.0} 个昼夜后熟，最多种 {CropPlotSpec.MaxPlants} 颗。", CampToast.Ok);
    }

    /// <summary>读档：把菜园原地立回来（位置与名字由存档给定，不重新分配序号；生长计时器在 StoryFlags 里天然复原）。</summary>
    private void RespawnCropPlot(string name, Rect2 rect)
    {
        SpawnCropPlot(name, rect);
        _cropPlotSeq = Mathf.Max(_cropPlotSeq, SeqOfCropPlot(name));   // 序号推到它之后，免得下次造菜园重名
    }

    /// <summary>
    /// 把一座菜园立到场上（新造/读档共用）：视觉 + 可点击容器（role="cropplot" ⇒ 右键前往下种/收获、Shift+右键拆）。
    /// <b>不建碰撞体、不挖导航洞</b>（<see cref="CropPlotSpec.IsSolid"/>=false）——一块菜地谁都跨得过去，摆不出 kill box。
    /// </summary>
    private void SpawnCropPlot(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new double[] { 0.30, 0.24, 0.16 }, jitter = 0.20 };   // 翻好的深色垄土
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 47 + SeqOfCropPlot(name), height: 4f, cell: 24f, collect: visuals);

        // Body=null：没有碰撞体。进 _furniture ⇒ ① 减速场自动收录（可跨越，减速值由 Wiki 配置提供）
        // ② Shift+右键走通用家具拆除（按 FurnitureBuildCost["菜园"] 折半返还，见 RemoveFurniture 的 ClearPlot 清计时器）
        // ③ 存档走 CampSave.PlacedFurniture 那条唯一出口。
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "cropplot" });
        _cropPlotCount++;
    }

    /// <summary>菜园实例名（"菜园#3"）里的序号；解不出来给 0。</summary>
    private static int SeqOfCropPlot(string key)
        => CropPlotSpec.IsCropPlotFurniture(key)
           && int.TryParse(key[CropPlotSpec.FurnitureNamePrefix.Length..], out int n) ? n : 0;

    // ──────────────────────────────── 种 / 收（右键前往交互）────────────────────────────────

    /// <summary>
    /// 到达菜园后执行交互（由 <c>CampMain.cs</c> 的 <see cref="ExecuteContainerInteract"/> 一行分发过来）：
    /// <list type="number">
    /// <item>有熟的 ⇒ <b>收</b>（走 <see cref="CropPlotRuntime.HarvestRipe"/>；分布与产出以 Wiki 配置表为准）。</item>
    /// <item>否则若能种 ⇒ <b>下种</b>（扣 1 种薯 + 起一条 <c>plant:菜园#N</c> 工时任务，夜间生产满 0.15h 落计时器）。</item>
    /// </list>
    /// </summary>
    private void ExecuteCropPlotInteract(Pawn arriver, ContainerRef hit)
    {
        string plot = hit.Name;

        // ① 有熟的先收（收获是即时的——熟土豆弯腰就薅）。
        if (CropPlotRuntime.RipeCount(_storyFlags, plot) > 0)
        {
            (int plants, int potatoes) = CropPlotRuntime.HarvestRipe(_storyFlags, _inventory, plot, _cropRng);
            _campToast.Show($"{plot}：收了 {plants} 颗，得土豆 {potatoes} 个。空出的地可以再下种。", CampToast.Ok);
            GD.Print($"[种植] {arriver.DisplayName} 收 {plot} → {plants} 颗 / {potatoes} 土豆");
            if (_stashPanel.Visible)
            {
                _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);
            }
            return;
        }

        // ② 没熟的可收 ⇒ 下种。种植走同一条工时队列（同 cook:/salvage:），台上有活就不接新单。
        if (_craftingJob is not null)
        {
            _campToast.Show("手头有活在做（制作/拆解/另一颗在种）：等它完工再下种。", CampToast.Bad);
            return;
        }
        if (!CropPlotRuntime.CanPlant(_storyFlags, _inventory, plot, out string? why))
        {
            _campToast.Show(why ?? "现在没法在这块地下种。", CampToast.Bad);
            return;
        }
        // 开工即扣料锁定（同 cook:）：扣掉 1 种薯，完工时才落计时器。
        if (!CropPlotRuntime.BeginPlant(_inventory))
        {
            _campToast.Show("没有种薯——种土豆得先有一颗土豆下地。", CampToast.Bad);
            return;
        }

        _craftingJob = new CraftingJob(CropPlotLogic.PlantJobPrefix + plot, CropPlotLogic.PlantWorkMinutes);
        _craftingJobWorker = arriver;
        _craftLastMinuteKey = -1;   // 重置增量基线（同下单制作/拆解）
        _craftMinuteBudget = 0f;

        string work = CraftingPanelFormat.FormatWorkDuration(CropPlotLogic.PlantWorkMinutes);
        _campToast.Show($"{arriver.DisplayName} 开始在 {plot} 下种（工时 {work}，夜间生产；种薯已下地）。", CampToast.Ok);
        GD.Print($"[种植] {arriver.DisplayName} 下种 {plot}（工时 {CropPlotLogic.PlantWorkMinutes} 分）");
        if (_stashPanel.Visible)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);   // 种薯已扣，库存面板开着就刷新
        }
    }

    /// <summary>
    /// 种植工时满、完工（由 <see cref="CompleteActiveCraftingJob"/> 的 <c>plant:</c> 分流一行调过来）：
    /// 把下一个空格的计时器置成 84 游戏小时（开始倒计时）。满种（不该发生，下种前已校验）则如实报——种薯已扣，白费。
    /// </summary>
    private void CompletePlantJob(string plotName)
    {
        int slot = CropPlotRuntime.CompletePlant(_storyFlags, plotName);
        if (slot == 0)
        {
            _campToast.Show($"{plotName} 已经满了，这颗种薯白下了。", CampToast.Bad);
            GD.Print($"[种植] 完工但 {plotName} 已满，种薯浪费");
            return;
        }
        int planted = CropPlotRuntime.PlantedCount(_storyFlags, plotName);
        _campToast.Show(
            $"{plotName} 下种成功（第 {planted}/{CropPlotSpec.MaxPlants} 颗）——" +
            $"{CropPlotLogic.MaturesInDayNightCycles:0.0} 个昼夜后成熟，种下就不用管。", CampToast.Ok);
        GD.Print($"[种植] {plotName} 第 {slot} 格下种，84 游戏小时倒计时开始");
    }

    // ──────────────────────────────── 每帧生长（零维护、昼夜都走）────────────────────────────────

    /// <summary>
    /// <b>每帧把 delta 折成游戏小时喂给 <see cref="CropPlotRuntime.TickGrowth"/></b>（由 <c>CampMain.cs</c> 的 <see cref="_Process"/> 一行调）。
    /// <para>
    /// 时间源用既有 <see cref="GameClock"/>：只在<b>昼/夜正相位</b>（DayExplore/NightAct，游戏钟真在走的两段）里推进，
    /// 一相位铺 12 游戏小时；冻结相位（聚餐/筹备/回营）<c>Engine.TimeScale=0</c> ⇒ delta≈0 ⇒ 天然不长。
    /// <b>昼夜都走、不按相位掷点、零维护</b>（用户："种下就不用管、一直走时间"）。存档天然覆盖（计时器在 StoryFlags）。
    /// </para>
    /// <para>场上一座菜园都没有 ⇒ 整段跳过（空闲营地零开销、零分配）。</para>
    /// </summary>
    private void TickCropGrowth(double delta)
    {
        if (_cropPlotCount <= 0 || delta <= 0.0)
        {
            return;
        }
        double phaseLen = _clock.CurrentPhaseLengthSeconds;   // 昼/夜正相位才 >0（GameClock 决定，不自造时间源）
        if (phaseLen <= 0.0)
        {
            return;
        }
        double gameHours = CropPlotRuntime.GameHoursForElapsed(delta, phaseLen);
        if (gameHours <= 0.0)
        {
            return;
        }
        CropPlotRuntime.TickGrowth(_storyFlags, gameHours);
    }

    /// <summary>
    /// 悬停提示（由 <c>CampMain.cs</c> 的 role switch 一行分发过来）：<b>把这块地的进度直接摊开</b>——
    /// 熟了几颗、种了几颗、最快几天后熟。地里的生长在场上没有痕迹，不报的话玩家只能干等。
    /// </summary>
    private string CropPlotHoverText(ContainerRef c)
    {
        string plot = c.Name;
        int planted = CropPlotRuntime.PlantedCount(_storyFlags, plot);
        int ripe = CropPlotRuntime.RipeCount(_storyFlags, plot);
        int cap = CropPlotSpec.MaxPlants;

        if (ripe > 0)
        {
            return $"{plot} · 🥔 {ripe} 颗熟了！选中角色右键前往收获 · 种 {planted}/{cap} · Shift+右键拆走";
        }
        if (planted > 0)
        {
            int days = CropPlotRuntime.SoonestDaysLeft(_storyFlags, plot);
            return $"{plot} · 种 {planted}/{cap}，最快 {days} 个昼夜后熟 · 右键前往补种（耗种薯 {CropPlotLogic.SeedCost}）· Shift+右键拆走";
        }
        return $"{plot} · 空地 0/{cap} · 选中角色右键前往下种（耗种薯 {CropPlotLogic.SeedCost}，{CropPlotLogic.MaturesInDayNightCycles:0.0} 昼夜熟）· Shift+右键拆走";
    }
}
