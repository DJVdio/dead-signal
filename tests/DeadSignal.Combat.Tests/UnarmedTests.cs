using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 空手近战（拳脚）+「持弓弩近战 = 空手近战」规则。
/// 用户原话：「空手和持弓近战都视作空手近战，造成钝伤」。
/// </summary>
public class UnarmedTests
{
    private static readonly BodyPart Chest = new() { Name = "胸部", VolumeWeight = 40 };

    // ---- 拳脚本体 ----

    [Fact]
    public void Fists_IsBluntUnarmedMelee()
    {
        Weapon f = WeaponTable.Fists();

        Assert.Equal(DamageType.Blunt, f.DamageType);   // 用户拍板：空手造成钝伤
        Assert.False(f.IsRanged);                        // 必中近战，无误差角
        Assert.False(f.UsesAmmo);                        // 拳头不吃弹药
        Assert.False(f.TwoHanded);
        Assert.Equal(0, f.Penetration);                  // 赤手无破甲
        Assert.Null(f.MeleeProfile());                   // 拳脚本身没有"枪托"可派生
    }

    [Fact]
    public void Fists_LowDamage_FastCooldown_QuietestAttack()
    {
        Weapon f = WeaponTable.Fists();
        Weapon dagger = WeaponTable.Dagger();   // 全表最快/最静的**武器**

        // 低伤害：均值低于全表最弱的近战武器（匕首）
        Assert.True((f.DamageMin + f.DamageMax) / 2 < (dagger.DamageMin + dagger.DamageMax) / 2);
        // 快冷却：出手间隔短于匕首（拳头没重量）
        Assert.True(f.AttackInterval < dagger.AttackInterval);
        Assert.True(f.AttackInterval > 0);
        // 噪音小：低于匕首（90）、也低于弓（70）——空手扭打是最安静的攻击方式
        Assert.True(f.NoiseRadius < dagger.NoiseRadius);
        Assert.True(f.NoiseRadius < WeaponTable.ShortBow().NoiseRadius);
        Assert.True(f.NoiseRadius > 0);   // 但不是哑剧
    }

    [Fact]
    public void Fists_ResolveAgainstArmor_UsesBluntDefense()
    {
        // 钝伤 ⇒ 结算读护甲的**钝防**（锐防再高也不管用）。
        var layer = new ArmorLayer { Name = "皮夹克", Slot = ArmorSlot.Outer, SharpDefense = 99, BluntDefense = 0 };
        var rng = new SequenceRandomSource(3, 0);   // 攻击 roll=3（T21 拳脚上限由 4 降到 3，roll 必须落在区间内）、防御 roll=0
        CombatResult r = new CombatResolver(rng).Resolve(WeaponTable.Fists(), new[] { layer }, Chest);

        Assert.Equal(DamageType.Blunt, r.FinalDamageType);
        Assert.Equal(LayerOutcome.Full, r.Layers[0].Outcome);   // 钝防 0 ⇒ 打穿（锐防 99 不参与）
    }

    // ---- 规则：拿什么打近战 ----

    [Fact]
    public void MeleeFor_EmptyHands_IsFists()
    {
        Weapon m = Unarmed.MeleeFor(null);

        Assert.Equal(WeaponTable.Fists().Name, m.Name);
        Assert.Equal(DamageType.Blunt, m.DamageType);
        Assert.True(Unarmed.IsUnarmedMelee(null));
    }

    [Fact]
    public void MeleeFor_EveryBowAndCrossbow_IsFists()
    {
        // 用户口径：持弓近战 = 空手近战（弓不是钝器，不做"抡弓砸人"）。
        // 弩同判：弓弩共用一套体系（Archery，共用箭），且**同样没有 StockMelee\* 枪托 profile**——
        // 枪托近战是枪械专属，弓弩皆无 ⇒ 同一条规则「没枪托可抡就用拳头」把两者一并覆盖，不开特例。
        foreach (Weapon bow in WeaponTable.ArcheryArsenal())
        {
            Weapon m = Unarmed.MeleeFor(bow);

            Assert.Equal(WeaponTable.Fists().Name, m.Name);
            Assert.Equal(DamageType.Blunt, m.DamageType);
            Assert.False(m.IsRanged);
            Assert.True(Unarmed.IsUnarmedMelee(bow));
        }
    }

    [Fact]
    public void MeleeFor_EveryFirearm_StillUsesStockProfile_Unchanged()
    {
        // 枪托近战（MeleeProfile 降级）保持不变——这次改动一根手指都不动它。
        foreach (Weapon gun in WeaponTable.Arsenal().Where(w => w.IsRanged && w.HasMeleeProfile))
        {
            Weapon m = Unarmed.MeleeFor(gun);
            Weapon stock = gun.MeleeProfile()!;

            Assert.Equal(stock.Name, m.Name);              // "步枪（枪托）"
            Assert.Equal(stock.DamageMax, m.DamageMax);
            Assert.Equal(stock.AttackInterval, m.AttackInterval);
            // [批次21·T7] 枪托噪音已**分枪型**（手枪柄 85 ~ 狙击枪托 125），不再是全局常量 110——
            // 本测试要钉的是"MeleeFor 原样返回这把枪自己的枪托 profile"，故与该枪的枪托对比，
            // 而不是与一个全局数字对比（后者会随数值调整无谓地红）。
            Assert.Equal(stock.NoiseRadius, m.NoiseRadius);
            Assert.False(Unarmed.IsUnarmedMelee(gun));
        }
    }

