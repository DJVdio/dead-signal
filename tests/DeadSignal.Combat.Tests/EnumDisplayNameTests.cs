using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「**任何枚举的英文标识符都不许被显示给玩家**」的钉死测试。
///
/// <para>
/// <b>这个 bug 是怎么来的</b>：枚举的英文名（<c>DayPhase.DayPrep</c>）是**代码内部的名字**，
/// 可 <c>ToString()</c> 和字符串插值会毫不客气地把它原样喂给 UI。HUD 左上角因此长期显示
/// <c>[DayPrep]</c>——旁边全是中文，中间夹一个英文枚举名，正是项目铁律里禁的「代码腔」。
/// 拆解播报同理会打出「拆完 Fence」。
/// </para>
///
/// <para>
/// <b>防线分两层</b>：
/// <list type="number">
/// <item><b>显示面</b>：HUD 状态行/结构播报被抽成纯函数（<see cref="HudStatusLine"/>、
///       <see cref="DisplayNames.StructureName"/>），测试直接断言其输出里**没有英文枚举名**。</item>
/// <item><b>映射面</b>：<see cref="DisplayNames"/> 是全部玩家可见枚举的中文名总表；下方 <see cref="Registry"/>
///       反射遍历**每个枚举的每一个值**，断言都配了中文。**以后谁新增一个枚举值忘了配中文，这里直接红**——
///       这正是本测试存在的意义（不是为了测已知的那几个字，是为了拦住未来的疏漏）。</item>
/// </list>
/// </para>
/// </summary>
public class EnumDisplayNameTests
{
    /// <summary>
    /// **面向玩家的枚举注册表**：枚举类型 → 取其显示名的函数。
    /// 新增「会显示给玩家的枚举」时**必须在此登记**（否则它不受下面的全覆盖检查保护）。
    /// 同时登记了历史遗留的入口（<c>HungerLevels.Label</c> 等），确保它们**确实转发**到了 <see cref="DisplayNames"/>。
    /// </summary>
    public static readonly IReadOnlyList<(Type EnumType, Func<object, string> Display)> Registry =
        new (Type, Func<object, string>)[]
        {
            (typeof(DayPhase), v => DisplayNames.Of((DayPhase)v)),
            (typeof(CampStructureKind), v => DisplayNames.Of((CampStructureKind)v)),
            (typeof(EquipSlot), v => DisplayNames.Of((EquipSlot)v)),
            (typeof(DogEquipSlot), v => DisplayNames.Of((DogEquipSlot)v)),
            (typeof(ArmorSlot), v => DisplayNames.Of((ArmorSlot)v)),
            (typeof(WeaponClass), v => DisplayNames.Of((WeaponClass)v)),
            (typeof(WeaponPart), v => DisplayNames.Of((WeaponPart)v)),
            (typeof(MeleeForm), v => DisplayNames.Of((MeleeForm)v)),
            (typeof(ToolSlot), v => DisplayNames.Of((ToolSlot)v)),
            (typeof(HungerLevel), v => DisplayNames.Of((HungerLevel)v)),
            (typeof(LoadoutTier), v => DisplayNames.Of((LoadoutTier)v)),
            (typeof(SizeTier), v => DisplayNames.Of((SizeTier)v)),
            (typeof(NightRaidLogic.ThreatBand), v => DisplayNames.Of((NightRaidLogic.ThreatBand)v)),
            // 卧床养病角色仍需显示名。
            (typeof(PawnRole), v => DisplayNames.Of((PawnRole)v)),
            // [批次21·impl-cooking] 炊具槽位（烹饪台面板上写着"锅：已装""烤架：空"）。
            (typeof(CookwareSlot), v => DisplayNames.Of((CookwareSlot)v)),
            // [批次21·impl-medicine] 医疗物资用途，与"为什么不能给他用这个"（医务面板按钮置灰时把原因挂在提示上）。
            (typeof(MedicalUseKind), v => DisplayNames.Of((MedicalUseKind)v)),
            (typeof(MedicalRefusal), v => DisplayNames.Of((MedicalRefusal)v)),
        };

    public static TheoryData<Type> RegisteredEnums()
    {
        var data = new TheoryData<Type>();
        foreach ((Type t, _) in Registry)
        {
            data.Add(t);
        }
        return data;
    }

    // ———————————————————————— 映射面：全覆盖 + 无英文 ————————————————————————

