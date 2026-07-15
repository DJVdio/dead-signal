using System.Linq;
using System.Text.Json.Nodes;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T64·impl-rename-arm] 手臂的部位名是「左手臂 / 右手臂」，**不是「左上臂 / 右上臂」**。
///
/// <para>
/// <b>为什么改</b>（用户原话）：「我看表格里的护甲对于手臂写的全是上臂，但其实手臂不分上下臂，应该只写手臂」。
/// 他是对的，而且理由在代码里就能看见：<b>手直接挂在「上臂」下面</b>（<see cref="HumanBody.LeftHand"/> 的 Parent
/// 就是 <see cref="HumanBody.LeftArm"/>）⇒ <b>引擎里根本没有前臂/小臂这个部位</b>，整条胳膊只有一节。
/// 叫它"上臂"等于凭空多出一个词，还暗示玩家有个够不着的"下臂"。
/// </para>
/// <para>
/// 🔴 <b>对比腿：腿是真的分了三节</b>（大腿 → 小腿 → 脚）⇒ "大腿"这个名字是**准确的**。
/// 所以这次**只改手臂，腿一格不动**。下面有一条专门的护栏钉死这件事。
/// </para>
/// <para>
/// 🔴 <b>改的只有中文名（数据主键）</b>：C# 常量名 <c>LeftArm</c>/<c>RightArm</c> 与枚举 <c>BodyRegion.Arm</c>
/// **本来就叫 Arm、不叫 UpperArm** —— 这正好说明当初的中文名才是错的那一个。
/// </para>
/// </summary>
public class BodyPartNamingTests
{
    // ---- ① 名字本身 ----

    [Fact]
    public void 手臂的部位名是左手臂右手臂_不是左上臂右上臂()
    {
        Assert.Equal("左手臂", HumanBody.LeftArm);
        Assert.Equal("右手臂", HumanBody.RightArm);
    }