    [Fact]
    public void MeleeFor_MeleeWeapon_ReturnsWeaponItself()
    {
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => !w.IsRanged))
        {
            Assert.Same(WeaponTable.Arsenal().First(x => x.Name == w.Name).Name, Unarmed.MeleeFor(w).Name);
            Assert.Equal(w.DamageType, Unarmed.MeleeFor(w).DamageType);   // 长剑还是锐伤，不被拉成钝伤
            Assert.False(Unarmed.IsUnarmedMelee(w));
        }
    }

    [Fact]
    public void MeleeFor_NaturalWeapons_ReturnThemselves()
    {
        // 天生武器（爪击/撕咬）是近战，不该被换成拳脚。
        Assert.Equal("爪击", Unarmed.MeleeFor(WeaponTable.ZombieClaw()).Name);
        Assert.Equal("撕咬", Unarmed.MeleeFor(WeaponTable.DogBite()).Name);
    }

    // ---- wiki 设计表同步（表赢代码）：用户在 wiki 上定的天生武器数值 ----

    /// <summary>
    /// 用户拍板的全局难度口径：「丧尸本就是<b>以量取胜</b>，三打一的战力比是九比一，所以<b>单一丧尸不该很强</b>」。
    /// 拳脚与爪击的冷却 1.2 → 1.4、爪击穿透 0.05 → 0.03 是**有意的削弱**，不是事故——
    /// 谁要把这几格调回去，先看这条测试和 docs/research/2026-07-14-lanchester.md。
    /// </summary>
    [Fact]
    public void NaturalWeapons_MatchDesignTable()
    {
        Weapon fists = WeaponTable.Fists();
        Assert.Equal(1.4, fists.AttackInterval);
        Assert.Equal(1, fists.DamageMin);
        Assert.Equal(3, fists.DamageMax);
        Assert.Equal(0, fists.Penetration);

        Weapon claw = WeaponTable.ZombieClaw();
        Assert.Equal(1.4, claw.AttackInterval);
        Assert.Equal(1, claw.DamageMin);
        Assert.Equal(3, claw.DamageMax);
        Assert.Equal(0.03, claw.Penetration);
    }

    /// <summary>
    /// 骨刀存在的理由：**造出来必须比空手强**。骨刀单持 DPS 1.50 > 拳脚 1.43——
    /// 这条关系只在拳脚冷却＝1.4 时成立（1.2 时拳脚 1.67 反超骨刀 ⇒ "造把骨刀不如用拳头"）。
    /// </summary>
    [Fact]
    public void BoneKnife_BeatsBareFists()
    {
        static double Dps(Weapon w) => (w.DamageMin + w.DamageMax) / 2 / w.AttackInterval;

        Weapon boneKnife = WeaponTable.Arsenal().First(w => w.Name == "骨刀");
        Assert.True(Dps(boneKnife) > Dps(WeaponTable.Fists()),
            $"骨刀 DPS {Dps(boneKnife):F2} 必须高于拳脚 {Dps(WeaponTable.Fists()):F2}——花材料造出来的东西不该不如空手");
    }

    // ---- Sim 基线零漂移护栏 ----

    [Fact]
    public void Fists_NotInArsenal_SoSimNeverIteratesIt()
    {
        // 结构性证明：Sim（Program/WeaponCalibration/UserPlanCalibration）只遍历 Arsenal()/ArcheryArsenal()。
        // 拳脚同爪击/撕咬——天生武器不入表 ⇒ Sim 的结算路径根本读不到它 ⇒ 既有基线不可能漂移。
        // 25 = 24 − 栓动猎枪（T29 用户从数值表删除） + 消防斧（[批次25·T44] 新建，**追加在末尾**）
        //      + 骨刀（[T56] 它早有配方却不在 Arsenal ⇒ 造得出来、拿不起来；补进表里，**同样追加在末尾**）。
        Assert.Equal(25, WeaponTable.Arsenal().Count);
        Assert.DoesNotContain(WeaponTable.Arsenal(), w => w.Name == WeaponTable.Fists().Name);
        Assert.DoesNotContain(WeaponTable.ArcheryArsenal(), w => w.Name == WeaponTable.Fists().Name);
    }

    // ---- Godot 消费层转发（CombatData 以 Link 编入本测试工程）----

    [Fact]
    public void CombatData_Fists_ForwardsEngineTable()
    {
        Assert.Equal(WeaponTable.Fists().Name, CombatData.Fists().Name);
        Assert.Equal(WeaponTable.Fists().DamageMax, CombatData.Fists().DamageMax);
    }
}
