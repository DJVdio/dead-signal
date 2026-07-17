using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>神秘商人停在大门外 —— 你得开门才能做生意</b>（用户拍板原话：「商人停在门外，你得开门（推荐）」）。
///
/// <para>
/// <b>它把商人从"便利设施"变成一次真实的风险决策</b>：他半夜来了，你想做生意 → <b>得开那扇闩着的大门</b>；
/// 开门声 100（<c>NoiseKind.Combat</c>，不分阵营）会招来附近闲逛的东西；<b>大门敞开的那几秒，营地是没有防线的</b>；
/// 而你派出去谈生意的那个人，此刻站在墙外。<b>你可能为了买两发子弹，放进来三只丧尸。</b>
/// 这正是门系统该有的张力 —— 别为了"流畅"把它磨平。
/// </para>
///
/// <para>
/// <b>⚠️ 本类最重要的一条测试是 <see cref="中立阵营永远开不了闩着的门_这是那颗雷的护栏"/></b>。
/// 修这个 bug 有一个<b>看起来像"一行修复"、实则会毁掉门闩</b>的诱惑：给 <c>DoorLogic.CanOpen</c> 的 Barred 分支
/// 加一句 <c>|| faction == Faction.Neutral</c>，让商人自己推门进来。<b>绝不能那么干</b>——理由见该测试。
/// 正解是<b>动商人的停留点，一个字都不动门</b>。
/// </para>
/// </summary>
public class MerchantStandTests
{
    // camp.json 的营地布局（围栏围成的闭合矩形；南北大门是唯二缺口）。
    // ⚠️ 这是【手抄副本，没有焊缝】——本文件从头到尾没有读过 godot/data/camp.json。
    //    核对时点 2026-07-17：与 camp.json `_layoutComment`「外沿 x∈[300,2100]、y∈[300,1500]、围栏厚 22、
    //    南北在 x∈[1100,1300] 各留 200px 缺口」一致（内沿 = 外沿 ∓ 22；营心 = 两轴中点）。
    // 🔴 但"当前一致"靠的是人手核对，不是机器：改了 camp.json 而不改这里，**下面的护栏一条都不会红**，
    //    却已经在量一个不存在的营地（同 StuartManor / GoldfingerGang 的画布副本脱焊陷阱）。
    //    正解是照 RealCampCoverTests.CampJsonPath() 补一条"副本 == 真源"的焊缝测试。见 journal [HANDOFF]。
    private const double CampMinX = 322, CampMaxX = 2078, CampMinY = 322, CampMaxY = 1478;
    private const double CampCx = 1200, CampCy = 900;                       // 营心 = ([300,2100],[300,1500]) 两轴中点
    private static readonly (double x, double y, double w, double h) SouthGate = (1100, 1478, 200, 22);
    private static readonly (double x, double y, double w, double h) NorthGate = (1100, 300, 200, 22);

    private static bool InsideCamp(double x, double y)
        => x > CampMinX && x < CampMaxX && y > CampMinY && y < CampMaxY;

    // ---------------- 那颗雷（最重要的一条） ----------------

