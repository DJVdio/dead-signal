using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>卧床养病的消费层接线</b>（批次21·impl-bedrest）——「控制角色上床养病和吃药」+「白天睡觉也要算治疗加成」。
///
/// <para>
/// 规则本身在 <see cref="BedrestLogic"/> / <see cref="BedRegistry"/> / <see cref="RestLedger"/> / <see cref="BedSpec"/>
/// （纯逻辑、零 Godot 依赖、单测覆盖）；<b>本文件只做空间执行</b>：把床立在场上、让人走过去躺下、按相位记账。
/// </para>
///
/// <para>
/// <b>为什么单独开一个 partial 文件</b>：<c>CampMain.cs</c> 是并发热点（照 <see cref="CampMain.Placement"/> 那个文件开的先例）。
/// 本块基本是自成一体的新增能力，故只在 <c>CampMain.cs</c> 里留几个**一行的调用点**，正文全在这儿 —— 谁都不用等谁。
/// </para>
///
/// <para><b>交互范式零发明</b>：床就是一个 <c>role="bed"</c> 的可点击容器 —— 选中角色 → 右键点床 → 走过去躺下
/// （复用既有的 <c>_pendingInteract → ExecuteContainerInteract</c> 那条链，和搜柜子/开门/搜尸体走的是同一条路）。
/// 再点一次他自己的床 = 起床。造床走沙袋那条链（配方 → 库存「摆放」→ 左键落位）。</para>
/// </summary>
public sealed partial class CampMain
{
    /// <summary>营地床位登记册（一人一床、一床一人）。建图时把 camp.json 的床登记进来，玩家造的床随建随登。</summary>
    private readonly BedRegistry _beds = new();

    /// <summary>玩家造出来的床的命名序号（"床#3" 起；开局那两张在 camp.json 里叫 床#1/床#2）。</summary>
    private int _bedSeq = 2;

    /// <summary>正处于"摆放床"模式（左键落位、右键取消）。同沙袋/改装台。</summary>
    private bool _placingBed;

    /// <summary>
    /// 已记账到哪个相位 —— 即"各 Pawn 当前的 <see cref="Pawn.Role"/> 属于哪个相位"。
    /// <para>
    /// <b>为什么需要它</b>：<see cref="OnGamePhaseChanged"/> 触发时，<see cref="PawnRoleManager"/> <b>还没</b>重排角色
    /// （CampMain 在 <c>_clock.OnPhaseChanged</c> 上先订阅，见 CampMain.cs:511 vs :661；CampMain.cs:5177 的注释也点了这一点）。
    /// 所以那一刻读到的 <c>p.Role</c> 是**上一个相位**的角色 —— 正好拿来给刚过去的那个相位记账。
    /// </para>
    /// </summary>
    private DayPhase _restLedgerPhase = DayPhase.DawnMeal;

    /// <summary>床的放置规格（喂 <see cref="CheckFurniturePlacement"/>）。<b>非实心</b>——人得走得上去躺下。</summary>
    private static readonly PlaceableSpec BedPlaceSpec =
        new(BedSpec.FurnitureKey, BedSpec.Width, BedSpec.Height, IsSolid: BedSpec.IsSolid);

    // ──────────────────────────────── 建图：把床立在场上 ────────────────────────────────

    /// <summary>
    /// camp.json 里 <c>role="bed"</c> 的 prop → 场上的床。<b>非实心</b>（不建碰撞体、不挖导航洞，同 seat/尸体）：
    /// 床要是实心的，伤员就被自己的床挡在外面了。登记为可点击容器 ⇒ 右键前往躺下。
    /// <para>由 <c>CampMain.cs</c> 的 prop 装载循环调用（那儿只有一行 <c>else if (pr.role == "bed")</c>）。</para>
    /// </summary>
    private void AddBedProp(PropSpec pr, Rect2 rect)
    {
        SpawnBed(pr.name!, rect);
        // camp.json 的开局床叫 床#1/床#2 → 把序号推到它们之后，免得玩家造的第一张床和它们重名。
        _bedSeq = Mathf.Max(_bedSeq, SeqOf(pr.name!));
    }

    /// <summary>玩家造的床落位：建视觉 + 登记容器 + 进床位册 + 进可拆家具账。<b>不动导航</b>（非实心）。</summary>
    private void PlaceBedAt(Vector2 cart)
    {
        string name = $"{BedSpec.FurnitureKey}#{++_bedSeq}";
        var rect = new Rect2(
            cart - new Vector2(BedSpec.Width / 2f, BedSpec.Height / 2f),
            new Vector2(BedSpec.Width, BedSpec.Height));
        SpawnBed(name, rect);
        _campToast.Show($"{name} 铺好了。谁需要，就叫他去躺着。", CampToast.Ok);
    }

