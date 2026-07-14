using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>左右手（纯逻辑，无 Godot 依赖）。</summary>
public enum Hand
{
    Left,
    Right,
}

/// <summary>
/// 武器持械模型（纯逻辑，无 Godot 依赖）：左手 / 右手各持一把武器，推导 <see cref="GripMode"/> 供引擎系数消费。
/// 用户拍板口径：两把单手武器 = 双持（引擎 ×0.70 攻速、远程误差角 ×1.25）；一把双手武器占两手；
/// 一把单手 = 单手；一把单手改双手握 = 双手（**无攻速加成**，与单手同速）。断手 → 该手不能持械。
/// 断手/GripMode 皆纯逻辑入参，不耦合 Body/Godot。
/// </summary>
public sealed class WeaponLoadout
{
    /// <summary>左手所持武器；null = 空手。双手持一把时左右手指向同一把。</summary>
    public Weapon? LeftHand { get; private set; }

    /// <summary>右手所持武器；null = 空手。双手持一把时左右手指向同一把。</summary>
    public Weapon? RightHand { get; private set; }

    /// <summary>true = 一把武器同时占据两手（双手武器，或单手武器改双手握）——**不带攻速加成**，纯装备约束。</summary>
    public bool TwoHandGrip { get; private set; }

    /// <summary>左手是否已切除（断手 → 不能持械）。</summary>
    public bool LeftHandLost { get; private set; }

    /// <summary>右手是否已切除（断手 → 不能持械）。</summary>
    public bool RightHandLost { get; private set; }

    public WeaponLoadout(bool leftHandLost = false, bool rightHandLost = false)
    {
        LeftHandLost = leftHandLost;
        RightHandLost = rightHandLost;
    }

    private bool HandLost(Hand hand) => hand == Hand.Left ? LeftHandLost : RightHandLost;

    /// <summary>空手（两手皆无武器）。</summary>
    public bool IsUnarmed => LeftHand is null && RightHand is null;

    /// <summary>
    /// 推导持握态：一把武器占两手 = 双手；两手各一把单手 = 双持；仅一把单手 = 单手；空手 = 单手（无攻击基线）。
    /// </summary>
    public GripMode Grip
    {
        get
        {
            if (TwoHandGrip)
            {
                return GripMode.TwoHanded;
            }

            return LeftHand is not null && RightHand is not null ? GripMode.DualWield : GripMode.OneHanded;
        }
    }

    /// <summary>当前主攻武器（供 Actor.AttackWeapon 兼容）：右手优先；空手 → null。</summary>
    public Weapon? PrimaryWeapon => RightHand ?? LeftHand;

    /// <summary>
    /// 装备到指定手：双手武器占两手（另一手清空）；单手武器占该手；两手各一把单手 = 双持。
    /// 断手（该手或双手武器需的另一手）、双持约束（两把均需 <see cref="Weapon.CanDualWield"/>）不满足则拒绝并返回 false，状态不变。
    /// </summary>
    public bool EquipToHand(Weapon weapon, Hand hand)
    {
        if (weapon.TwoHanded)
        {
            return EquipTwoHanded(weapon);
        }

        if (HandLost(hand))
        {
            return false; // 断手不能持械
        }

        // 若正双手握一把，装到单手即换成单手持械：先解除双手握占用。
        Weapon? otherHeld = TwoHandGrip ? null : Other(hand);

        // 另一手已有单手武器 → 将形成双持，两把都须可双持。
        if (otherHeld is not null && (!weapon.CanDualWield || !otherHeld.CanDualWield))
        {
            return false;
        }

        if (TwoHandGrip)
        {
            ClearBoth();
        }

        SetHand(hand, weapon);
        return true;
    }

    /// <summary>双手持一把武器（双手武器，或单手武器改双手握——**无攻速加成**）：占两手、另一手清空。任一手断则拒绝。</summary>
    public bool EquipTwoHanded(Weapon weapon)
    {
        if (LeftHandLost || RightHandLost)
        {
            return false; // 双手握需两手俱在
        }

        LeftHand = weapon;
        RightHand = weapon;
        TwoHandGrip = true;
        return true;
    }

    /// <summary>
    /// 读档：把持械态直接摆回去，<b>绕过 <see cref="EquipToHand"/> 的规则校验</b>。
    /// <para>
    /// 为什么不重放 EquipToHand：那些校验（断手禁持、双持要求两把都 <see cref="Weapon.CanDualWield"/>）
    /// 是给「玩家现在要装备」用的。读档不是装备动作，是把世界摆回它存档那一刻的样子——
    /// 而那一刻的持械态<b>本来就是过了校验才形成的</b>。
    /// </para>
    /// <para>
    /// ⚠️ 真正的危险在于：重放一旦被拒，<b>武器就静默消失了</b>——不报错、不提示，玩家只是发现自己空着手。
    /// 存档最恶劣的 bug 就长这样。所以这里直接赋值。
    /// </para>
    /// </summary>
    public void Restore(Weapon? leftHand, Weapon? rightHand, bool twoHandGrip)
    {
        LeftHand = leftHand;
        RightHand = rightHand;
        TwoHandGrip = twoHandGrip;
    }

    /// <summary>卸下：双手握时两手一起清空；否则仅清该手。</summary>
    public void Unequip(Hand hand)
    {
        if (TwoHandGrip)
        {
            ClearBoth();
            return;
        }

        SetHand(hand, null);
    }

    /// <summary>通知断手：标记该手已切除，并清掉其上武器（双手握则整把落地）。</summary>
    public void NotifyHandLost(Hand hand)
    {
        if (hand == Hand.Left)
        {
            LeftHandLost = true;
        }
        else
        {
            RightHandLost = true;
        }

        if (TwoHandGrip)
        {
            ClearBoth(); // 双手武器缺一手即整把落地
            return;
        }

        SetHand(hand, null);
    }

    private Weapon? Other(Hand hand) => hand == Hand.Left ? RightHand : LeftHand;

    private void SetHand(Hand hand, Weapon? weapon)
    {
        if (hand == Hand.Left)
        {
            LeftHand = weapon;
        }
        else
        {
            RightHand = weapon;
        }
    }

    private void ClearBoth()
    {
        LeftHand = null;
        RightHand = null;
        TwoHandGrip = false;
    }
}
