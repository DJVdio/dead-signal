using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 WeaponLoadout.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 手持光源的「占一只手」态。见 journal [DECISION] light-items：手电非武器，不塞进严格 Weapon 型的
// WeaponLoadout(避免污染 GripMode/攻速/误差角)，另立本轻量态。校验读 WeaponLoadout 的公开事实
// (断手 / 双手武器占两手 / 该手已持械)，**不**改 WeaponLoadout、不耦合 Godot，保持纯逻辑可测。
//
// 规则(拟定，对齐 SPEC-B4「占一只手；双手武器与光源互斥；持光源=另一手只能单手武器」)：
//   · 占用一只手；该手断 → 拒绝；
//   · 双手武器占两手(TwoHandGrip) → 与光源互斥，拒绝；
//   · 该手已握单手武器 → 拒绝(光源需一只空手)；
//   · 允许：一手持光 + 另一手一把单手武器；
//   · 不可能：双持两把单手武器同时持光(两手皆被武器占，无空手)——自然从「该手已持械」推出。
// 反向约束(已持光时装武器应被挡)由 <see cref="BlocksWeaponEquip"/> / <see cref="BlocksTwoHandedEquip"/> 提供，
// 运行时在 Pawn.EquipWeapon / Pawn.EquipWeaponTwoHanded 里先查后装——**互斥必须双向**，否则就是「后装的赢」：
// 装光源时查了持械、装武器时不查光源 ⇒ 举着火把照样能装上双手步枪，火把还不掉。

/// <summary>某角色手持光源的态：占哪只手、持的什么光源(<see cref="LightProfile"/>)、还剩多少电池/燃烧耐久。空 = 未持光。</summary>
public sealed class HeldLightState
{
    /// <summary>当前所持光源；null = 未持光。</summary>
    public LightProfile? Held { get; private set; }

    private LightChargeState? _charge;

    /// <summary>光源占用的手；null = 未持光。</summary>
    public Hand? HandUsed { get; private set; }

    /// <summary>是否正持光源(黑暗中据此算暴露代价 <see cref="LightSource.ExposureDetectionMultiplier"/>)。</summary>
    public bool IsActive => Held is not null;

    /// <summary>当前是否仍在发光；耗尽后为 false，但仍占用持光的手。</summary>
    public bool IsLit => Held is not null && (_charge?.IsLit ?? true);

    /// <summary>给照明/暴露消费方的有效光源；耗尽后返回 null。</summary>
    public LightProfile? ActiveHeld => IsLit ? Held : null;

    /// <summary>剩余可发光游戏秒；固定光源为 0（无限），未持光也为 0。</summary>
    public double RemainingSeconds => _charge?.RemainingSeconds ?? 0;

    /// <summary>电池/耐久类型；未持光为 None。</summary>
    public LightFuelKind FuelKind => _charge?.FuelKind ?? LightFuelKind.None;

    /// <summary>消耗已受时标缩放的游戏秒；返回本次是否刚好熄灭。</summary>
    public bool Consume(double gameSeconds) => _charge?.Consume(gameSeconds) ?? false;

    /// <summary>
    /// 纯校验核(不含 Weapon 类型)：该手能否接手持光源。
    /// </summary>
    /// <param name="handLost">该手是否已切除。</param>
    /// <param name="twoHandGrip">是否正双手握一把武器(占两手)。</param>
    /// <param name="handHoldsWeapon">该手是否已握着一把武器。</param>
    public static bool CanHold(bool handLost, bool twoHandGrip, bool handHoldsWeapon)
        => !handLost && !twoHandGrip && !handHoldsWeapon;

    /// <summary>
    /// 尝试用 <paramref name="hand"/> 持起 <paramref name="light"/>，按 <paramref name="loadout"/> 的持械/断手事实校验。
    /// 通过则占手、返回 <c>true</c>；不通过状态不变、返回 <c>false</c>。
    /// </summary>
    public bool TryHold(LightProfile light, Hand hand, WeaponLoadout loadout, double? remainingSeconds = null)
    {
        bool handLost = hand == Hand.Left ? loadout.LeftHandLost : loadout.RightHandLost;
        bool twoHandGrip = loadout.TwoHandGrip;
        // 双手握时左右手指向同一把，单看该手会误判——twoHandGrip 已独立短路，故此处只判非双手握时该手是否持械。
        bool handHoldsWeapon = !twoHandGrip && (hand == Hand.Left ? loadout.LeftHand : loadout.RightHand) is not null;

        if (!CanHold(handLost, twoHandGrip, handHoldsWeapon))
        {
            return false;
        }

        Held = light;
        HandUsed = hand;
        _charge = new LightChargeState(light, remainingSeconds);
        return true;
    }

    /// <summary>放下光源(解除占手)。</summary>
    public void Drop()
    {
        Held = null;
        HandUsed = null;
        _charge = null;
    }

    /// <summary>
    /// 反向约束：正持光源时是否应挡下「装双手武器」(双手武器与光源互斥)。
    /// 运行时在 <see cref="WeaponLoadout.EquipTwoHanded"/> 前查此，为 true 则先让玩家放下光源。
    /// </summary>
    public static bool BlocksTwoHandedEquip(HeldLightState? light) => light?.IsActive == true;

    /// <summary>
    /// 反向约束（通用式）：正持光源时，把 <paramref name="weapon"/> 装到 <paramref name="hand"/> 是否应被挡。
    /// <para>
    /// <b>为什么必须有这个</b>：<see cref="WeaponLoadout"/> 看不见光源（刻意解耦），故装备侧若不主动查光源，
    /// 互斥就是<b>单向</b>的——持械时装光源挡得住(<see cref="TryHold"/>)，持光时装武器却畅通无阻，
    /// 结果是「后装的赢」：举着火把装上双手步枪，火把并不会掉。
    /// </para>
    /// 口径（与 <see cref="CanHold"/> 严格对偶）：
    ///   · 未持光 → 不挡；
    ///   · 双手武器 → 一律挡（占两手，与光源互斥；两只手都无处可去）；
    ///   · 单手武器装进<b>正持光的那只手</b> → 挡（一只手不能既握火把又握匕首）；
    ///   · 单手武器装进<b>另一只手</b> → 放行（「一手火把 + 一手单手武器」是允许的既有玩法）。
    /// </summary>
    public static bool BlocksWeaponEquip(HeldLightState? light, Weapon weapon, Hand hand)
        => light?.IsActive == true && (weapon.TwoHanded || light.HandUsed == hand);
}
