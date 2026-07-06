using System.Text;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 表现逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 把一次命中结果（<see cref="AttackOutcome"/> + 承伤方名）翻译成**一行中文战斗日志**。
/// 纯函数、无副作用、脱 Godot。
///
/// 视角：**承伤方视角**——CombatFeed 载荷只带承伤方（Target），不含攻击方，故日志写
/// "B 的左手被撕裂 20，断肢！"，不做"A 击中 B"的攻击方归属（需改总线载荷，本轮不做）。
///
/// 措辞口径：伤害动词按<b>伤害类型</b>分——钝器=砸伤、锐器=撕裂；被甲完全挡下走"挡下"专述；
/// 断肢/骨折/脑震荡/流血依序追加为分句；致死追加专门措辞"当场毙命！"。
/// </summary>
public static class CombatLogFormatter
{
    public static string Format(string targetName, AttackOutcome hit)
    {
        string name = string.IsNullOrWhiteSpace(targetName) ? "无名者" : targetName;

        if (hit.Blocked)
        {
            // 被甲完全挡下：不写伤口动词，只述"甲片挡下"。
            return $"{name} 的{hit.PartName}被甲片挡下，未伤分毫。";
        }

        // 伤害动词按类型分：钝=砸伤、锐=撕裂。
        string hurt = hit.FinalType == DamageType.Blunt ? "被砸伤" : "被撕裂";

        var sb = new StringBuilder();
        sb.Append($"{name} 的{hit.PartName}{hurt} {hit.Damage}");

        // 效果依序追加为分句（断肢→骨折→脑震荡→流血），与飘字后缀同序。
        if (hit.Severed) sb.Append("，断肢！");
        if (hit.Fractured) sb.Append("，骨折");
        if (hit.Concussed) sb.Append("，脑震荡");
        if (hit.Bled) sb.Append("，流血不止");

        // 致死专门措辞，排在所有效果之后收束整句。
        if (hit.Died)
        {
            sb.Append("，当场毙命！");
        }
        else
        {
            // 未致死：若末尾已是感叹（如断肢！）则保留，否则以句号收束。
            char last = sb[sb.Length - 1];
            if (last != '！' && last != '。')
            {
                sb.Append('。');
            }
        }

        return sb.ToString();
    }
}
