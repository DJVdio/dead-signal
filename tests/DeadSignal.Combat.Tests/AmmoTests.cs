using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 弹药系统（批次18）：枪必须消耗弹药，弹药靠搜刮/制作获取。
/// 设计意图（用户拍板）：把枪的碾压从"平衡问题"变成"资源管理问题"——步枪照样强，
/// 但你得掂量这两发子弹值不值得花。**用后勤代价平衡，不削数值。**
/// 弹药粒度＝3 种（子弹/霰弹/箭矢，不分口径）；平衡杠杆在"每次攻击消耗量"（连发几发就吃几发）。
/// </summary>
public class AmmoTests
{
    // ── 零回归：近战武器不吃弹药 ─────────────────────────────────────────────
    [Fact]
    public void 近战武器_默认不消耗弹药()
    {
        foreach (Weapon melee in new[]
        {
            WeaponTable.Dagger(), WeaponTable.Shortsword(), WeaponTable.Rapier(),
            WeaponTable.Longsword(), WeaponTable.Greatsword(), WeaponTable.Pitchfork(),
            WeaponTable.Club(), WeaponTable.SpikeHammer(), WeaponTable.Warhammer(),
            WeaponTable.ZombieClaw(), WeaponTable.DogBite(),
        })
        {
            Assert.False(melee.UsesAmmo, $"{melee.Name} 不该吃弹药");
            Assert.Equal(0, melee.AmmoPerAttack);
        }
    }

    [Fact]
    public void 不吃弹药的武器_零库存也能打_且不扣弹()
    {
        ShotPlan plan = AmmoLogic.PlanShot(WeaponTable.Dagger(), available: 0);

        Assert.True(plan.CanFire);
        Assert.Equal(0, plan.AmmoSpent);
        Assert.Equal(1, plan.RoundsFired);
    }

    // ── 枪械吃子弹 ───────────────────────────────────────────────────────────
    [Fact]
    public void 枪与弹药的映射_照用户拍板()
    {
        // 用户原话：「手枪、冲锋枪用短子弹；自制猎枪、步枪用中子弹；狙击枪用长子弹」。
        // 鹿弹 → 自制霰弹枪。
        // （栓动猎枪原本也吃中子弹，已随用户在数值表上删除这把武器一并撤下。）
        var expected = new (Weapon Gun, string Ammo)[]
        {
            (WeaponTable.Pistol(), AmmoKeys.ShortBullet),
            (WeaponTable.Smg(), AmmoKeys.ShortBullet),
            (WeaponTable.ImprovisedHuntingGun(), AmmoKeys.MediumBullet),
            (WeaponTable.Rifle(), AmmoKeys.MediumBullet),
            (WeaponTable.SniperRifle(), AmmoKeys.LongBullet),
            (WeaponTable.ImprovisedShotgun(), AmmoKeys.Buckshot),
        };

        foreach ((Weapon gun, string ammo) in expected)
        {
            Assert.True(gun.UsesAmmo, $"{gun.Name} 必须吃弹药");
            Assert.Equal(ammo, gun.AmmoKey);
            Assert.True(gun.AmmoPerAttack >= 1);
        }
    }

    [Fact]
    public void 制作比_照用户拍板的稀缺梯度()
    {
        // 用户原话：「一个子弹零件造8个短子弹，5个中子弹，2个长子弹」+「一个子弹零件造4发鹿弹」。
        // 越强的枪，同一份原料能喂它的次数越少 —— 这就是"强，但打不起"的算式。
        Assert.Equal(8, BulletParts.YieldPer(AmmoKeys.ShortBullet));
        Assert.Equal(5, BulletParts.YieldPer(AmmoKeys.MediumBullet));
        Assert.Equal(4, BulletParts.YieldPer(AmmoKeys.Buckshot));
        Assert.Equal(2, BulletParts.YieldPer(AmmoKeys.LongBullet));
        Assert.Equal(0, BulletParts.YieldPer(AmmoKeys.Arrow));   // 箭不吃子弹零件

        // 配方产出量必须与制作比逐一对齐（配方表改错了，这里会先叫）。
        foreach (string key in new[] { AmmoKeys.ShortBullet, AmmoKeys.MediumBullet, AmmoKeys.Buckshot, AmmoKeys.LongBullet })
        {
            RecipeData r = RecipeBook.All.Single(x => x.OutputKey == key);
            Assert.Equal(BulletParts.YieldPer(key), r.OutputQuantity);
            Assert.Equal(1, r.MaterialCosts[BulletParts.Key]);   // 恒为「1 个子弹零件」
        }
    }