    /// <summary>读档：把玩家造的那张床原地立回来（位置由存档给定，不重新分配名字）。</summary>
    private void RespawnPlayerBed(string name, Rect2 rect)
    {
        SpawnBed(name, rect);
        _bedSeq = Mathf.Max(_bedSeq, SeqOf(name)); // 序号推到它之后，免得下次造床重名
    }

    /// <summary>把一张床立到场上（新造/读档共用）：视觉 + 可点击容器 + 床位册 + 可拆家具账。<b>不建碰撞体、不挖导航洞。</b></summary>
    private void SpawnBed(string name, Rect2 rect)
    {
        var style = new PixelStyle { color = new double[] { 0.46, 0.38, 0.36 }, jitter = 0.08 };
        var visuals = new List<Node2D>();
        AddOccluderVisual(rect, style, seed: 23, height: 10f, cell: 40f, visuals);

        _containers.Add(new ContainerRef { Name = name, Rect = rect, Role = "bed" });
        _beds.AddBed(name);
        _furniture[name] = new FurnitureInstance { Rect = rect, Body = null, Visuals = visuals };
    }

    /// <summary>床实例名（"床#3"）里的序号；解不出来给 0。</summary>
    private static int SeqOf(string bedKey)
        => bedKey.StartsWith(BedSpec.FurnitureKey + "#")
           && int.TryParse(bedKey[(BedSpec.FurnitureKey.Length + 1)..], out int n) ? n : 0;

    /// <summary>
    /// 拆掉的家具要是一张床 → 从床位册注销 + 可点击容器注销。<b>躺在上面的人被赶下来</b>
    /// （<see cref="BedRegistry.RemoveBed"/> 连带清占用）：他改打地铺 —— 仍在休养，只是不再吃睡床加成。
    /// <para>由 <c>CampMain.RemoveFurniture</c> 调用（拆除的唯一出口，故不会漏）。非床的家具进来是无操作。</para>
    /// </summary>
    private void RemoveBedIfAny(string furnitureName)
    {
        if (!furnitureName.StartsWith(BedSpec.FurnitureKey + "#"))
        {
            return;
        }

        int? sleeper = _beds.OccupantOf(furnitureName);
        _beds.RemoveBed(furnitureName);
        _containers.RemoveAll(c => c.Name == furnitureName); // 床没了，那个可点击的点必须跟着消失

        if (sleeper is int id && _survivors.FirstOrDefault(s => s.Id == id) is { } p)
        {
            // 他还在养病（BedrestOrdered 不动）——只是从今往后睡地板。别偷偷把他的令也撤了。
            _campToast.Show($"{furnitureName} 被拆了，{p.DisplayName} 只能睡地铺了。", CampToast.Bad);
        }
    }

    /// <summary>
    /// 库存面板点「摆放」一张床 → 进入放置模式（左键落位、右键取消）。走沙袋/改装台那条既有链，不发明新范式。
    /// 由 <c>CampMain.cs</c> 的 <c>OnStashPlaceRequested</c> 一行分发过来。
    /// </summary>
    private void BeginBedPlacement()
    {
        if (_inventory.MaterialCount(BedSpec.ItemKey) <= 0)
        {
            _campToast.Show("库里没有床——先去工作台造一张。", CampToast.Bad);
            return;
        }
        _placingBed = true;
        BeginFurniturePlacement(BedPlaceSpec); // impl-placement 白送的绿/红落位预览
        CloseStash();
    }

    /// <summary>放置模式下左键落位。拒绝时**不退出放置模式**（换个地方接着点，同沙袋范式）。</summary>
    private void TryPlaceBed(Vector2 cart)
    {
        if (!CheckFurniturePlacement(BedPlaceSpec, cart))
        {
            return; // 拒绝提示已由 CheckFurniturePlacement 弹过
        }
        if (!_inventory.TrySpendMaterial(BedSpec.ItemKey, 1))
        {
            _campToast.Show("库里没有床——先去工作台造一张。", CampToast.Bad);
            EndBedPlacement();
            return;
        }
        PlaceBedAt(cart);
        EndBedPlacement();
    }

    /// <summary>退出摆放床模式（落位成功 / 右键作罢 都走这儿）。</summary>
    private void EndBedPlacement()
    {
        _placingBed = false;
        EndFurniturePlacement();
    }

    // ──────────────────────────────── 交互：躺下 / 起床 ────────────────────────────────