    [Fact]
    public void 中立阵营永远开不了闩着的门_这是那颗雷的护栏()
    {
        // 【为什么这条测试必须存在】
        // 「商人进不了营地」这个 bug 有一个**诱人的一行修复**：让 Faction.Neutral 也能开闩着的门。
        // **那会毁掉门闩**：
        //  ① 概念上直接塌 —— 门闩是**从里面插的横木**，一个站在门外的陌生人凭什么抬得起来？那还叫闩吗？
        //  ② 它是颗**待引爆的雷** —— Faction.Neutral 现在只有商人，但它是个**开放阵营**。
        //     哪天谁加一个中立 NPC（流浪者？难民？反水前的克莉丝汀？），它就**自动获得了推开营地大门的权限**，
        //     而这条规则藏在 DoorLogic 深处，没人会记得。
        // 正解：**动商人的停留点，一个字都不动门。**
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Neutral, isAnimal: false));
        Assert.False(DoorLogic.CanPick(DoorState.Barred, Faction.Neutral, isAnimal: false, lockpickCount: 99));

        // 顺带把整张表钉死：闩着的门，**只有自己人**抬得起。
        Assert.True(DoorLogic.CanOpen(DoorState.Barred, Faction.Survivor, isAnimal: false));
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Raider, isAnimal: false));
        Assert.False(DoorLogic.CanOpen(DoorState.Barred, Faction.Zombie, isAnimal: false));
    }

    // ---------------- 商人停在门外 ----------------

    [Fact]
    public void 商人停留点在营地外_而不是营地里()
    {
        // 这就是那个老 bug 的正面护栏：此前商人的停留点是**营心**，而围栏是闭合矩形、
        // 唯二缺口是两道大门 —— 他寻路根本进不来，只会卡在门外发呆。
        (double x, double y) = MerchantStand.OutsideGate(
            SouthGate.x, SouthGate.y, SouthGate.w, SouthGate.h, CampCx, CampCy, MerchantStand.GateStandoff);

        Assert.False(InsideCamp(x, y), $"商人停留点 ({x},{y}) 落在营地里了——他进不来，会卡在门外");
        Assert.True(y > CampMaxY, "南门外 = 围栏南沿之外");
    }

    [Fact]
    public void 商人停在门正前方_不偏不倚()
    {
        // 停在门的正对面：玩家一开门，一眼就看见他。偏了的话玩家开了门还得满地找人。
        (double x, double y) = MerchantStand.OutsideGate(
            SouthGate.x, SouthGate.y, SouthGate.w, SouthGate.h, CampCx, CampCy, MerchantStand.GateStandoff);
        Assert.Equal(SouthGate.x + SouthGate.w / 2, x, precision: 6); // 与门中心同一条竖线
    }

    [Fact]
    public void 北门对称成立_朝外就是朝北()
    {
        // "外"由营心决定，不靠硬编码 south/north —— 北门的"外"是 y 更小的一侧。
        (double x, double y) = MerchantStand.OutsideGate(
            NorthGate.x, NorthGate.y, NorthGate.w, NorthGate.h, CampCx, CampCy, MerchantStand.GateStandoff);

        Assert.False(InsideCamp(x, y));
        Assert.True(y < CampMinY, "北门外 = 围栏北沿之外");
        Assert.Equal(NorthGate.x + NorthGate.w / 2, x, precision: 6);
    }

    [Fact]
    public void 商人就站在门口_不站到天边去()
    {
        // 他得**够得着**：玩家开了门，派个人走几步就能谈生意。停太远就变成"出门远征"了。
        (double x, double y) = MerchantStand.OutsideGate(
            SouthGate.x, SouthGate.y, SouthGate.w, SouthGate.h, CampCx, CampCy, MerchantStand.GateStandoff);

        double gateOuterEdgeY = SouthGate.y + SouthGate.h; // 门的外沿
        double gap = y - gateOuterEdgeY;
        Assert.True(gap > 0, "得真的在门外");
        Assert.True(gap <= 120, $"离门 {gap}px 太远了——玩家开门后应该一步就走到");
    }

    [Fact]
    public void 站位隔着门_所以大门闩着时人走不过去()
    {
        // 本机制的**几何前提**：商人停留点与营地内部之间，**隔着大门那个矩形**。
        // 若不隔着（比如他站在围栏的某个缺口外），玩家不开门也能绕过去，整个取舍就没了。
        (double mx, double my) = MerchantStand.OutsideGate(
            SouthGate.x, SouthGate.y, SouthGate.w, SouthGate.h, CampCx, CampCy, MerchantStand.GateStandoff);

        // 营心 → 商人 的连线必然穿过南门的 x 跨度（他在门正前方，门是唯一的洞）
        Assert.InRange(mx, SouthGate.x, SouthGate.x + SouthGate.w);
        Assert.True(my > SouthGate.y, "商人在门的外侧，营心在门的内侧 —— 门夹在中间");
    }

    [Fact]
    public void 停留距离是数据_不是魔数()
    {
        // 数值「拟定待调」，但**必须为正**（0 = 站在门板里）。
        Assert.True(MerchantStand.GateStandoff > 0);
    }

    // ---------------- 商人在门外安不安全（主 agent 点名要答的） ----------------

    [Fact]
    public void 商人在门外不会被咬死_中立阵营谁都不打他_接替链不会被耗尽()
    {
        // 【主 agent 的担心：商人在门外被丧尸/劫掠者打死 → MerchantLineage 接替链快速耗尽 → 永久断商】
        // **结构性答案：不会。** Faction.Neutral 与**任何**阵营都不敌对（用户拍板的敌对矩阵），
        // 而全仓所有择敌都走 Factions.IsHostile ⇒ **丧尸和劫掠者永远不会把他当目标**。
        // 他站在门外，和站在营地正中一样安全 —— 因为**哪儿都没人打他**。
        Assert.False(Factions.IsHostile(Faction.Zombie, Faction.Neutral));
        Assert.False(Factions.IsHostile(Faction.Raider, Faction.Neutral));
        Assert.False(Factions.IsHostile(Faction.Survivor, Faction.Neutral));
        Assert.False(Factions.IsHostile(Faction.Neutral, Faction.Zombie));

        // ⇒ 接替链只有**玩家亲手杀他**才会推进（打劫商人本来就是设计意图，不是意外）。
        // 所以"把他挪到门外"这件事，对接替链的风险是 **0**。真正的风险在**玩家自己派出去的那个人**身上：
        // 门开着、他在墙外 —— 那正是这单要的张力。
    }
}