    [Fact]
    public void 全套部位名里不许再出现上臂这个词()
    {
        string[] offenders = HumanBody.Parts()
            .Select(p => p.Name)
            .Where(n => n.Contains("上臂"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"手臂不分上下臂（引擎里压根没有前臂部位，手直接挂在手臂下）⇒ 部位名不该出现「上臂」。仍在用的：{string.Join("、", offenders)}");
    }

    /// <summary>🔴 护栏：**腿不许被顺手改掉**。大腿/小腿是真实存在的两节（大腿→小腿→脚），名字是准的。</summary>
    [Fact]
    public void 腿仍然分大腿小腿_这次改名不许波及腿()
    {
        Assert.Equal("左大腿", HumanBody.LeftLeg);
        Assert.Equal("右大腿", HumanBody.RightLeg);
        Assert.Equal("左小腿", HumanBody.LeftCalf);
        Assert.Equal("右小腿", HumanBody.RightCalf);

        // 而且腿确实是三节：脚挂小腿下、小腿挂大腿下 —— 这是"大腿"这个名字站得住的原因。
        var parts = HumanBody.Parts();
        Assert.Equal(HumanBody.LeftCalf, parts.Single(p => p.Name == HumanBody.LeftFoot).Parent);
        Assert.Equal(HumanBody.LeftLeg, parts.Single(p => p.Name == HumanBody.LeftCalf).Parent);
    }

    // ---- ② 层级：手臂只有一节（这就是改名的依据） ----

    [Fact]
    public void 手直接挂在手臂下_中间没有前臂这一节()
    {
        var parts = HumanBody.Parts();

        BodyPart leftHand = parts.Single(p => p.Name == HumanBody.LeftHand);
        BodyPart leftArm = parts.Single(p => p.Name == HumanBody.LeftArm);

        // 手的父节点直接就是手臂 ⇒ 手臂与手之间没有任何一节 ⇒ 不存在"前臂/小臂" ⇒ "上臂"是多出来的词。
        Assert.Equal(HumanBody.LeftArm, leftHand.Parent);
        Assert.Equal(HumanBody.Chest, leftArm.Parent);
        Assert.DoesNotContain(parts, p => p.Parent == HumanBody.LeftArm && p.Name != HumanBody.LeftHand);
    }

    // ---- ③ 🔴 Sim 零漂移护栏：改名一格数值都不许动 ----

    [Fact]
    public void 改名不许改数值_手臂仍是体积权重8血量21()
    {
        var parts = HumanBody.Parts();

        foreach (string arm in new[] { HumanBody.LeftArm, HumanBody.RightArm })
        {
            BodyPart p = parts.Single(x => x.Name == arm);
            Assert.Equal(8.0, p.VolumeWeight);        // 命中权重：动了它 Sim 立刻漂移
            Assert.Equal(21.0, p.MaxHp);              // 切除门槛：动了它切除率立刻漂移
            Assert.Equal(BodyRegion.Arm, p.Region);
            Assert.Equal(BodyMacroRegion.Arm, p.MacroRegion);
        }
    }

    // ---- ④ 护甲覆盖表按新名列（用户看见"上臂"的正是这张表） ----

    [Fact]
    public void 躯干外衣的覆盖列写的是手臂_不是上臂()
    {
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();

        var parts = HumanBody.Parts();
        Assert.True(shirt.Covers(parts.Single(p => p.Name == HumanBody.LeftArm)));
        Assert.True(shirt.Covers(parts.Single(p => p.Name == HumanBody.RightArm)));

        // 手不在躯干外衣的覆盖里（手另有手套）—— 顺带钉死"手臂不含手"这条口径。
        Assert.False(shirt.Covers(parts.Single(p => p.Name == HumanBody.LeftHand)));
    }

    // ---- ⑤ 🔴 存档迁移：老档里的「左上臂」不许被静默吃掉 ----

    /// <summary>
    /// 手搓一份**存档里带身体快照**的 JSON（部位名就是 key / 列表元素）：
    /// 左上臂受了伤（HP 5/21）、右上臂骨折、左上臂在流血。
    /// <para>
    /// 🔴 <b>版本号刻意写成当前版本（v3）</b>：v3 是**这次改名之前**就发布的（impl-iron 的废金属→铁），
    /// 所以"写于 v3、里面却是「左上臂」"的档是**真实存在的一类档**。若把改名塞进 <c>if (fromVersion &lt; 3)</c>，
    /// 恰恰会漏掉这批最需要迁移的档（<c>TryMigrate</c> 对当前版本是直接原样放行的）。
    /// </para>
    /// </summary>
    private static string SaveWithLegacyArmNames(int version) => $$"""
    {
      "Version": {{version}},
      "Camp": {
        "Survivors": [
          {
            "Name": "阿强",
            "Body": {
              "Hp":    { "胸": 20, "左上臂": 5,  "右上臂": 21, "左大腿": 12 },
              "MaxHp": { "胸": 20, "左上臂": 21, "右上臂": 21, "左大腿": 12 },
              "Severed": [],
              "Destroyed": [],
              "Disabled": [],
              "Bleeding": [ "左上臂" ],
              "BleedingRates": [ 1.0 ],
              "BleedingLevels": [ 2 ],
              "Fractured": [ "右上臂" ],
              "TreatedFractures": [],
              "Blood": 4.5,
              "BloodMax": 5.0,
              "BleedRatePerWound": 0.55,
              "BleedRateMultiplier": 1.0,
              "BledOut": false,
              "IsDead": false,
              "Prosthetics": []
            },
            "Conditions": [
              { "Type": "Bleeding", "BodyPart": "左上臂", "OnLimb": true, "Severity": 0.45, "CureProgress": 0.0 }
            ]
          }
        ]
      }
    }
    """;

    [Fact]
    public void 老档里的左上臂会被迁移成左手臂_伤情一处不丢()
    {
        bool ok = SaveMigration.TryMigrate(SaveWithLegacyArmNames(SaveCodec.CurrentVersion), SaveCodec.CurrentVersion, out string? migrated, out string? error);

        Assert.True(ok, $"带老部位名的存档应当能迁移，实际失败：{error}");
        Assert.NotNull(migrated);

        // 老名字一处不许剩（剩一处 ⇒ Body.Restore 会静默丢弃那个键 ⇒ 断胳膊长回来）
        Assert.DoesNotContain("上臂", migrated!);

        JsonNode body = JsonNode.Parse(migrated!)!["Camp"]!["Survivors"]![0]!["Body"]!;

        // Hp / MaxHp 的 **字典 key** 改名，值原封不动
        Assert.Equal(5, body["Hp"]!["左手臂"]!.GetValue<double>());
        Assert.Equal(21, body["MaxHp"]!["左手臂"]!.GetValue<double>());
        Assert.Null(body["Hp"]!["左上臂"]);

        // 列表元素改名
        Assert.Equal("左手臂", body["Bleeding"]![0]!.GetValue<string>());
        Assert.Equal("右手臂", body["Fractured"]![0]!.GetValue<string>());

        // 伤病条目的 BodyPart 改名
        JsonNode cond = JsonNode.Parse(migrated!)!["Camp"]!["Survivors"]![0]!["Conditions"]![0]!;
        Assert.Equal("左手臂", cond["BodyPart"]!.GetValue<string>());

        // 腿没被波及
        Assert.Equal(12, body["Hp"]!["左大腿"]!.GetValue<double>());
    }

    /// <summary>迁移后的部位名，<see cref="Body.Restore"/> 必须真的认得（否则等于没迁）。</summary>
    [Fact]
    public void 迁移后的部位名Body能认出来_断胳膊不会长回来()
    {
        SaveMigration.TryMigrate(SaveWithLegacyArmNames(SaveCodec.CurrentVersion), SaveCodec.CurrentVersion, out string? migrated, out _);
        JsonNode body = JsonNode.Parse(migrated!)!["Camp"]!["Survivors"]![0]!["Body"]!;

        var snap = new BodySnapshot();
        foreach (var kv in body["Hp"]!.AsObject())
        {
            snap.Hp[kv.Key] = kv.Value!.GetValue<double>();
        }
        foreach (var kv in body["MaxHp"]!.AsObject())
        {
            snap.MaxHp[kv.Key] = kv.Value!.GetValue<double>();
        }
        snap.Fractured.Add(body["Fractured"]![0]!.GetValue<string>());
        snap.BloodMax = 5.0;
        snap.Blood = 4.5;
        snap.BleedRatePerWound = 0.55;

        var b = new Body(HumanBody.Parts());
        b.Restore(snap);

        // 🔴 这两条就是"静默吞数据"的反面：老名字读回来 HP 会是满血 21、骨折会消失。
        Assert.Equal(5, b.HpOf(HumanBody.LeftArm));
        Assert.True(b.IsFractured(HumanBody.RightArm));
    }

    /// <summary>已经是新名字的存档：迁移是**幂等**的（跑一遍等于没跑）。</summary>
    [Fact]
    public void 新档再迁一次是幂等的()
    {
        SaveMigration.TryMigrate(SaveWithLegacyArmNames(SaveCodec.CurrentVersion), SaveCodec.CurrentVersion, out string? once, out _);
        bool ok = SaveMigration.TryMigrate(once!, SaveCodec.CurrentVersion, out string? twice, out string? error);

        Assert.True(ok, error);
        Assert.Equal(JsonNode.Parse(once!)!.ToJsonString(), JsonNode.Parse(twice!)!.ToJsonString());
    }

    /// <summary>
    /// 🔴 <b>不许在长文本里做子串替换</b>：叙事文案里若出现"上臂"（描写伤口之类），那是**叙事用词、不是部位名**。
    /// 迁移只改**恰好等于**老部位名的字符串（字典 key / 列表元素 / BodyPart 字段）。
    /// </summary>
    [Fact]
    public void 叙事文案里的上臂不许被改_只改恰好等于老部位名的字符串()
    {
        string save = $$"""
        { "Version": {{SaveCodec.CurrentVersion}},
          "Camp": { "Log": [ "一道深口子从他的左上臂一直划到手腕。" ],
                    "Survivors": [ { "Body": { "Hp": { "左上臂": 5 } } } ] } }
        """;

        bool ok = SaveMigration.TryMigrate(save, SaveCodec.CurrentVersion, out string? migrated, out string? error);

        Assert.True(ok, error);

        JsonNode root = JsonNode.Parse(migrated!)!;
        // 部位名（字典 key）改了
        Assert.Equal(5, root["Camp"]!["Survivors"]![0]!["Body"]!["Hp"]!["左手臂"]!.GetValue<double>());
        // 叙事句子一字未动
        Assert.Equal("一道深口子从他的左上臂一直划到手腕。", root["Camp"]!["Log"]![0]!.GetValue<string>());
    }
}
