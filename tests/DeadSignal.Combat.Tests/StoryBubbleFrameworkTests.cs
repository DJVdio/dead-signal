using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 「按条件播放用户写的台词」框架的纯逻辑单测（先红后绿）：
// StoryFlags 存取/比较、谓词与 condition 与/或求值、选择器 v2 过滤+加权+去重、triggers 施加。
// 全部脱 Godot——用手搭的 PawnSnapshot/MealWorldContext 与 SequenceRandomSource 复现。
public class StoryBubbleFrameworkTests
{
    // ---------------- StoryFlags ----------------

    [Fact]
    public void StoryFlags_Set_Get_Equals_CaseInsensitive()
    {
        var f = new StoryFlags();
        Assert.Null(f.Get("radio"));
        Assert.False(f.Has("radio"));

        f.Set("radio", "Fixed");
        Assert.Equal("Fixed", f.Get("radio"));
        Assert.True(f.Has("radio"));
        Assert.True(f.Equals("RADIO", "fixed"));   // key 与 value 都不区分大小写
        Assert.False(f.Equals("radio", "broken"));
    }

    [Fact]
    public void StoryFlags_SetNull_ClearsKey()
    {
        var f = new StoryFlags();
        f.Set("met_stranger", "true");
        f.Set("met_stranger", null);
        Assert.False(f.Has("met_stranger"));
        Assert.True(f.Equals("met_stranger", null)); // 未设置 == 期望 null
    }

    // ---------------- 谓词求值 ----------------

    private static MealWorldContext Ctx(
        string phase = "dusk", StoryFlags? flags = null,
        IEnumerable<PawnSnapshot>? pawns = null, int food = 10, double morale = 50) =>
        new MealWorldContext
        {
            Phase = phase,
            Flags = flags ?? new StoryFlags(),
            Pawns = (pawns ?? Enumerable.Empty<PawnSnapshot>()).ToList(),
            Food = food,
            Morale = morale,
        };

    [Fact]
    public void Predicate_Phase()
    {
        var p = new BubblePredicate { type = "phase", value = "dusk" };
        Assert.True(p.Eval(Ctx(phase: "dusk")));
        Assert.False(p.Eval(Ctx(phase: "dawn")));
    }

    [Fact]
    public void Predicate_Flag_EqualityAndPresence()
    {
        var flags = new StoryFlags();
        flags.Set("act", "2");

        var eq = new BubblePredicate { type = "flag", key = "act", value = "2" };
        Assert.True(eq.Eval(Ctx(flags: flags)));

        var present = new BubblePredicate { type = "flag", key = "act" }; // value 省略=判已设置
        Assert.True(present.Eval(Ctx(flags: flags)));

        var absent = new BubblePredicate { type = "flag", key = "unknown" };
        Assert.False(absent.Eval(Ctx(flags: flags)));
    }

    [Fact]
    public void Predicate_AnyPawnState_SeveredHand()
    {
        var maimed = new PawnSnapshot { Name = "蒂诺", HasSeveredHand = true, HasAnySevered = true };
        var whole = new PawnSnapshot { Name = "克莉丝汀" };
        var p = new BubblePredicate { type = "any_pawn_state", value = "severed_hand" };
        Assert.True(p.Eval(Ctx(pawns: new[] { maimed, whole })));
        Assert.False(p.Eval(Ctx(pawns: new[] { whole })));
    }

    [Fact]
    public void Predicate_NamedPawnState()
    {
        var dead = new PawnSnapshot { Name = "蒂诺", IsDead = true };
        var p = new BubblePredicate { type = "pawn_state", name = "蒂诺", value = "dead" };
        Assert.True(p.Eval(Ctx(pawns: new[] { dead })));

        var pWrongName = new BubblePredicate { type = "pawn_state", name = "无此人", value = "dead" };
        Assert.False(pWrongName.Eval(Ctx(pawns: new[] { dead })));
    }

    [Fact]
    public void Predicate_Food_Morale_Comparisons()
    {
        var lowFood = new BubblePredicate { type = "food", op = "lt", amount = 5 };
        Assert.True(lowFood.Eval(Ctx(food: 3)));
        Assert.False(lowFood.Eval(Ctx(food: 8)));

        var highMorale = new BubblePredicate { type = "morale", op = "gte", amount = 60 };
        Assert.True(highMorale.Eval(Ctx(morale: 70)));
        Assert.False(highMorale.Eval(Ctx(morale: 40)));
    }

