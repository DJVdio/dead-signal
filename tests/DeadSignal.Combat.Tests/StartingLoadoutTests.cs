using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// authored「自带装备」真正接线到开局 spawn 的护栏（逐角色 loadout）。
/// <para>
/// Pawn/CampMain 活在 Godot 类型里进不了单测 ⇒ 逐角色武器/穿戴接线只能盯**源码**（同 <c>CarryLoadWiringTests</c> 口径）：
/// 谁把某人的开局武器改回去、谁删了道格的墨镜，这里立刻红。纯逻辑侧（枚举/重量）则真跑。
/// </para>
/// <para>
/// 开局武器以角色 Wiki 为准：道格棍棒、克莉丝汀匕首、耗子刺剑，其余角色按各自 authored 配置。
/// </para>
/// </summary>
public class StartingLoadoutTests
{
    private static string Source(string relativePath, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relativePath)))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, $"找不到 {relativePath}");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    private static string Pawn() => Source(Path.Combine("godot", "scripts", "Pawn.cs"));
    private static string Camp() => Source(Path.Combine("godot", "scripts", "CampMain.cs"));
    private static string Pete() => Source(Path.Combine("godot", "scripts", "CampMain.PeteEvent.cs"));

    // ---------- 纯逻辑：起始武器枚举 + 重量 ----------

    [Fact]
    public void StartingWeapon_NameMapping_MatchesTables()
    {
        Assert.Null(StartingWeaponInfo.WeaponName(StartingWeapon.None));
        Assert.Equal(WeaponTable.Pistol().Name, StartingWeaponInfo.WeaponName(StartingWeapon.Pistol));
        Assert.Equal(WeaponTable.Dagger().Name, StartingWeaponInfo.WeaponName(StartingWeapon.Dagger));
        Assert.Equal(WeaponTable.Club().Name, StartingWeaponInfo.WeaponName(StartingWeapon.Club));
        Assert.Equal(WeaponTable.Rapier().Name, StartingWeaponInfo.WeaponName(StartingWeapon.Rapier));
    }

    [Fact]
    public void StartingWeapon_KeyParse_IsCaseInsensitive_UnknownIsNone()
    {
        Assert.Equal(StartingWeapon.Pistol, StartingWeaponInfo.FromKey("pistol"));
        Assert.Equal(StartingWeapon.Dagger, StartingWeaponInfo.FromKey("Dagger"));
        Assert.Equal(StartingWeapon.Club, StartingWeaponInfo.FromKey("CLUB"));
        Assert.Equal(StartingWeapon.Rapier, StartingWeaponInfo.FromKey("rapier"));
        Assert.Equal(StartingWeapon.None, StartingWeaponInfo.FromKey("none"));
        Assert.Equal(StartingWeapon.None, StartingWeaponInfo.FromKey(null));
        Assert.Equal(StartingWeapon.None, StartingWeaponInfo.FromKey("不认识"));
    }

    [Fact]
    public void GearKg_NoWeapon_IsApparelOnly()
    {
        // 无武器开局：只有三件套的重量（0.80kg）。
        Assert.Equal(SurvivorStartingKit.ApparelKg, SurvivorStartingKit.GearKg(StartingWeapon.None), 6);
        Assert.Equal(0.80, SurvivorStartingKit.GearKg(StartingWeapon.None), 6);
    }

    [Fact]
    public void GearKg_ClubPlusSunglasses_AddsBothOverApparel()
    {
        // 道格：三件套 + 棍棒 + 墨镜。
        double club = ItemWeights.WeaponKg(WeaponTable.Club().Name);
        double shades = ItemWeights.ArmorKg("墨镜");
        double doug = SurvivorStartingKit.GearKg(StartingWeapon.Club, new[] { "墨镜" });
        Assert.Equal(SurvivorStartingKit.ApparelKg + club + shades, doug, 6);
    }

    [Fact]
    public void Sunglasses_IsCatalogItem_OccupyingEyesSlot_SoEquipApparelResolvesIt()
    {
        // 道格的墨镜走 Pawn.EquipApparel("墨镜")（slot 缺省自解析）：只有当它是目录品且占眼镜槽，这条统一路径才穿得上。
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("墨镜");
        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.Eyes }, def!.Slots);
    }

    // ---------- 源码接线：逐角色 loadout（Godot 类型进不了单测，盯源码） ----------

    [Fact]
    public void Doug_StartsWith_Club_And_Sunglasses()
    {
        string camp = Camp();
        // 道格：棍棒进主手 + 墨镜进眼镜槽，去掉旧的手枪。
        Assert.Contains("Pawn.Create(\"道格\", StartingWeapon.Club", camp);
        Assert.Contains("墨镜", camp);
        Assert.DoesNotContain("Pawn.Create(\"道格\", usePistol", camp);
    }

    [Fact]
    public void NurseAndPete_StartWith_None()
    {
        string camp = Camp();
        string pete = Pete();
        // 南丁格尔：无武器。
        Assert.Contains("Pawn.Create(NurseRecruit.NurseName, StartingWeapon.None", camp);
        // 男孩 / 皮特：空手入队。
        Assert.Contains("StartingWeapon.None", pete);
        Assert.DoesNotContain("usePistol", pete);
    }

    [Fact]
    public void Rat_StartsWith_Rapier()
    {
        string camp = Camp();
        string pawn = Pawn();
        Assert.Contains("Pawn.Create(RatPerk.RatName, StartingWeapon.Rapier", camp);
        // 不能只把枚举传进去：Pawn.Create 必须真的把刺剑构造成主手武器。
        Assert.Contains("StartingWeapon.Rapier => WeaponTable.Rapier()", pawn);
    }

    [Fact]
    public void Rat_Has_A_Dedicated_Design_Entry_Without_Invented_Backstory()
    {
        string design = Source(Path.Combine("docs", "superpowers", "specs", "2026-07-04-dead-signal-design.md"));
        Assert.Contains("### 可招募角色：耗子（已确认事实与 authored 留白）", design);
        Assert.Contains("累计搜出 **75 件**升到 2 级、**250 件**升到 3 级", design);
        Assert.Contains("前史、性格、为什么会在下水道、与其他角色的既有关系", design);
        Assert.Contains("全部留白，等待用户手写", design);
    }

    [Fact]
    public void Christine_StartsWith_DaggerAndLeatherChest()
    {
        string camp = Camp();
        // 克莉丝汀：匕首 + Pawn.Create 自动三件套 + 皮革胸甲。
        Assert.Contains("StartingWeapon.Dagger", camp);
        Assert.Contains("extraApparel: new[] { \"皮革胸甲\" }", camp);
    }

    [Fact]
    public void CampJson_SpawnWeaponField_DrivesSpawn_NotPistolBool()
    {
        string camp = Camp();
        // spawn 循环按新的武器规格读取（string weapon 字段解析成 StartingWeapon），不再喂 s.pistol 布尔。
        Assert.Contains("StartingWeaponInfo.FromKey", camp);
        Assert.DoesNotContain("Pawn.Create(s.name ?? \"幸存者\", s.pistol", camp);
    }

    [Fact]
    public void PawnCreate_TakesStartingWeapon_And_ExtraApparel()
    {
        string pawn = Pawn();
        Assert.Contains("StartingWeapon weapon", pawn);
        // 额外穿戴品走既有 EquipApparel 统一路径。
        Assert.Contains("extraApparel", pawn);
        Assert.DoesNotContain("bool usePistol", pawn);
    }
}