    [Fact]
    public void 连发武器_一次攻击吃满连发数的弹()
    {
        // 平衡杠杆：步枪二连发 → 一次射击吃 2 发；冲锋枪三连发 → 3 发。
        // "步枪 93.5% + 二连发 + 穿透40%" 之所以可接受，正因为它一次攻击烧掉两颗子弹。
        Weapon rifle = WeaponTable.Rifle();
        Weapon smg = WeaponTable.Smg();

        Assert.Equal(rifle.BurstCount, rifle.AmmoPerAttack);
        Assert.Equal(smg.BurstCount, smg.AmmoPerAttack);
        // 对照的单发枪原为栓动猎枪（已删）→ 改用同吃中子弹的自制猎枪，意图不变：
        // 连发枪一次攻击烧的弹必须多于单发枪。
        Assert.True(rifle.AmmoPerAttack > WeaponTable.ImprovisedHuntingGun().AmmoPerAttack,
            "步枪单次攻击的弹药代价必须高于单发枪——这是它强的代价");
    }

    [Fact]
    public void 弹药充足_按连发数整轮开火()
    {
        ShotPlan plan = AmmoLogic.PlanShot(WeaponTable.Rifle(), available: 10);

        Assert.True(plan.CanFire);
        Assert.Equal(WeaponTable.Rifle().BurstCount, plan.RoundsFired);
        Assert.Equal(WeaponTable.Rifle().BurstCount, plan.AmmoSpent);
    }

    [Fact]
    public void 弹药不足一整轮_打出剩下的几发_不浪费余弹()
    {
        // 步枪二连发但只剩 1 发 → 打单发，而非"凑不齐一轮就不开火"（否则最后一发变永久死库存）。
        ShotPlan plan = AmmoLogic.PlanShot(WeaponTable.Rifle(), available: 1);

        Assert.True(plan.CanFire);
        Assert.Equal(1, plan.RoundsFired);
        Assert.Equal(1, plan.AmmoSpent);
    }

    [Fact]
    public void 弹药耗尽_不能开火()
    {
        ShotPlan plan = AmmoLogic.PlanShot(WeaponTable.Rifle(), available: 0);

        Assert.False(plan.CanFire);
        Assert.Equal(0, plan.RoundsFired);
        Assert.Equal(0, plan.AmmoSpent);
    }

    // ── 没弹药 → 降级为枪托近战（复用既有贴脸枪托机制）─────────────────────
    [Fact]
    public void 没弹药时_枪退化为枪托近战_且枪托不吃弹药()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.False(AmmoLogic.PlanShot(rifle, available: 0).CanFire);