    /// <summary>
    /// 走到床前之后干什么（由 <see cref="ExecuteContainerInteract"/> 的 "bed" 分支调用）。
    /// <list type="bullet">
    /// <item>点的是**他自己正躺着的那张床** → 起床（撤令 + 退床）。</item>
    /// <item>否则 → 躺下养病（占这张床 + 下卧床令），代价是**夜里不站岗、不生产、不读书**。</item>
    /// </list>
    /// 拒绝理由一律**说人话**（照 SiteActionOption 的规矩：不藏选项，说明为什么）。
    /// </summary>
    private void ExecuteBedInteract(Pawn arriver, ContainerRef hit)
    {
        // 再点一次自己的床 = 起来。
        if (arriver.BedrestOrdered && _beds.BedOf(arriver.Id) == hit.Name)
        {
            WakeFromBedrest(arriver);
            return;
        }

        BedrestOrder order = BedrestLogic.CanOrderBedrest(
            arriver.Alive, arriver.Role, _clock.CurrentPhase,
            hasOwnBed: _beds.HasBed(arriver.Id), freeBeds: _beds.FreeBeds);
        if (!order.Allowed)
        {
            _campToast.Show(order.Message, CampToast.Bad);
            return;
        }

        if (!_beds.TryClaimSpecific(hit.Name, arriver.Id))
        {
            int? holder = _beds.OccupantOf(hit.Name);
            string who = holder is int hid
                ? _survivors.FirstOrDefault(s => s.Id == hid)?.DisplayName ?? "别人"
                : "别人";
            _campToast.Show($"{who}已经躺在{hit.Name}上了。一张床只睡一个人。", CampToast.Bad);
            return;
        }

        arriver.SetBedrest(true);
        arriver.Role = PawnRole.Bedrest;
        // 躺着的人不站岗、不读书 —— 把他从夜间指派里摘掉，别让岗位空着还不告诉玩家。
        ReleaseNightAssignmentsOf(arriver);

        string cost = BedrestLogic.CostsNightShift(_clock.CurrentPhase)
            ? "——今晚他不站岗也不干活"
            : "";
        _campToast.Show($"{arriver.DisplayName} 躺到{hit.Name}上养病{cost}。", CampToast.Ok);
    }

    /// <summary>
    /// <b>手术池床位加成的唯一真值来源。</b>具体数值以 Wiki 配置为准。
    /// <para>
    /// 医疗面板的「床上」是**展示层**（它自己也是从 <see cref="BedRegistry"/> 同步来的，见 <c>MedicalPanel.SyncBedCheck</c>），
    /// 但展示层不该是权威 —— 手术真要给 +10 池（<see cref="HealthConditionSet.BedBonusPoints"/>），
    /// 就得**病人真的躺在一张真实存在的床上**。所以三个手术 handler 一律不信 UI 传来的那个 bool，就地问床位册。
    /// </para>
    /// <para>这条把"先叫他去躺下"变成了动刀前一个**真实的准备动作**，而不是一次勾选。</para>
    /// </summary>
    private bool RealOnBed(Pawn patient) => patient is not null && _beds.HasBed(patient.Id);

    /// <summary>
    /// 玩家给一个正躺着养病的人下了别的令 ⇒ 把他叫起来（规则在 <see cref="BedrestLogic.WakesOnCommand"/>）。
    /// <para><b>点床除外</b>：那是养病流程自己的入口（点自己那张=起床、点别人的空床=换床躺），
    /// 由 <see cref="ExecuteBedInteract"/> 判——在这里叫醒会让"点自己的床=起床"退化成"起床→走过去→又躺下"。</para>
    /// </summary>
    /// <param name="targetContainerRole">这条令的目标容器 role；null = 地面移动令。</param>
    private void WakeIfBedrest(Pawn? p, string? targetContainerRole)
    {
        if (p is { BedrestOrdered: true } && BedrestLogic.WakesOnCommand(targetContainerRole))
        {
            WakeFromBedrest(p);
        }
    }

    /// <summary>叫他起来：撤卧床令 + 退床（床空出来给别人）。角色由 <see cref="PawnRoleManager"/> 在下个相位重排。</summary>
    private void WakeFromBedrest(Pawn p)
    {
        p.SetBedrest(false);
        _beds.Release(p.Id);
        if (p.Role == PawnRole.Bedrest)
        {
            p.Role = PawnRole.Idle;
        }
        _campToast.Show($"{p.DisplayName} 起来了。", CampToast.Ok);
    }

