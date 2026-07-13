using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 DoorLogic.cs / DoorSecurityLogic.cs / VisionLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（真把商人放到那儿、登记交互点、判可达）归 CampMain.SpawnMerchant，本文件只出**纯几何**。

/// <summary>
/// <b>神秘商人停在大门外</b>（用户拍板原话：「商人停在门外，你得开门」）。
///
/// <para>
/// <b>它修的 bug</b>：商人此前生成在南门外、却 <c>CommandMoveTo</c> 到<b>营心</b>。而营地围栏是**闭合矩形**、
/// 唯二缺口就是两道大门 ⇒ 他寻路根本进不来，只会卡在门外发呆。
/// （这个 bug <b>比门系统更老</b>：门系统之前大门是纯实心可破坏结构，他一样进不来——只是从没人发现。）
/// </para>
///
/// <para>
/// <b>⚠️ 修它有一个「看起来像一行修复、实则会毁掉门闩」的诱惑</b>：给 <see cref="DoorLogic.CanOpen"/> 的
/// <see cref="DoorState.Barred"/> 分支加一句 <c>|| faction == Faction.Neutral</c>，让商人自己推门进来。
/// <b>绝不能那么干</b>：
/// <list type="number">
/// <item><b>概念上直接塌</b> —— 门闩是**从里面插的横木**，一个站在门外的陌生人凭什么抬得起来？那还叫闩吗？</item>
/// <item><b>它是颗待引爆的雷</b> —— <c>Faction.Neutral</c> 现在只有商人，但它是个**开放阵营**。哪天谁加一个
/// 中立 NPC（流浪者？难民？反水前的克莉丝汀？），它就<b>自动获得了推开营地大门的权限</b>，
/// 而这条规则藏在 <c>DoorLogic</c> 深处，没人会记得。</item>
/// </list>
/// <b>正解：动商人的停留点，一个字都不动门。</b>（护栏见 <c>MerchantStandTests.中立阵营永远开不了闩着的门_这是那颗雷的护栏</c>）
/// </para>
///
/// <para>
/// <b>玩法后果（这才是重点，别磨平）</b>：商人半夜在门外摆摊，你想做生意 → <b>得开那扇闩着的大门</b>。
/// 开门声 100（不分阵营）招来附近闲逛的东西；<b>大门敞开的那几秒，营地没有防线</b>；
/// 而你派去谈生意的人，此刻站在墙外。<b>你可能为了买两发子弹，放进来三只丧尸。</b>
/// 他<b>不会走进来</b>——一个谨慎的商人不会大摇大摆走进一个持枪的营地。想做生意，你出来。
/// </para>
/// </summary>
public static class MerchantStand
{
    /// <summary>
    /// 商人站在大门外多远（像素）。数值「拟定待调」。
    /// <para>
    /// <b>要够近</b>：玩家一开门就该看见他、派个人走几步就能谈生意——停太远，"开门做生意"就变成"出门远征"了。
    /// <b>也要真在门外</b>：不能压在门板上（门一关会把他夹进去）。60px ≈ 一个身位多一点。
    /// </para>
    /// </summary>
    public const double GateStandoff = 60;

    /// <summary>
    /// 大门外的商人停留点：从**门中心**沿「朝外」方向推出 <paramref name="standoff"/> 像素。
    ///
    /// <para>
    /// <b>「外」由营心决定，不硬编码 south/north</b>：大门是个扁矩形（薄的那一维就是墙的厚度方向），
    /// 沿**薄轴**、朝**背离营心**的一侧推出去，就是门外。南门推向南、北门推向北，一个公式两头都对
    /// （单测两头都钉了）。日后加东/西大门也不用改这里。
    /// </para>
    ///
    /// <para>
    /// 停留点在门的**正前方**（与门中心同轴）——玩家一开门就看见他，不用满地找人。
    /// 且它与营地内部之间**恰好隔着大门那个矩形** ⇒ 门闩着时，玩家的人<b>寻路走不过去</b>。
    /// 这就是"你得开门"的几何前提。
    /// </para>
    /// </summary>
    /// <param name="gx">大门 rect 的 x。</param>
    /// <param name="gy">大门 rect 的 y。</param>
    /// <param name="gw">大门 rect 的宽。</param>
    /// <param name="gh">大门 rect 的高。</param>
    /// <param name="campCx">营地中心 x（用来定"哪边是外"）。</param>
    /// <param name="campCy">营地中心 y。</param>
    /// <param name="standoff">推出多远（<see cref="GateStandoff"/>）。</param>
    public static (double x, double y) OutsideGate(
        double gx, double gy, double gw, double gh,
        double campCx, double campCy,
        double standoff)
    {
        double mx = gx + gw / 2, my = gy + gh / 2; // 门中心

        // 大门是扁的：宽 > 高 ⇒ 它是一段横墙（南/北门），"外"在 y 方向；反之是竖墙（东/西门），"外"在 x 方向。
        bool horizontal = gw >= gh;

        if (horizontal)
        {
            double outward = my >= campCy ? 1 : -1;                  // 门在营心之南 → 朝南推；之北 → 朝北推
            double edge = outward > 0 ? gy + gh : gy;                // 门的外沿
            return (mx, edge + outward * standoff);
        }
        else
        {
            double outward = mx >= campCx ? 1 : -1;                  // 门在营心之东 → 朝东推；之西 → 朝西推
            double edge = outward > 0 ? gx + gw : gx;
            return (edge + outward * standoff, my);
        }
    }
}