        // 空枪只能抡：MeleeProfile 是钝击必中的近战版，本身不消耗弹药（否则空枪连抡都抡不动）。
        Weapon stock = rifle.MeleeProfile()!;
        Assert.False(stock.UsesAmmo);
        Assert.True(AmmoLogic.PlanShot(stock, available: 0).CanFire);
    }

    // ── 霰弹：弹丸不乘弹药（8 颗弹丸在同一发壳里）─────────────────────────
    [Fact]
    public void 霰弹枪_多弹丸只吃一发霰弹_弹丸数不乘弹药()
    {
        // 一发霰弹里有 8 颗弹丸 → 打出 8 次独立判定，但只扣 1 发弹药。
        var shotgun = new Weapon
        {
            Name = "测试霰弹枪",
            IsRanged = true,
            PelletCount = 8,
            BurstCount = 1,
            AmmoKey = AmmoKeys.Buckshot,
        };

        Assert.Equal(1, shotgun.AmmoPerAttack);
        Assert.Equal(1, AmmoLogic.PlanShot(shotgun, available: 3).AmmoSpent);
    }

    // ── 箭矢回收：本类只留**通用掷点器**（rate 是入参）────────────────────────
    // ⚠ 回收率本身**不在 AmmoLogic**（原先这儿有个 ArrowRecoveryRate = 0.60，已退役）：
    // 用户拍板「箭只有 25% 的几率不被损毁；读过《弓与箭之道》则是 50%」——回收率取决于**射手读没读过书**，
    // 那是弓弩的规则，故单一真源在 Archery.ArrowRecoveryRate（其行为由 ArcheryTests 覆盖）。
    [Fact]
    public void 箭矢回收_通用掷点器_每支箭独立判定_rate由调用方给()
    {
        // 掷点 < rate 即回收。喂 [0.1, 0.9, 0.3]、rate=0.6 → 有 2 支落在阈值内。
        var rng = new SequenceRandomSource(0.1, 0.9, 0.3);

        Assert.Equal(2, AmmoLogic.RollArrowRecovery(arrowsFired: 3, recoveryRate: 0.6, rng));
    }

    [Fact]
    public void 箭矢回收_必然回收与必然损毁的边界()
    {
        Assert.Equal(4, AmmoLogic.RollArrowRecovery(4, recoveryRate: 1.0, new SequenceRandomSource(0.99, 0.99, 0.99, 0.99)));
        Assert.Equal(0, AmmoLogic.RollArrowRecovery(4, recoveryRate: 0.0, new SequenceRandomSource(0.01, 0.01, 0.01, 0.01)));
    }

    [Fact]
    public void 子弹与鹿弹一律不回收_只有箭捡得回来()
    {
        // 打出去的子弹就是没了。"射出去还能捡回来"是弓弩独有的后勤优势——
        // 但也别高估它：基础只有 25%（Archery.BaseArrowRecoveryRate），射四支捡回一支。
        Assert.Equal(0.25, Archery.BaseArrowRecoveryRate);
        Assert.False(ArrowTable.IsArrow(AmmoKeys.ShortBullet));
        Assert.False(ArrowTable.IsArrow(AmmoKeys.Buckshot));
    }

    // ── 弹药源：玩家吃库存，敌方无限（零回归，丧尸/劫掠者没有库存模型）───────
    [Fact]
    public void 库存弹药源_读写营地共享库存()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Material(AmmoKeys.ShortBullet, "短子弹", 5));
        var source = new InventoryAmmoSource(inv);

        Assert.Equal(5, source.Count(AmmoKeys.ShortBullet));
        Assert.True(source.Spend(AmmoKeys.ShortBullet, 2));
        Assert.Equal(3, source.Count(AmmoKeys.ShortBullet));

        Assert.False(source.Spend(AmmoKeys.ShortBullet, 99)); // 不足 → 原样不动
        Assert.Equal(3, source.Count(AmmoKeys.ShortBullet));
    }

    [Fact]
    public void 无限弹药源_敌方单位零回归()
    {
        var source = new UnlimitedAmmoSource();

        Assert.True(source.Count(AmmoKeys.ShortBullet) > 0);
        Assert.True(source.Spend(AmmoKeys.ShortBullet, 1000));
    }

    // ── 弹药物品与配方 ───────────────────────────────────────────────────────
    [Fact]
    public void 四种子弹是材料_而箭的类别键不是材料()
    {
        // 四种子弹：1 弹药键 = 1 种材料。
        foreach (string key in new[] { AmmoKeys.ShortBullet, AmmoKeys.MediumBullet, AmmoKeys.LongBullet, AmmoKeys.Buckshot })
        {
            Assert.True(Materials.Has(key), $"{key} 必须在材料目录（弹药走材料堆，复用堆叠/实扣/投放链路）");
            Assert.Equal(MaterialCategory.Ammo, Materials.Find(key)!.Value.Category);
        }

        // 箭是「1 类别 : N 材料」（用户拍板箭分 4 种，且箭会反过来改写弓的属性）：
        // AmmoKeys.Arrow 只是**类别键**，供 Weapon.AmmoKey 表达"这武器吃箭"——它本身不是一种材料。
        Assert.False(Materials.Has(AmmoKeys.Arrow), "箭的类别键不该注册成材料——库存里躺的是具体的某一种箭");

        // 具体的箭才是材料，且都归弹药类。
        foreach (ArrowDef arrow in ArrowTable.All)
        {
            Assert.True(Materials.Has(arrow.Key));
            Assert.Equal(MaterialCategory.Ammo, Materials.Find(arrow.Key)!.Value.Category);
        }
    }

    [Fact]
    public void 四种子弹配方吃火药与子弹零件_箭配方一律不吃()
    {
        // 枪弹的两条后勤腿：①子弹零件（唯一共同瓶颈，精密件搜刮为主）；
        // ②火药 → 火药吃燃料（Recipe「gunpowder」= 石料+燃料），而燃料同时是火把/发电机的命根子。
        // "多打两枪"与"今晚有没有灯"于是落进同一个预算。
        foreach (string key in new[] { AmmoKeys.ShortBullet, AmmoKeys.MediumBullet, AmmoKeys.LongBullet, AmmoKeys.Buckshot })
        {
            RecipeData r = RecipeBook.All.Single(x => x.OutputKey == key);
            Assert.True(r.MaterialCosts.ContainsKey("gunpowder"), $"{r.DisplayName} 必须吃火药");
            Assert.True(r.MaterialCosts.ContainsKey(BulletParts.Key), $"{r.DisplayName} 必须吃子弹零件");
        }

        // 箭**一律不吃子弹零件**（它们根本不是一条供应链）。
        foreach (RecipeData a in RecipeBook.All.Where(r => ArrowTable.IsArrow(r.OutputKey)))
        {
            Assert.False(a.MaterialCosts.ContainsKey(BulletParts.Key), $"{a.DisplayName} 不该吃子弹零件");
        }

        // 箭**一律不吃火药** → 因而不吃燃料 —— 这是弓弩的立身之本：可持续，且射出去还能捡回来。
        var arrowRecipes = RecipeBook.All.Where(r => ArrowTable.IsArrow(r.OutputKey)).ToList();
        Assert.NotEmpty(arrowRecipes);
        foreach (RecipeData arrow in arrowRecipes)
        {
            Assert.False(arrow.MaterialCosts.ContainsKey("gunpowder"), $"{arrow.DisplayName} 不该吃火药");
        }
    }

    [Fact]
    public void 子弹零件保留为枪弹材料_但制作配方已删除()
    {
        Assert.True(Materials.Has(BulletParts.Key), "子弹零件仍是四种枪弹的共同材料");
        Assert.DoesNotContain(RecipeBook.All, r => r.OutputKey == BulletParts.Key);
    }

    // ── 弓/弩：「1 类别 : 4 材料」的开火接线（Actor.ResolveRangedWeapon 的纯逻辑等价物）───────
    [Fact]
    public void 弓_自动搭上最差的那种箭_好箭留着()
    {
        // 玩家没显式选箭时的兜底：优先烧掉最差的箭，别让碳纤维箭被自动打光。
        var inv = new InventoryStore();
        inv.Add(Item.Material(ArrowKeys.Carbon, "碳纤维箭", 10));
        inv.Add(Item.Material(ArrowKeys.SharpenedStick, "削尖的木箭", 2));
        var source = new InventoryAmmoSource(inv);

        ArrowDef? picked = Archery.PickCheapestAvailable(source.Count);

        Assert.Equal(ArrowKeys.SharpenedStick, picked!.Key);
    }

    [Fact]
    public void 弓_扣的是具体那种箭_不是类别键()
    {
        // 关键接线点：弓的 Weapon.AmmoKey 是**类别键**（ammo_arrow），库存里根本没有这个键——
        // 若照着它去扣弹，弓会永远打不响。必须扣「选中那支箭」的具体键。
        var inv = new InventoryStore();
        inv.Add(Item.Material(ArrowKeys.Handmade, "自制箭", 3));
        var source = new InventoryAmmoSource(inv);

        Weapon bow = WeaponTable.ShortBow();
        Assert.Equal(AmmoKeys.Arrow, bow.AmmoKey);
        Assert.Equal(0, source.Count(bow.AmmoKey));      // 类别键在库存里恒为 0

        ArrowDef arrow = Archery.PickCheapestAvailable(source.Count)!;
        ShotPlan plan = AmmoLogic.PlanShot(bow, source.Count(arrow.Key));

        Assert.True(plan.CanFire);
        Assert.True(source.Spend(arrow.Key, plan.AmmoSpent));
        Assert.Equal(2, source.Count(ArrowKeys.Handmade));
    }

    [Fact]
    public void 弓_一支箭都没有_打不出来_且没枪托可抡()
    {
        var source = new InventoryAmmoSource(new InventoryStore());
        Weapon bow = WeaponTable.ShortBow();

        Assert.Null(Archery.PickCheapestAvailable(source.Count));
        Assert.False(AmmoLogic.PlanShot(bow, 0).CanFire);
        // 弓没有枪托近战 profile（贴脸即死，潜行武器该付的代价）→ 空弦时这一下**根本打不出来**。
        Assert.Null(bow.MeleeProfile());
    }

    [Fact]
    public void 箭矢回收_回收的是具体那种箭()
    {
        var inv = new InventoryStore();
        var source = new InventoryAmmoSource(inv);

        source.Recover(ArrowKeys.Handmade, 2);

        Assert.Equal(2, source.Count(ArrowKeys.Handmade));
        Assert.Equal(ItemCategory.Material, inv.Items.Single().Category);
    }

    // ── 搜刮投放：枪的强度现在完全由弹药供给决定，这是难度旋钮 ──────────────────

    /// <summary>全部探索点目的地（供遍历全图掉落）。</summary>
    private static readonly string[] AllDestinations =
    {
        ExplorationCache.RiversideCabinName, ExplorationCache.HarvesterWarehouseName,
        ExplorationCache.WatchersCabinName, ExplorationCache.CityRooftopLookoutName,
        ExplorationCache.BroadcastStationName, ExplorationCache.GoldfingerBaseName,
        ExplorationCache.EastNewVillageName, ExplorationCache.GasStationName,
        ExplorationCache.SupermarketName, ExplorationCache.HospitalName,
    };

    /// <summary>全图某种弹药的搜刮投放总量。</summary>
    private static int TotalLootedAmmo(string ammoKey)
    {
        var flags = new StoryFlags();
        return AllDestinations
            .SelectMany(ExplorationCache.CacheIdsFor)
            .Distinct()
            .Select(id => ExplorationCache.Resolve(id, flags))
            .Where(r => r is not null)
            .SelectMany(r => r!.Value.Loot)
            .Where(l => l.Kind == LootKind.Material && l.RefId == ammoKey)
            .Sum(l => l.Quantity);
    }

    [Fact]
    public void 搜刮到枪的地方_必定同时搜刮到它吃的那种弹()
    {
        // 拿到枪却一发能用的弹都没有 = 拿到一根烧火棍。而弹药分了 4 种 —— 给错种类等于没给。
        var flags = new StoryFlags();

        // 河边小屋·枪柜：原本 ← 栓动猎枪（吃中子弹）。栓动猎枪已删除后枪柜一度空了（设计缺口）⇒
        // 用户拍板改掉**自制猎枪**填缺口——自制猎枪同吃中子弹，"有枪必有弹"的前提就地复位。
        CacheResult gunCabinet = ExplorationCache.Resolve(ExplorationCache.RiversideGunCabinetId, flags)!.Value;
        Assert.Contains(gunCabinet.Loot, l => l.Kind == LootKind.Weapon
            && l.RefId == WeaponTable.ImprovisedHuntingGun().Name);
        Assert.Contains(gunCabinet.Loot, l => l.RefId == WeaponTable.ImprovisedHuntingGun().AmmoKey);
        Assert.Equal(AmmoKeys.MediumBullet, WeaponTable.ImprovisedHuntingGun().AmmoKey);

        // 金手指帮·军械柜 ← 冲锋枪（吃短子弹）
        CacheResult armory = ExplorationCache.Resolve(ExplorationCache.GoldfingerArmoryId, flags)!.Value;
        Assert.Contains(armory.Loot, l => l.Kind == LootKind.Weapon);
        Assert.Contains(armory.Loot, l => l.RefId == WeaponTable.Smg().AmmoKey);
    }

    [Fact]
    public void 全图弹药投放_顺着稀缺梯度走()
    {
        // 投放必须顺着制作比的梯度（短8 > 中5 > 鹿4 > 长2）：越贵的弹越少见。
        // 否则"长子弹遍地都是"会让梯度设计当场作废。
        int shortB = TotalLootedAmmo(AmmoKeys.ShortBullet);
        int medium = TotalLootedAmmo(AmmoKeys.MediumBullet);
        int buck = TotalLootedAmmo(AmmoKeys.Buckshot);
        int longB = TotalLootedAmmo(AmmoKeys.LongBullet);

        Assert.True(shortB > medium, $"短子弹({shortB}) 该比中子弹({medium}) 常见");
        Assert.True(medium > longB, $"中子弹({medium}) 该比长子弹({longB}) 常见");
        Assert.True(buck > longB, $"鹿弹({buck}) 该比长子弹({longB}) 常见");
        Assert.True(longB <= 4, $"长子弹全图 {longB} 发 —— 它该是全表最稀有的一发，多了就白设计了");
    }

    [Fact]
    public void 子弹零件是真正的瓶颈_全图刻意稀少()
    {
        // 子弹零件＝四种子弹的**唯一共同原料**，所以它才是难度旋钮本身。
        // 全图 9 个零件 ≈ 72 发短子弹 / 45 发中子弹 / 18 发长子弹（还得各配 1 火药 → 各吃 1 燃料）。
        int parts = TotalLootedAmmo(BulletParts.Key);

        Assert.True(parts > 0, "一个子弹零件都搜不到 = 弹药彻底断供");
        Assert.True(parts <= 12, $"全图子弹零件 {parts} 个 —— 涨过这个量级，「枪强但打不起」当场失效");
    }

    [Fact]
    public void 弹药配方产物_落地为可堆叠的材料物品()
    {
        // CraftOutputFactory 对材料目录里的产物走材料分支 → 弹药自动落地为可堆叠材料堆。
        var produced = CraftOutputFactory.Create(AmmoKeys.ShortBullet, 8).ToList();

        Assert.Single(produced);
        Assert.Equal(ItemCategory.Material, produced[0].Category);
        Assert.Equal(AmmoKeys.ShortBullet, produced[0].RefKey);
        Assert.Equal(8, produced[0].MaterialQuantity);
    }
}
