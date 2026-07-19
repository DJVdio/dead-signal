using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 开火噪音系统。用户拍板：「弓箭可以设计成潜行武器，<b>但是也会发出一定声响</b>」+「做最小噪音，<b>丧尸和劫掠者都听得到</b>」。
/// <para>设计意图：<b>你能悄悄干掉一个，但干不掉一群</b>。弓不是无声万能解，枪会招来一片。</para>
/// </summary>
public class NoiseTests
{
    // ---- 判定纯逻辑 ----

    [Fact]
    public void 无声武器_一个听者也不惊动_既有近战零回归()
    {
        // NoiseRadius 默认 0 = 全部近战/天生武器 → 无论听者多近、多敌对，都不触发。
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 0, noiseRadius: 0));
    }

    [Fact]
    public void 半径内的敌对闲人_会过来侦查()
    {
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 300, noiseRadius: 600));
    }

    [Fact]
    public void 半径外的听不见()
    {
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 601, noiseRadius: 600));
        // 边界含在内
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 600, noiseRadius: 600));
    }

    [Fact]
    public void 已经在追人的敌人_不被枪声拽走注意力()
    {
        // 否则会出现"开一枪把追我的丧尸引开"的滑稽解法，且破坏既有追击/破防行为。
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true, listenerHasTarget: true, hostileToSource: true,
            distance: 10, noiseRadius: 600));
    }

    [Fact]
    public void 战斗噪音_不分阵营_自己人的枪声照样把你叫过来()
    {
        // ⚠️ 用户拍板推翻旧口径：「**战斗声不分阵营**，脚步声分」。
        // 旧规则是"同阵营不理会自己人的枪声"——现在作废：打斗的动静（枪响、撞击、惨叫）
        // **谁听见都会过来看**，不管是谁在打谁。
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: false,
            distance: 10, noiseRadius: 600));
    }

    [Fact]
    public void 尸体不侦查()
    {
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: false, listenerHasTarget: false, hostileToSource: true,
            distance: 10, noiseRadius: 600));
    }

    // ⚠️ 本处原有两条 impl-bow 时代的断言（`枪托贴脸砸人_无声` / `全部近战与天生武器_恒0噪音`）
    // 已**随本批次的用户拍板作废并删除**：用户要求「近战会有一定的噪音」，近战不再恒 0。
    // 取代它们的是下方「行为噪音」段里的 `全部近战武器_都有噪音` 与 `枪托砸人_是近战量级的噪音`。

    // ---- 武器表噪音梯度 ----

    [Fact]
    public void 全部远程武器_噪音非零_弓也不是无声()
    {
        // 用户原话：弓「**也会发出一定声响**」——弓不是无声万能解。
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.IsRanged))
        {
            Assert.True(w.NoiseRadius > 0, $"「{w.Name}」是远程武器，噪音不该为 0");
        }
    }

    [Fact]
    public void 弓弩明显比枪安静_这是潜行武器的立身之本()
    {
        Weapon[] archery = WeaponTable.Arsenal().Where(w => w.IsRanged && Archery.UsesArrows(w)).ToArray();
        Weapon[] guns = WeaponTable.Arsenal().Where(w => w.IsRanged && !Archery.UsesArrows(w)).ToArray();

        Assert.NotEmpty(archery);
        Assert.NotEmpty(guns);

        double loudestArchery = archery.Max(w => w.NoiseRadius);
        double quietestGun = guns.Min(w => w.NoiseRadius);

        // 最吵的弓弩也要远比最安静的枪安静（不是"略低"，是数量级的差）。
        Assert.True(loudestArchery * 2 < quietestGun,
            $"最吵的弓弩({loudestArchery}) 应远低于最安静的枪({quietestGun})");
    }

    [Fact]
    public void 弓的噪音_传不出丧尸嗅觉半径_故放箭不额外招怪()
    {
        // 梯度锚点①：丧尸嗅觉兜底半径。**读单一真源 NoiseLogic.ZombieSmellRadius，不手抄字面量**
        // （Zombie.SmellSenseRadius 亦转发到它；此前这里硬编码着第三个 70，改锚点时不会红＝护栏失效）。
        // 弓 ≤ 该半径 ⇒ 听得见你放箭的丧尸，本来就已经闻得到你 —— 放箭不多招一只。
        const double ZombieSmellRadius = NoiseLogic.ZombieSmellRadius;

        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.IsRanged && Archery.UsesArrows(w)))
        {
            Assert.True(w.NoiseRadius <= ZombieSmellRadius,
                $"「{w.Name}」噪音 {w.NoiseRadius} 超出了丧尸嗅觉半径 {ZombieSmellRadius}，潜行就不成立了");
        }
    }

    [Fact]
    public void 枪的噪音_远超丧尸夜间视距_一枪招来视野外的一群()
    {
        // 梯度锚点②：丧尸夜间前向视距 ≈ 219px（Zombie.BaseSightRange 490 × 夜间系数 ≈0.4475）。
        // 枪 ≫ 219 ⇒ 一枪把你根本看不见的丧尸拽过来。这就是"干不掉一群"。
        const double ZombieNightSight = 219;

        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.IsRanged && !Archery.UsesArrows(w)))
        {
            Assert.True(w.NoiseRadius > ZombieNightSight,
                $"「{w.Name}」是枪，噪音 {w.NoiseRadius} 应远超丧尸夜间视距 {ZombieNightSight}");
        }
    }

    [Fact]
    public void 枪的噪音梯度_大口径比小口径响()
    {
        // 响度 ∝ 装药量/机械动静：狙击 > 步枪 > 手枪。
        Assert.True(WeaponTable.SniperRifle().NoiseRadius > WeaponTable.Rifle().NoiseRadius);
        Assert.True(WeaponTable.Rifle().NoiseRadius > WeaponTable.Pistol().NoiseRadius);
    }

    // ---- Sim 零漂移护栏 ----

    [Fact]
    public void 噪音字段不参与任何战斗结算_Sim基线零漂移()
    {
        // NoiseRadius 是纯数据，只被 Godot 空间层消费。两把**只有噪音半径不同**的武器，
        // 喂同一串随机数，引擎结算（伤害/护甲/穿透层数）必须一字不差 ——
        // 这是"既有 Sim 基线零漂移"的机制保证（Sim 是纯引擎对决、无空间层，根本不跑噪音）。
        var chest = new BodyPart { Name = "胸部", VolumeWeight = 40 };
        var layers = new[]
        {
            new ArmorLayer { Name = "外层", Slot = ArmorSlot.Plate, SharpDefense = 12, BluntDefense = 6 },
            new ArmorLayer { Name = "内层", Slot = ArmorSlot.Skin, SharpDefense = 8, BluntDefense = 4 },
        };

        static Weapon Make(double noise) => new()
        {
            Name = "测试枪", DamageMin = 10, DamageMax = 20, Penetration = 0.2,
            DamageType = DamageType.Sharp, IsRanged = true, AttackInterval = 1, NoiseRadius = noise,
        };

        // 顺序：atk1∈[10,20] / def1∈[0, 12×(1−0.2)=9.6] / atk2∈[0,18] / def2∈[0, 8×0.8=6.4]
        double[] seq = { 18, 8, 12, 5 };
        CombatResult loud = new CombatResolver(new SequenceRandomSource(seq)).Resolve(Make(9999), layers, chest);
        CombatResult silent = new CombatResolver(new SequenceRandomSource(seq)).Resolve(Make(0), layers, chest);

        Assert.Equal(loud.FinalDamage, silent.FinalDamage);
        Assert.Equal(loud.LayersPenetrated, silent.LayersPenetrated);
        Assert.Equal(loud.Terminated, silent.Terminated);
        Assert.Equal(loud.FinalDamageType, silent.FinalDamageType);
    }

    // ================= 行为噪音（走路 / 近战 / 开门）=================
    // 用户拍板：「走路会有较小的噪音、开门、近战会有一定的噪音」——噪音源不再只有开火。

    [Fact]
    public void 走路噪音_必须小于丧尸嗅觉半径_否则玩家寸步难行()
    {
        // 硬约束：走路 < 70（Zombie.SmellSenseRadius）。否则你一迈腿就把周围全招来，地图没法走。
        // 走路该是「贴着地板的低语」，不是招魂铃。
        Assert.True(NoiseLogic.WalkNoiseRadius > 0, "走路有声（用户：走路会有较小的噪音）");
        Assert.True(NoiseLogic.WalkNoiseRadius < 70, "走路噪音必须小于丧尸嗅觉半径 70");
    }

    [Fact]
    public void 走路噪音_按累计位移节流_不是每帧广播()
    {
        // 性能红线：走路是**持续行为**，每帧广播就是 O(N²) 灾难。按**累计走过的距离**节流。
        double acc = 0;

        Assert.False(NoiseLogic.StrideDue(ref acc, 10, strideDistance: 48), "才走 10px，还不该响");
        Assert.False(NoiseLogic.StrideDue(ref acc, 30, strideDistance: 48), "累计 40px，仍不该响");
        Assert.True(NoiseLogic.StrideDue(ref acc, 10, strideDistance: 48), "累计 50px ≥ 一个步幅 → 响一次");

        // 响过之后扣掉一个步幅，余量留着接着攒（不清零，否则快跑会丢步）。
        Assert.Equal(2, acc, 3);
    }

    [Fact]
    public void 站着不动_不发出走路噪音()
    {
        double acc = 0;
        for (int i = 0; i < 100; i++)
        {
            Assert.False(NoiseLogic.StrideDue(ref acc, 0, strideDistance: 48));
        }
    }

    [Fact]
    public void 全部近战武器_都有噪音_砍人是有动静的()
    {
        // 用户拍板：近战「会有一定的噪音」——不再是恒 0。
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => !w.IsRanged))
        {
            Assert.True(w.NoiseRadius > 0, $"「{w.Name}」是近战，砍人不可能一点声都没有");
        }
    }

    [Fact]
    public void 排序铁律_走路_小于_弓弩_小于_近战_远小于_枪()
    {
        // 这条排序是整个潜行玩法的骨架，破了就没得玩：
        //   走路(最静) < 弓弩(≤70，不额外招怪) < 近战(有打斗声，会招一点) ≪ 枪(把屏幕外的全拽来)
        double walk = NoiseLogic.WalkNoiseRadius;
        double loudestArchery = WeaponTable.Arsenal().Where(w => Archery.UsesArrows(w)).Max(w => w.NoiseRadius);
        double quietestMelee = WeaponTable.Arsenal().Where(w => !w.IsRanged).Min(w => w.NoiseRadius);
        double loudestMelee = WeaponTable.Arsenal().Where(w => !w.IsRanged).Max(w => w.NoiseRadius);
        double quietestGun = WeaponTable.Arsenal().Where(w => w.IsRanged && !Archery.UsesArrows(w)).Min(w => w.NoiseRadius);

        Assert.True(walk < loudestArchery, $"走路({walk}) 必须比最吵的弓弩({loudestArchery}) 还静");
        Assert.True(loudestArchery < quietestMelee,
            $"最吵的弓弩({loudestArchery}) 必须比最静的近战({quietestMelee}) 还静 —— "
            + "**弓比近战安静就是弓存在的意义**：远远一箭放倒，好过凑上去砍出一堆动静");
        Assert.True(loudestMelee * 2 <= quietestGun,
            $"最吵的近战({loudestMelee}) 必须不高于最静枪声({quietestGun})的一半");
    }

    [Fact]
    public void 近战噪音梯度_越沉越响_砸甲的最响()
    {
        // 响度 ∝ 质量 × 挥击幅度 × 砸不砸甲。
        Assert.True(WeaponTable.Warhammer().NoiseRadius > WeaponTable.Greatsword().NoiseRadius,
            "破甲锤砸甲当当响，该是全近战最响");
        Assert.True(WeaponTable.Greatsword().NoiseRadius > WeaponTable.Dagger().NoiseRadius,
            "重剑抡起来比匕首响");
        // 匕首最静，呼应它自己的 flavor：「小巧、贴身、安静」。
        Assert.Equal(WeaponTable.Arsenal().Where(w => !w.IsRanged).Min(w => w.NoiseRadius),
            WeaponTable.Dagger().NoiseRadius);
    }

    [Fact]
    public void 爪击与撕咬_也有声_打斗不可能是哑剧()
    {
        Assert.True(WeaponTable.ZombieClaw().NoiseRadius > 0);
        Assert.True(WeaponTable.DogBite().NoiseRadius > 0);
    }

    [Fact]
    public void 枪托砸人_是近战量级的噪音_不是枪响也不是无声()
    {
        // 修正上一版：枪托曾被设成恒 0（无声）。但抡枪托砸人显然有动静——它是**近战**，该按近战计。
        Weapon rifle = WeaponTable.Rifle();
        Weapon stock = rifle.MeleeProfile()!;

        Assert.True(stock.NoiseRadius > 0, "抡枪托砸人不是哑剧");
        Assert.True(stock.NoiseRadius < rifle.NoiseRadius / 2, "但远不是开枪的动静（枪声 600 vs 枪托 115）");

        // [批次21·T7] 枪托噪音已**分枪型**（按枪身质量 85~125）：拿手枪柄敲人和抡一杆 6kg 狙击枪托砸下去，
        // 动静不是一回事。全局常量 NoiseLogic.StockMeleeNoiseRadius(=110) 退化为**兜底默认值**——
        // 只在武器没填 StockMeleeNoiseRadius 时生效。
        Assert.Equal(rifle.StockMeleeNoiseRadius, stock.NoiseRadius);

        // 兜底仍然管用：没填分枪型噪音的远程武器，回落到那个 110。
        var noStockNoise = new Weapon
        {
            Name = "测试枪", IsRanged = true, DamageMax = 10, AttackInterval = 1,
            StockMeleeDamageMax = 5,   // 有枪托，但没填枪托噪音
        };
        Assert.Equal(NoiseLogic.StockMeleeNoiseRadius, noStockNoise.MeleeProfile()!.NoiseRadius);
    }

    [Fact]
    public void 开门噪音_已备好但当前无处可挂()
    {
        // 门控（开关门）机制**在本仓尚不存在**（camp.json 自述「门控（开关门）机制后续做」；
        // 建筑的"门"只是墙上一个缺口+装饰，营地大门是默认关闭的可破坏结构）。
        // 故开门噪音只备好常量与 API，等门控立项时一行接入。
        Assert.True(NoiseLogic.DoorNoiseRadius > 0);
        Assert.True(NoiseLogic.DoorNoiseRadius > NoiseLogic.WalkNoiseRadius, "开门比走路响");
        Assert.True(NoiseLogic.DoorNoiseRadius < WeaponTable.Pistol().NoiseRadius, "但远不到枪的量级");
    }

    [Fact]
    public void 玩家操控的单位_绝不被噪音牵着走_这是硬安全阀()
    {
        // 🐛 P0 回归钉（impl-bow 报的）：噪音走的是 CommandMoveTo —— **玩家下指令的同一条通道**。
        // 若不挡住玩家单位，任何一只丧尸走过幸存者 40px 内，那个没在攻击的幸存者就会自己朝丧尸走过去，
        // 玩家的操作被无声覆盖（"我的人中邪了"）。走路噪音把这坑从偶发变成持续灾难。
        //
        // Pawn / Dog 都是 Faction.Survivor，与丧尸敌对、常常没有攻击目标、就站在你身边 ——
        // 除了 RespondsToNoise 这一条，**其余四条它们全都满足**。这一条是唯一拦得住的东西。
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: false,   // 玩家操控的单位
            listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 0, noiseRadius: NoiseLogic.WalkNoiseRadius));

        // 同样的情形，换成 AI（丧尸/劫掠者）→ 照常被叫过来（对照组，证明不是把噪音整个关掉了）
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true,
            listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 0, noiseRadius: NoiseLogic.WalkNoiseRadius));

        // 哪怕是最响的枪声、贴着脸响，玩家单位也不动
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: false,
            listenerAlive: true, listenerHasTarget: false, hostileToSource: true,
            distance: 0, noiseRadius: WeaponTable.SniperRifle().NoiseRadius));
    }

    [Fact]
    public void 砸门破防_比任何一次挥砍都响_但远不到枪()
    {
        // 「开门」无处可挂，但**砸门**是本仓真实存在的门交互（BreachController.TryBreach）。
        // 抡着家伙砸木头铁皮 —— 动静该盖过最重的近战武器。
        double loudestMelee = WeaponTable.Arsenal().Where(w => !w.IsRanged).Max(w => w.NoiseRadius);
        double quietestGun = WeaponTable.Arsenal()
            .Where(w => w.IsRanged && !Archery.UsesArrows(w)).Min(w => w.NoiseRadius);

        Assert.True(NoiseLogic.BreachNoiseRadius > loudestMelee,
            $"砸门({NoiseLogic.BreachNoiseRadius}) 该比最响的近战({loudestMelee}) 还响");
        Assert.True(NoiseLogic.BreachNoiseRadius < quietestGun,
            $"但砸门({NoiseLogic.BreachNoiseRadius}) 仍远不到枪的量级({quietestGun})");
    }

    [Fact]
    public void 狗的脚步_比人轻()
    {
        // 布鲁斯四只软肉垫，体重不到人的四分之一 —— 天然适合放出去探路。
        Assert.True(NoiseLogic.DogWalkNoiseRadius > 0, "狗也不是飘着走的");
        Assert.True(NoiseLogic.DogWalkNoiseRadius < NoiseLogic.WalkNoiseRadius);
    }

    // ===== 用户拍板「战斗声不分阵营，脚步声分」的 5 条效果，逐条钉死 =====
    // 这 5 条就是验收单本身。任何一条红了，就是语义被改坏了。

    [Fact]
    public void 效果1_丧尸爪你_会引来其他丧尸_被围就真的滚雪球()
    {
        // 新增效果（本次拍板的核心）：声源=丧尸，听者=另一只丧尸（**同阵营**）。
        // 旧规则下同阵营被 hostileToSource 挡掉 ⇒ 丧尸爪你引不来同类，"被围滚雪球"根本不成立。
        // 战斗噪音不分阵营后：打斗的动静谁听见都过来。
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: false,   // ← 丧尸听丧尸，同阵营
            distance: 50, noiseRadius: WeaponTable.ZombieClaw().NoiseRadius));
    }

    [Fact]
    public void 效果2_你砍丧尸_引来其他丧尸_原本就有()
    {
        // 声源=玩家，听者=丧尸（敌对）。这条旧规则下就成立，不能被改坏。
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: true,
            distance: 100, noiseRadius: WeaponTable.Longsword().NoiseRadius));
    }

    [Fact]
    public void 效果3_劫掠者开枪_引来丧尸也引来其他劫掠者()
    {
        double gun = WeaponTable.Rifle().NoiseRadius;

        // 丧尸听见（敌对）—— 旧规则就有
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: true,
            distance: 300, noiseRadius: gun));

        // ⭐ 其他劫掠者也听见（**同阵营**）—— 新增。枪响是枪响，不因为是自己人就听不见。
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Combat, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: false,
            distance: 300, noiseRadius: gun));
    }

    [Fact]
    public void 效果4_丧尸走路_不引来其他丧尸_抱团护栏保住()
    {
        // 声源=丧尸，听者=丧尸（同阵营），**移动**噪音 → 必须 False。
        // 这是与效果1 的**对照**：同样是同阵营听者，战斗声叫得动，脚步声叫不动。
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Movement, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: false,
            distance: 1, noiseRadius: NoiseLogic.WalkNoiseRadius));
    }

    [Fact]
    public void 效果5_玩家和狗_永远不被任何噪音吸引_P0护栏跨类别都成立()
    {
        // P0 护栏必须**跨噪音类别**成立：战斗噪音"不分阵营"绝不意味着玩家单位也会被吸引。
        // 逐一扫：两种类别 × 敌对/同阵营 × 从最静的脚步到最响的狙击枪 —— 玩家单位一概纹丝不动。
        double[] radii =
        {
            NoiseLogic.WalkNoiseRadius, NoiseLogic.DoorNoiseRadius,
            NoiseLogic.BreachNoiseRadius, WeaponTable.SniperRifle().NoiseRadius,
        };

        foreach (NoiseKind kind in new[] { NoiseKind.Movement, NoiseKind.Combat })
        {
            foreach (bool hostile in new[] { true, false })
            {
                foreach (double r in radii)
                {
                    Assert.False(NoiseLogic.ShouldInvestigate(
                        kind: kind,
                        listenerRespondsToNoise: false,   // ← 玩家的 Pawn / 狗
                        listenerAlive: true, listenerHasTarget: false, hostileToSource: hostile,
                        distance: 0, noiseRadius: r),
                        $"玩家单位被 {kind} 噪音(半径 {r}) 拽动了 —— P0 护栏破了，玩家会失去对角色的控制");
                }
            }
        }
    }

    [Fact]
    public void 移动噪音_分阵营_丧尸不会被彼此的脚步声吸引成一坨()
    {
        // 用户拍板：「战斗声不分阵营，**脚步声分**」。脚步保留敌对过滤，这是**抱团震荡护栏**：
        // 脚步是**持续行为**，若不分阵营，每只丧尸的每一步都会把周围的丧尸吸过来，
        // 它们会互相吸引、越滚越紧，最后聚成一坨在原地抖 —— 这正是必须保住的那条线。
        const double walk = NoiseLogic.WalkNoiseRadius;

        // 脚边一个闲着的敌人 → 听得见，过来看看
        Assert.True(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Movement, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: true,
            distance: walk - 1, noiseRadius: walk));

        // 已经在追人的 → 不被脚步声拽走注意力
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Movement, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: true, hostileToSource: true,
            distance: 1, noiseRadius: walk));

        // ⭐ 同阵营 → 不理会。**丧尸走路引不来其他丧尸**，就是靠这一条。
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Movement, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: false,
            distance: 1, noiseRadius: walk));

        // 半径外 → 听不见（走路半径小，一步之外就没人理你）
        Assert.False(NoiseLogic.ShouldInvestigate(
            kind: NoiseKind.Movement, listenerRespondsToNoise: true, listenerAlive: true,
            listenerHasTarget: false, hostileToSource: true,
            distance: walk + 1, noiseRadius: walk));
    }

    [Fact]
    public void 平方距离版_与距离版语义完全一致_热路径不开方()
    {
        // 空间层每次广播都走平方版（走路是热路径，不该为每个听者付一次 sqrt）。
        // 两版必须逐点等价，否则"测了距离版、跑的是平方版"就是假绿。
        // **两种噪音类别 × 敌对/同阵营都要扫**，否则新加的 kind 分支可能只在一版里生效。
        double[] distances = { 0, 1, 39, 40, 41, 69, 70, 71, 350, 700 };
        double[] radii = { 0, NoiseLogic.WalkNoiseRadius, 70, 150, NoiseLogic.BreachNoiseRadius, 700 };

        foreach (NoiseKind kind in new[] { NoiseKind.Movement, NoiseKind.Combat })
        {
            foreach (bool hostile in new[] { true, false })
            {
                foreach (double d in distances)
                {
                    foreach (double r in radii)
                    {
                        Assert.Equal(
                            NoiseLogic.ShouldInvestigate(kind, true, true, false, hostile, d, r),
                            NoiseLogic.ShouldInvestigateSquared(kind, true, true, false, hostile, d * d, r));
                    }
                }
            }
        }
    }

    [Fact]
    public void 节流_一个步幅最多响一次_长距离不丢步也不超发()
    {
        // 性能红线的量化护栏：走 1000px，最多只能响 1000/48 = 20 声（而不是每帧一声）。
        double acc = 0;
        int fired = 0;
        const double total = 1000;
        const double perFrame = 95.0 / 60.0; // 人类移速 95px/s，60fps → 每帧约 1.58px

        for (double walked = 0; walked < total; walked += perFrame)
        {
            if (NoiseLogic.StrideDue(ref acc, perFrame, NoiseLogic.StrideDistance))
            {
                fired++;
            }
        }

        int expected = (int)(total / NoiseLogic.StrideDistance);
        Assert.InRange(fired, expected - 1, expected);

        // 关键：走这 1000px 花了约 632 帧，却只响了 ~20 声 —— 每帧广播的话就是 632 声。
        Assert.True(fired < 632 / 10, "节流必须把广播压到每帧广播的十分之一以下");
    }
}