    /// <summary>
    /// 注册表里每个枚举的**每一个值**都必须有中文显示名：非空、不含 ASCII 字母、且不等于英文枚举名本身。
    /// <b>新增枚举值忘了配中文 ⇒ 这条红。</b>
    /// </summary>
    [Theory]
    [MemberData(nameof(RegisteredEnums))]
    public void 每个玩家可见枚举的每个值都有中文显示名(Type enumType)
    {
        Func<object, string> display = Registry.First(e => e.EnumType == enumType).Display;

        foreach (object value in Enum.GetValues(enumType))
        {
            string name = value.ToString()!;   // 英文枚举名（代码内部的名字）
            string shown = display(value);     // 显示给玩家的名字

            Assert.False(string.IsNullOrWhiteSpace(shown), $"{enumType.Name}.{name} 没有显示名");
            Assert.NotEqual(name, shown);
            Assert.DoesNotContain(shown, c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            Assert.False(
                shown == DisplayNames.Unknown,
                $"{enumType.Name}.{name} 漏配中文显示名（掉进了「{DisplayNames.Unknown}」兜底）——请在 DisplayNames 里补上。");
        }
    }

    /// <summary>
    /// 兜底是 fail-safe 的：即便真漏了（或拿到一个越界值），显示的也是「未知」，
    /// **而不是英文枚举名，也不是崩溃**。玩家永远看不到代码腔。
    /// </summary>
    [Fact]
    public void 未登记的枚举值兜底显示未知_而不是英文名()
    {
        Assert.Equal(DisplayNames.Unknown, DisplayNames.Of((DayPhase)99));
        Assert.Equal(DisplayNames.Unknown, DisplayNames.Of((CampStructureKind)99));
        Assert.Equal(DisplayNames.Unknown, DisplayNames.Of((EquipSlot)99));
    }

    /// <summary>历史入口必须转发到 <see cref="DisplayNames"/>（单一事实源，不许两份 switch 各说各话）。</summary>
    [Fact]
    public void 旧的显示名入口一律转发到统一映射()
    {
        foreach (HungerLevel v in Enum.GetValues<HungerLevel>())
        {
            Assert.Equal(DisplayNames.Of(v), v.Label());
        }
        foreach (ToolSlot v in Enum.GetValues<ToolSlot>())
        {
            Assert.Equal(DisplayNames.Of(v), v.Label());
        }
        foreach (LoadoutTier v in Enum.GetValues<LoadoutTier>())
        {
            Assert.Equal(DisplayNames.Of(v), CarryCapacity.TierLabel(v));
        }
        foreach (SizeTier v in Enum.GetValues<SizeTier>())
        {
            Assert.Equal(DisplayNames.Of(v), ExplorationProgress.TierLabel(v));
        }
    }

    // ———————————————————————— 显示面：HUD 状态行 ————————————————————————

    /// <summary>
    /// HUD 左上角状态行**不许出现英文枚举名**——这正是 <c>[DayPrep]</c> 的原案发地。
    /// 全部内部流程节点逐个过一遍：显示必须是中文，且原英文名不得出现在整行里。
    /// </summary>
    [Theory]
    [InlineData(DayPhase.DawnMeal)]
    [InlineData(DayPhase.DayPrep)]
    [InlineData(DayPhase.DayTravel)]
    [InlineData(DayPhase.DayExplore)]
    [InlineData(DayPhase.DayReturn)]
    [InlineData(DayPhase.DuskMeal)]
    [InlineData(DayPhase.NightPrep)]
    [InlineData(DayPhase.NightAct)]
    public void HUD状态行显示中文相位名_不是英文枚举名(DayPhase phase)
    {
        string line = HudStatusLine.Compose(
            exploring: false, day: 3, clock: "08:20", phase: phase, speed: "3x", survivors: 4, bagLine: "");

        Assert.Contains($"[{DisplayNames.Of(phase)}]", line);
        Assert.DoesNotContain(phase.ToString(), line);   // ← 原 bug：这里会打出 [DayPrep]
    }

    /// <summary>状态行整体格式（营地/探索、天数、时钟、速度、幸存者、可选背包行）。</summary>
    [Fact]
    public void HUD状态行整体格式()
    {
        Assert.Equal(
            "营地  第 3 天  08:20  [白天筹备]   速度 1x   幸存者 4",
            HudStatusLine.Compose(false, 3, "08:20", DayPhase.DayPrep, "1x", 4, ""));

        Assert.Equal(
            "探索  第 7 天  14:05  [外出探索]   速度 3x   幸存者 2   背包 12.0 / 50.0 kg（轻装）",
            HudStatusLine.Compose(true, 7, "14:05", DayPhase.DayExplore, "3x", 2, "   背包 12.0 / 50.0 kg（轻装）"));
    }

    /// <summary>
    /// 全部内部流程节点的中文名逐字钉死（**这是玩家会读到的字**，改动须过用户）。
    /// 顺序即一天的流转：白天筹备 → 出发路上 → 外出探索 → 返回营地 → 黄昏聚餐 → 夜间部署 → 夜间行动 → 清晨聚餐。
    /// </summary>
    [Fact]
    public void 相位中文名逐字钉死()
    {
        Assert.Equal("白天筹备", DisplayNames.Of(DayPhase.DayPrep));
        Assert.Equal("出发路上", DisplayNames.Of(DayPhase.DayTravel));
        Assert.Equal("外出探索", DisplayNames.Of(DayPhase.DayExplore));
        Assert.Equal("返回营地", DisplayNames.Of(DayPhase.DayReturn));
        Assert.Equal("黄昏聚餐", DisplayNames.Of(DayPhase.DuskMeal));
        Assert.Equal("夜间部署", DisplayNames.Of(DayPhase.NightPrep));
        Assert.Equal("夜间行动", DisplayNames.Of(DayPhase.NightAct));
        Assert.Equal("清晨聚餐", DisplayNames.Of(DayPhase.DawnMeal));
    }

    // ———————————————————————— 显示面：结构播报 ————————————————————————

    /// <summary>
    /// 拆解播报的结构名：没有专名时**退回中文种类名**，而不是 <c>kind.ToString()</c>
    /// （原 bug：toast 打出「拆完 Fence：…」）。
    /// </summary>
    [Theory]
    [InlineData(CampStructureKind.Fence, "围栏")]
    [InlineData(CampStructureKind.Gate, "大门")]
    [InlineData(CampStructureKind.Door, "门")]
    public void 结构没有专名时退回中文种类名_不是英文枚举名(CampStructureKind kind, string expected)
    {
        Assert.Equal(expected, DisplayNames.StructureName(null, kind));
        Assert.Equal(expected, DisplayNames.StructureName("", kind));
        Assert.DoesNotContain(kind.ToString(), DisplayNames.StructureName("", kind));
    }

    /// <summary>有专名（如「厨房门」）就用专名——专名本来就是中文，优先级高于种类名。</summary>
    [Fact]
    public void 结构有专名时用专名()
    {
        Assert.Equal("厨房门", DisplayNames.StructureName("厨房门", CampStructureKind.Door));
    }
}