    [Fact]
    public void Predicate_Hunger_AnyAndNamed()
    {
        var hungry = new PawnSnapshot { Name = "蒂诺", HungerStage = 2 };
        var fed = new PawnSnapshot { Name = "克莉丝汀", HungerStage = 5 };

        var anyHungry = new BubblePredicate { type = "hunger", stage = 3 }; // ≤3 存在
        Assert.True(anyHungry.Eval(Ctx(pawns: new[] { hungry, fed })));
        Assert.False(anyHungry.Eval(Ctx(pawns: new[] { fed })));

        var namedHungry = new BubblePredicate { type = "hunger", name = "蒂诺", stage = 2 };
        Assert.True(namedHungry.Eval(Ctx(pawns: new[] { hungry, fed })));
    }

    [Fact]
    public void Predicate_AnyPawnState_HeavyBloodLoss()
    {
        var bleeder = new PawnSnapshot { Name = "蒂诺", HasBleeding = true, HasHeavyBloodLoss = true };
        var scratched = new PawnSnapshot { Name = "克莉丝汀", HasBleeding = true }; // 流血但未到重度
        var heavy = new BubblePredicate { type = "any_pawn_state", value = "heavy_blood_loss" };
        Assert.True(heavy.Eval(Ctx(pawns: new[] { bleeder })));
        Assert.False(heavy.Eval(Ctx(pawns: new[] { scratched })));
    }

    [Fact]
    public void Predicate_UnknownType_IsFalse()
    {
        var p = new BubblePredicate { type = "relationship_love" }; // 关系类谓词不存在 → 恒假
        Assert.False(p.Eval(Ctx()));
    }

    // ---------------- condition 与/或 ----------------

    [Fact]
    public void Condition_All_And_Any()
    {
        var flags = new StoryFlags();
        flags.Set("act", "2");
        var cond = new BubbleCondition
        {
            all = new List<BubblePredicate>
            {
                new BubblePredicate { type = "phase", value = "dusk" },
                new BubblePredicate { type = "flag", key = "act", value = "2" },
            },
            any = new List<BubblePredicate>
            {
                new BubblePredicate { type = "food", op = "lt", amount = 5 },
                new BubblePredicate { type = "morale", op = "lt", amount = 20 },
            },
        };
        // all 满足 + any 中士气低满足
        Assert.True(cond.IsSatisfied(Ctx(phase: "dusk", flags: flags, food: 99, morale: 10)));
        // any 全不满足
        Assert.False(cond.IsSatisfied(Ctx(phase: "dusk", flags: flags, food: 99, morale: 99)));
        // all 中相位不满足
        Assert.False(cond.IsSatisfied(Ctx(phase: "dawn", flags: flags, food: 1, morale: 10)));
    }

    [Fact]
    public void Condition_Empty_IsAlwaysSatisfied()
    {
        Assert.True(new BubbleCondition().IsSatisfied(Ctx()));
    }

    // ---------------- 选择器 v2：过滤 + 加权 + 去重 ----------------

    private static MealBubble Bubble(string text, string phase = "any", BubbleCondition? cond = null,
        double weight = 1.0, List<BubbleTrigger>? triggers = null) =>
        new MealBubble { text = text, phase = phase, condition = cond, weight = weight, triggers = triggers };

    [Fact]
    public void Pick_FiltersByCondition()
    {
        var onlyWhenStarving = new BubbleCondition
        {
            all = new List<BubblePredicate> { new BubblePredicate { type = "food", op = "lt", amount = 3 } },
        };
        var pool = new MealBubblePool(new[]
        {
            Bubble("通用一句"),
            Bubble("锅都空了", cond: onlyWhenStarving),
        }, new SequenceRandomSource(0, 0, 0, 0));

        var picked = pool.Pick(Ctx(food: 10), 5);
        Assert.DoesNotContain(picked, b => b.text == "锅都空了"); // 食物足→条件句被过滤
        Assert.Contains(picked, b => b.text == "通用一句");
    }

    [Fact]
    public void Pick_Weighted_PrefersHeavier()
    {
        var pool = new MealBubblePool(new[]
        {
            Bubble("轻", weight: 1),
            Bubble("重", weight: 9),
        }, new SequenceRandomSource(5.0)); // total=10，r=5 落在"重"（累加 9 覆盖 [0,9)）
        var picked = pool.Pick(Ctx(), 1);
        Assert.Single(picked);
        Assert.Equal("重", picked[0].text);
    }

