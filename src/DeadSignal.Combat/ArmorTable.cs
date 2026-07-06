namespace DeadSignal.Combat;

/// <summary>
/// 唯一权威护甲数据源。原先 Godot(CombatData) 与 Sim(Program/DuelReport) 各自维护护甲工厂，
/// 此处统一收拢。SurvivorArmor / ZombieHide 采 Godot 现行值；布衣/皮甲/板甲为 Sim 参数化甲组
/// （Program 与 DuelReport 原本重复定义、数值一致，合并至此，输出不变）。
/// 防御值均为原型期<b>拟定待调</b>（锐防约 2×钝防，板甲比例更高）。
/// 传入 Resolve 前仍会经 <see cref="CombatResolver.OrderOuterToInner"/> 归一层序。
/// </summary>
public static class ArmorTable
{
    // ---- Godot 消费层套装 ----

    /// <summary>幸存者：皮夹克(外) + 贴身布衣(内) 两层。</summary>
    public static IReadOnlyList<ArmorLayer> SurvivorArmor() => new[]
    {
        new ArmorLayer { Name = "皮夹克", SharpDefense = 6, BluntDefense = 3, Weight = 3, Slot = ArmorSlot.Outer },   // 拟定待调
        new ArmorLayer { Name = "贴身布衣", SharpDefense = 2, BluntDefense = 1, Weight = 1, Slot = ArmorSlot.Skin }, // 拟定待调
    };

    /// <summary>丧尸：一层腐烂硬皮（对钝器略韧）。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => new[]
    {
        new ArmorLayer { Name = "腐皮", SharpDefense = 1.5, BluntDefense = 3, Weight = 0, Slot = ArmorSlot.Skin }, // 拟定待调
    };

    // ---- Sim 参数化甲层（单层工厂，供聚合/对决按层组合）----

    /// <summary>布衣（贴身层）。</summary>
    public static ArmorLayer Cloth() => new() { Name = "布衣", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2, Weight = 1 };    // 拟定待调

    /// <summary>皮甲（外套层）。</summary>
    public static ArmorLayer Leather() => new() { Name = "皮甲", Slot = ArmorSlot.Outer, SharpDefense = 12, BluntDefense = 6, Weight = 4 }; // 拟定待调

    /// <summary>板甲（装甲层）。</summary>
    public static ArmorLayer Plate() => new() { Name = "板甲", Slot = ArmorSlot.Plate, SharpDefense = 34, BluntDefense = 11, Weight = 12 }; // 拟定待调
}
