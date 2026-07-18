using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>可摆放家具注册表</b>（批次21·impl-furniture-registry）—— 把"玩家能摆到地上的家具"这件事从
/// <b>散落在 ~5 处的平行 <c>if</c>/<c>switch</c> 分派链</b>收成<b>一张表</b>。
///
/// <para>═══ <b>它解决的是哪个 bug</b> ═══
/// 在它之前，新增一种可摆放家具（陷阱 / 捕鸟陷阱 / 床 / 菜园 / 宰杀点…）要在多处<b>各加一条平行分支</b>：
/// <c>OnStashPlaceRequested</c> 的 <c>if</c> 链、<c>HandleMouseButton</c> 的六块同形 <c>if(_placingX)</c>、
/// <c>RestorePlacedFurniture</c> 的前缀分派、<c>ClearPlayerPlacedFurniture</c> 的 OR 谓词链……
/// <b>漏接任何一条</b>就复活"死按钮"（摆不出来 / 读档消失 / 拆不掉）—— 捕鸟陷阱、圈套、床、改装台都曾漏接过。
/// 收成一张表后，<b>这四条链改成遍历同一份 <see cref="_placeables"/></b>：忘接一条在结构上不再可能。
/// </para>
///
/// <para>═══ <b>刻意只重路由"分派"，不碰"正文"</b>（零行为改动的关键）═══
/// 每个 <see cref="PlaceableFurnitureDef"/> 只是把<b>既有的</b>逐类型方法（<c>BeginTrapPlacement</c> /
/// <c>TryPlaceTrap</c> / <c>EndTrapPlacement</c> / <c>RespawnTrap</c> / <c>TrapSpec.IsTrapFurniture</c> …）
/// <b>包成委托</b> —— 那些方法一个字都没改，各自的落位/扣料/视觉/掷点/存档正文仍在各自的 partial 文件里
/// （<c>CampMain.Traps.cs</c> / <c>CampMain.BirdTrap.cs</c> / <c>CampMain.Farming.cs</c> …）。
/// 表只回答"这个 key/前缀是哪一种"，答完把活派回原方法。行为因此逐字节不变（既有单测=黄金参照，全绿即证）。
/// </para>
///
/// <para>═══ <b>刻意留在表外的三处特例</b>（有意，不是遗漏）═══
/// <list type="bullet">
/// <item><b>掷点结算</b>（<c>ResolveTrapsForPhase</c> / <c>ResolveBirdTrapsForPhase</c>）：圈套命中后要再掷"物种点"
///       （老鼠/兔），捕鸟不掷 —— <b>两种随机流形状不同，合并会让产出概率漂移</b>。各走各的，不进表。</item>
/// <item><b>存档写入</b>（<c>CapturePlacedFurniture</c>）：锚定设施优先、free 家具单遍 <c>_furniture</c> —— <b>顺序敏感</b>，
///       套表遍历会改变存档 JSON 的条目顺序（结果虽同，但非逐字节）。保持原样，见该方法的指针注释。</item>
/// <item><b>悬停/到达交互</b>（<c>HoverTextFor</c> / <c>ExecuteContainerInteract</c> 的 role 分支）：与门/柜子/商人等
///       非家具 role 交织在同一个 <c>switch</c> 里；且漏接它们只是"没提示/点了没反应"，当场可见，不是阴险的死按钮。
///       ROI 不划算、风险更高，不动。</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>
    /// 全部<b>持久化的玩家可摆放家具</b>的注册表（含锚定实心设施 —— 它们不进库存、由配方完工直接立起，
    /// 但一样要存档/读档/清场）。四条分派链（摆放入口 / 放置模式鼠标 / 读档复原 / 读档前清场）共读这一份。
    /// <para>在 <see cref="_Ready"/> 里一次性构建（此时逐类型方法与 Spec 常量都已就绪）。</para>
    /// </summary>
    private List<PlaceableFurnitureDef> _placeables = new();

    /// <summary>
    /// 构建注册表（<see cref="_Ready"/> 里调一次）。<b>顺序无关</b>：四条链要么按 key 精确匹配、要么按互斥的
    /// <see cref="PlaceableFurnitureDef.Match"/> 谓词分派，各类型的实例名空间互不相交。为可读性按"先 free 后锚定"排。
    /// </summary>
    private void BuildPlaceables()
    {
        _placeables = new List<PlaceableFurnitureDef>
        {
            // ── 非实心、可跨越、可自由摆放（库存「摆放」→ 左键落位 → 右键作罢）──
            new()
            {
                TypeName = "沙袋",
                StashItemKey = SandbagSpec.ItemKey,
                Begin = BeginSandbagPlacement,
                IsPlacing = () => _placingSandbag,
                TryPlace = TryPlaceSandbag,
                Cancel = () => _placingSandbag = false,
                CancelToast = "算了，沙袋先搁着。",
                Match = SandbagSpec.IsSandbagFurniture,
                IsSolid = false,
                Respawn = RespawnSandbag,
            },
            // 顺序 = 既有 HandleMouseButton 六块 if(_placingX) 的排列（沙袋→陷阱→捕鸟→床→桌子→菜园→宰杀点）：
            // 同一时刻至多一种放置模式为真，唯一能与别种共存的是沙袋（它的 Begin 不关库存面板）——而沙袋恒排第一，
            // 故遍历命中的那一种与旧代码逐块判定的结果永远一致。读档复原/清场/摆放入口按互斥谓词或 key 分派，与顺序无关。
            new()
            {
                TypeName = "圈套陷阱",
                StashItemKey = TrapSpec.ItemKey,
                Begin = BeginTrapPlacement,
                IsPlacing = () => _placingTrap,
                TryPlace = TryPlaceTrap,
                Cancel = EndTrapPlacement,
                CancelToast = "算了，陷阱先收着。",
                Match = TrapSpec.IsTrapFurniture,
                IsSolid = false,
                Respawn = RespawnTrap,
            },
            new()
            {
                TypeName = "捕鸟陷阱",
                StashItemKey = BirdTrapSpec.ItemKey,
                Begin = BeginBirdTrapPlacement,
                IsPlacing = () => _placingBirdTrap,
                TryPlace = TryPlaceBirdTrap,
                Cancel = EndBirdTrapPlacement,
                CancelToast = "算了，捕鸟陷阱先收着。",
                Match = BirdTrapSpec.IsBirdTrapFurniture,
                IsSolid = false,
                Respawn = RespawnBirdTrap,
            },
            new()
            {
                TypeName = "床",
                StashItemKey = BedSpec.ItemKey,
                Begin = BeginBedPlacement,
                IsPlacing = () => _placingBed,
                TryPlace = TryPlaceBed,
                Cancel = EndBedPlacement,
                CancelToast = "算了，床先搁着。",
                Match = IsPlayerPlacedBed,
                IsSolid = false,
                Respawn = RespawnPlayerBed,
            },
            new()
            {
                TypeName = "桌子",
                StashItemKey = TableSpec.ItemKey,
                Begin = BeginTablePlacement,
                IsPlacing = () => _placingTable,
                TryPlace = TryPlaceTable,
                Cancel = EndTablePlacement,
                CancelToast = "算了，桌子先搁着。",
                Match = TableSpec.IsTableFurniture,
                IsSolid = false,
                Respawn = RespawnTable,
            },
            new()
            {
                TypeName = "沙发",
                StashItemKey = SofaSpec.ItemKey,
                Begin = BeginSofaPlacement,
                IsPlacing = () => _placingSofa,
                TryPlace = TryPlaceSofa,
                Cancel = EndSofaPlacement,
                CancelToast = "算了，沙发先搁着。",
                Match = SofaSpec.IsSofaFurniture,
                IsSolid = SofaSpec.IsSolid,
                Respawn = RespawnSofa,
            },
            new()
            {
                TypeName = "菜园",
                StashItemKey = CropPlotSpec.ItemKey,
                Begin = BeginCropPlotPlacement,
                IsPlacing = () => _placingCropPlot,
                TryPlace = TryPlaceCropPlot,
                Cancel = EndCropPlotPlacement,
                CancelToast = "算了，菜园先搁着。",
                Match = CropPlotSpec.IsCropPlotFurniture,
                IsSolid = false,
                Respawn = RespawnCropPlot,
            },
            // ── 实心宰杀点：库存「摆放」→ 室内落位（落位后重烘焙导航，正文在 CampMain.Butchery.cs）──
            new()
            {
                TypeName = "简易宰杀点",
                StashItemKey = ButcherStation.PointItemKey,
                Begin = BeginButcherPointPlacement,
                IsPlacing = () => _placingButcherPoint,
                TryPlace = TryPlaceButcherPoint,
                Cancel = EndButcherPointPlacement,
                CancelToast = "算了，宰杀点先收着。",
                Match = key => key == ButcherStation.PointFurnitureKey,
                IsSolid = true,
                Respawn = (_, rect) => SpawnButcherPoint(rect),
            },
            // ── 以下四种是"配方完工直接立起"的锚定实心设施：不进库存、无放置模式（StashItemKey/IsPlacing 留空），
            //    但一样要读档复原 + 读档前清场，故进表 ──
            new()
            {
                TypeName = "宰杀台",
                Match = key => key == ButcherStation.TableFurnitureKey,
                IsSolid = true,
                Respawn = (_, rect) => SpawnButcherTable(rect),
            },
            new()
            {
                TypeName = "烹饪台",
                Match = key => key == CookStation.PropName,
                IsSolid = true,
                Respawn = (_, rect) => SpawnCookStation(rect),
            },
            new()
            {
                TypeName = "改装台",
                Match = key => key == WeaponModLogic.BenchFurnitureKey,
                IsSolid = true,
                Respawn = (_, rect) => SpawnModBench(rect),
            },
        };

        // ── 焊死两份事实源 ──：库存「摆放」按钮读 PlaceableItems（纯逻辑）、本表驱动分派。
        // 两份都从同一批 Spec 常量取 key，但"谁忘补哪一份"是本项目反复踩的坑（纯逻辑绿≠功能生效）。
        // 这里当场核对：本表里"能进库存摆放"的 key 集合，必须与 PlaceableItems.All 逐一对上。
        // 不一致 ⇒ 编辑器控制台一条刺眼的错误（只在真分叉时出现），逼下一个加家具的人两边一起补。
        var registryStashKeys = _placeables
            .Where(d => d.StashItemKey is not null)
            .Select(d => d.StashItemKey!)
            .ToHashSet();
        if (!registryStashKeys.SetEquals(PlaceableItems.All.ToHashSet()))
        {
            GD.PushError(
                "[家具注册表] StashItemKey 集合与 PlaceableItems.All 不一致 —— 有人只补了一份。" +
                $"表内：{string.Join(",", registryStashKeys.OrderBy(k => k))}；" +
                $"PlaceableItems：{string.Join(",", PlaceableItems.All.OrderBy(k => k))}");
        }
    }

    /// <summary>
    /// 库存「摆放」→ 进入对应家具的放置模式（由 <see cref="OnStashPlaceRequested"/> 遍历本表分派）。
    /// 命中即调该家具的 <see cref="PlaceableFurnitureDef.Begin"/>（= 既有的 <c>BeginXPlacement</c>），未命中返回 false。
    /// </summary>
    private bool TryBeginPlacementFor(string key)
    {
        foreach (PlaceableFurnitureDef def in _placeables)
        {
            if (def.StashItemKey == key)
            {
                def.Begin!();
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 一种<b>持久化的玩家可摆放家具</b>的登记项 —— 把该类型散落在各分派链里的四件事收成一处：
/// 摆放入口（<see cref="Begin"/>）、放置模式鼠标（<see cref="IsPlacing"/>/<see cref="TryPlace"/>/<see cref="Cancel"/>）、
/// 实例识别（<see cref="Match"/>）、读档复原（<see cref="Respawn"/>）。
/// <para>
/// <b>只装委托、不装正文</b>：委托全指向 <see cref="CampMain"/> 里既有的逐类型方法（见 <c>CampMain.Placeables.cs</c> 类注）。
/// 本类是纯数据容器，逻辑一行都没有。
/// </para>
/// </summary>
public sealed class PlaceableFurnitureDef
{
    /// <summary>中文类型名（"圈套陷阱" / "宰杀台"）—— 仅用于诊断/日志，不做逻辑键。</summary>
    public string TypeName { get; init; } = "";

    /// <summary>
    /// 库存里触发"摆放"的物品 key（<c>null</c> = 不进库存的锚定设施，如烹饪台/改装台/宰杀台——由配方完工直接立起）。
    /// 非空者必与 <see cref="PlaceableItems.All"/> 对得上（在 <see cref="CampMain.BuildPlaceables"/> 里当场核对）。
    /// </summary>
    public string? StashItemKey { get; init; }

    /// <summary>进入放置模式（= 既有 <c>BeginXPlacement</c>）。仅库存可摆放者有；锚定设施留 <c>null</c>。</summary>
    public Action? Begin { get; init; }

    /// <summary>当前是否正处于本家具的放置模式（= 读既有 <c>_placingX</c> 标志）。锚定设施留 <c>null</c>。</summary>
    public Func<bool>? IsPlacing { get; init; }

    /// <summary>放置模式左键落位（= 既有 <c>TryPlaceX</c>；入参是反投影后的 cartesian 坐标）。</summary>
    public Action<Vector2>? TryPlace { get; init; }

    /// <summary>放置模式右键作罢（= 既有 <c>EndXPlacement</c> 或就地清标志）。之后弹 <see cref="CancelToast"/>。</summary>
    public Action? Cancel { get; init; }

    /// <summary>右键作罢时的一行提示（"算了，陷阱先收着。"，走 <c>CampToast.Ok</c>）。</summary>
    public string? CancelToast { get; init; }

    /// <summary>
    /// 判断一个 <c>_furniture</c> 实例 key 是不是本类型（= 既有前缀/名字谓词，如 <c>TrapSpec.IsTrapFurniture</c>）。
    /// 各类型的谓词互斥（实例名空间不相交）—— 读档复原、读档前清场都靠它分派。
    /// </summary>
    public Func<string, bool> Match { get; init; } = _ => false;

    /// <summary>
    /// 实心（建碰撞体 + 挖导航洞 ⇒ 真挡路）。读档复原时决定要不要在末尾重烘焙一次导航
    /// （非实心家具不改寻路图，立回去不用重烘焙）。
    /// </summary>
    public bool IsSolid { get; init; }

    /// <summary>读档：把一件本类型家具按存档里的实例名 + 原位置立回场上（= 既有 <c>RespawnX</c>/<c>SpawnX</c>）。</summary>
    public Action<string, Rect2> Respawn { get; init; } = (_, _) => { };
}
