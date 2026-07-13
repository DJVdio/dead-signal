using System.Text;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 表现逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 把一次命中结果（攻击方名 + 承伤方名 + <see cref="AttackOutcome"/>）翻译成**一行中文战斗日志**。
/// 纯函数、无副作用、脱 Godot。
///
/// 视角：**攻击方归属视角**——有攻击方时写"A 击中 B 的左手，撕裂 20，断肢！"；攻击方为空/null
/// （环境伤害/无源）时优雅退回**承伤方视角**"B 的左手被撕裂 20，断肢！"，不凭空捏造攻击方。
/// 武器名不在 CombatFeed 载荷内（AttackOutcome 无此字段），故暂不写武器，待日后总线扩载荷再补。
///
/// 措辞口径：伤害动词按<b>伤害类型</b>分——钝器=砸伤、锐器=撕裂（有攻击方用主动式"砸伤/撕裂"、
/// 无攻击方退被动式"被砸伤/被撕裂"）；被甲完全挡下走"挡下"专述；断肢/骨折/脑震荡/流血依序追加为
/// 分句；致死追加专门措辞"当场毙命！"。
/// </summary>
public static class CombatLogFormatter
{
    public static string Format(string? attackerName, string targetName, AttackOutcome hit)
    {
        string name = string.IsNullOrWhiteSpace(targetName) ? "无名者" : targetName;
        bool hasAttacker = !string.IsNullOrWhiteSpace(attackerName);
        // 攻击方前缀 + 承伤方定语：有攻击方"{A} 击中 {B} 的{部位}"，无则退回"{B} 的{部位}"。
        string subject = hasAttacker ? $"{attackerName!.Trim()} 击中 {name} 的{hit.PartName}" : $"{name} 的{hit.PartName}";

        if (hit.Blocked)
        {
            // 被甲完全挡下：不写伤口动词，只述"甲片挡下"。有攻击方时前缀已带"击中"，补逗号断句。
            return hasAttacker
                ? $"{subject}，被甲片挡下，未伤分毫。"
                : $"{subject}被甲片挡下，未伤分毫。";
        }

        // 伤害动词按类型分：钝=砸伤、锐=撕裂。有攻击方用主动式，无攻击方退被动式。
        string verb = hit.FinalType == DamageType.Blunt ? "砸伤" : "撕裂";

        var sb = new StringBuilder();
        sb.Append(hasAttacker
            ? $"{subject}，{verb} {hit.Damage:0.#}"
            : $"{subject}被{verb} {hit.Damage:0.#}"); // 小数伤害显示一位小数（去尾零，[SPEC-B14-补6]）

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