    /// <summary>把某人从守卫/读书指派里摘掉（他要躺着养病了，岗位得腾给别人）。</summary>
    private void ReleaseNightAssignmentsOf(Pawn p)
    {
        var guards = _roleManager.GuardAssignments
            .Where(kv => kv.Value != p.Id)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        if (guards.Count != _roleManager.GuardAssignments.Count)
        {
            _roleManager.SetGuardAssignments(guards);
        }

        if (_roleManager.ReadingAssignments.ContainsKey(p.Id))
        {
            var reading = _roleManager.ReadingAssignments
                .Where(kv => kv.Key != p.Id)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            _roleManager.SetReadingAssignments(reading);
        }
    }

    /// <summary>床的悬停提示：谁躺着 / 空着 / 该右键干什么。</summary>
    private string BedHoverText(ContainerRef c, bool hasSelection)
    {
        string noSel = hasSelection ? "" : "（先选中角色）";
        int? holder = _beds.OccupantOf(c.Name);
        if (holder is int id)
        {
            Pawn? p = _survivors.FirstOrDefault(s => s.Id == id);
            string who = p?.DisplayName ?? "有人";
            return p is not null && p.BedrestOrdered
                ? $"{c.Name}（{who}正躺着养病）· 选中他右键点这张床=叫他起来{noSel}"
                : $"{c.Name}（{who}在睡）{noSel}";
        }
        return $"{c.Name}（空着）· 选中角色后右键前往躺下养病 —— 躺着的人夜里不站岗、不生产{noSel}";
    }

    // ──────────────────────────────── 记账：白天睡觉也算治疗加成 ────────────────────────────────

    /// <summary>
    /// <b>给刚过去的那个相位记一笔休养账。</b>在 <see cref="OnGamePhaseChanged"/> 的**最开头**调用 ——
    /// 那一刻 <see cref="PawnRoleManager"/> 还没重排角色，<c>p.Role</c> 仍是**离场相位**的角色（见 <see cref="_restLedgerPhase"/> 的注释）。
    ///
    /// <para><b>这就是"白天睡觉吃到治疗加成"的落点</b>：留守者白天被日程强制 <see cref="PawnRole.Sleeping"/>
    /// （PawnRoleManager.cs:74,82），那三个相位在这里逐个进账 ⇒ 黎明结算时休养占比 ≥ 3/6。
    /// 旧模型整日只取一个布尔、且在黎明读到的是**昨夜**的角色 ⇒ 白天睡整天等于白睡。</para>
    ///
    /// <para><b>床位分配</b>：卧床养病者**独占**一张床（显式右键占的，别人抢不走 —— 这是床稀缺的代价）；
    /// 日程强制睡眠者在记账时**临时认领一张空床**，记完就腾出来（TryClaim 只拿空床，抢不到就打地铺）。</para>
    /// </summary>
    private void RecordRestForOutgoingPhase()
    {
        if (!BedrestLogic.CountsTowardLedger(_restLedgerPhase))
        {
            return; // 聚餐是模态过渡（全员爬起来吃饭），不计入 —— 计进去只会把占比一起稀释，凭空惩罚所有人
        }

        foreach (Pawn p in _survivors)
        {
            if (!p.Alive)
            {
                continue;
            }

            bool hasBed = _beds.HasBed(p.Id);
            if (!hasBed && p.Role == PawnRole.Sleeping)
            {
                hasBed = _beds.TryClaim(p.Id) != null; // 日程强制睡：临时占一张空床；没空床就打地铺
            }

            p.Rest.Record(BedrestLogic.QualityFor(p.Role, hasBed));

            if (!p.BedrestOrdered)
            {
                _beds.Release(p.Id); // 只有卧床养病者长期占床；睡完就把床腾出来
            }
        }
    }

    /// <summary>本昼夜该幸存者的休养/睡床占比（喂 <c>Pawn.AdvanceHealthDay</c>）。黎明结算后由调用方 <c>Rest.Reset()</c> 清账。</summary>
    private static (double RestFraction, double BedFraction) RestArgsFor(Pawn p)
        => (p.Rest.RestFraction, p.Rest.BedFraction);

    // ──────────────────────────────── 存档 ────────────────────────────────

    /// <summary>存档：床位占用 + 床命名序号（床本体在 CampSave.Furniture 里，同其它家具）。</summary>
    private void CaptureBedSave(CampSave s)
    {
        s.BedOccupancy = _beds.Occupancy.ToDictionary(kv => kv.Key, kv => kv.Value);
        s.BedSeq = _bedSeq;
    }

    /// <summary>读档：床位占用还原（床本体已由家具还原流程立回场上并 <see cref="BedRegistry.AddBed"/> 过）。</summary>
    private void ApplyBedSave(CampSave s)
    {
        _bedSeq = Mathf.Max(_bedSeq, s.BedSeq);
        _beds.RestoreOccupancy(s.BedOccupancy);
    }
}