    [Fact]
    public void Pick_Dedup_AvoidsRecentlyPlayed()
    {
        var pool = new MealBubblePool(new[]
        {
            Bubble("A", weight: 1),
            Bubble("B", weight: 1),
        }, new SequenceRandomSource(0.0, 0.0, 0.0, 0.0), dedupWindow: 4);

        // 第一餐：r=0 → 取到 total 起点第一条 A（remaining 顺序 A,B）
        var first = pool.Pick(Ctx(), 1);
        Assert.Equal("A", first[0].text);

        // 第二餐：A 在近期已播 → 池只剩 B，必出 B（不连播）
        var second = pool.Pick(Ctx(), 1);
        Assert.Equal("B", second[0].text);
    }

    [Fact]
    public void Pick_Dedup_FallsBackWhenExhausted()
    {
        var pool = new MealBubblePool(new[] { Bubble("独苗") },
            new SequenceRandomSource(0.0, 0.0), dedupWindow: 4);
        var first = pool.Pick(Ctx(), 1);
        var second = pool.Pick(Ctx(), 1);
        // 合格池只有一条且已播 → 去重清空后兜底仍播它，不空场
        Assert.Equal("独苗", first[0].text);
        Assert.Equal("独苗", second[0].text);
    }

    [Fact]
    public void Pick_NoEligible_ReturnsEmpty()
    {
        var pool = new MealBubblePool(new[] { Bubble("只在黎明", phase: "dawn") },
            new SequenceRandomSource());
        Assert.Empty(pool.Pick(Ctx(phase: "dusk"), 3));
    }

    [Fact]
    public void ApplyTriggers_WritesFlags()
    {
        var flags = new StoryFlags();
        var chosen = new[]
        {
            Bubble("推进剧情", triggers: new List<BubbleTrigger>
            {
                new BubbleTrigger { key = "radio_hint_seen", value = "true" },
            }),
        };
        MealBubblePool.ApplyTriggers(chosen, flags);
        Assert.True(flags.Equals("radio_hint_seen", "true"));
    }

    [Fact]
    public void Schema_v2_Deserializes_ConditionAndTriggers()
    {
        // 守护 json schema：condition(all/any 谓词) + weight + triggers 能被 System.Text.Json 直接映射。
        const string json = @"[
          { ""speaker"": null, ""text"": ""断手旁白"",
            ""condition"": { ""all"": [ { ""type"": ""any_pawn_state"", ""value"": ""severed_hand"" } ] } },
          { ""speaker"": ""蒂诺"", ""text"": ""解码"", ""phase"": ""dusk"", ""weight"": 2.0,
            ""condition"": { ""all"": [ { ""type"": ""flag"", ""key"": ""radio_decoded"" } ] },
            ""triggers"": [ { ""key"": ""radio_hint_shared"", ""value"": ""true"" } ] }
        ]";
        var bubbles = System.Text.Json.JsonSerializer.Deserialize<MealBubble[]>(json)!;
        Assert.Equal(2, bubbles.Length);
        Assert.Equal("any_pawn_state", bubbles[0].condition!.all![0].type);
        Assert.Equal(2.0, bubbles[1].weight);
        Assert.Equal("radio_hint_shared", bubbles[1].triggers![0].key);

        // 且能真正驱动选择器：无 flag 时解码句被过滤，置 flag 后合格。
        var flags = new StoryFlags();
        var pool = new MealBubblePool(bubbles, new SequenceRandomSource(0, 0, 0, 0));
        Assert.DoesNotContain(pool.Pick(Ctx(phase: "dusk", flags: flags), 5), b => b.text == "解码");
        flags.Set("radio_decoded", "true");
        Assert.Contains(pool.Pick(Ctx(phase: "dusk", flags: flags), 5), b => b.text == "解码");
    }

    [Fact]
    public void BackCompat_PhaseOnlyPick_StillWorks()
    {
        var pool = new MealBubblePool(new[]
        {
            Bubble("通用"),
            Bubble("黄昏专属", phase: "dusk"),
        }, new SequenceRandomSource(0, 0, 0, 0));
        var picked = pool.Pick("dawn", 5); // 旧签名
        Assert.Contains(picked, b => b.text == "通用");
        Assert.DoesNotContain(picked, b => b.text == "黄昏专属");
    }
}
