using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationWalls.cs / RuinedChurch.cs / RefugeeCamp.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（丧尸实体、感知 raycast、门开关、唤醒后的追击/寻路）归 Zombie.cs / TestExploration 运行时层。
//
// ================= [SPEC-T60] 探索期丧尸威胁模型：唤醒规则 =================
//
// 🔴 用户口径（原话，authored 唯一事实源）：
//    「普通丧尸根据视野、噪音、靠近这些唤醒;这几个房间里的丧尸作为一类特殊丧尸,
//      有且仅有开门会唤醒,变为普通丧尸。」
//
// 背景（door-scout 摸清）：`Zombie.Think` 原本是**全局昼夜门控**（白天休眠、夜晚才感知追击），而探索
// 发生在 DayExplore（白天）⇒ 改造前探索期丧尸是**站着的休眠布景、根本不攻击玩家**（TODO⑨"交互底座未接通"）。
// 本轴把探索期威胁接上：分**两类**丧尸——
//   · **普通丧尸**：探索期照常靠**视野 / 噪音 / 靠近（嗅觉）**感知玩家 → 唤醒（转入追击）。
//   · **门后特殊丧尸**（教堂墓地后院门后 / 难民各房门后 / 警察拘留区铁门后）：一类特殊丧尸，
//     **完全冻结**、对视野/噪音/靠近**全免疫**，有且仅有**对应门被打开**才唤醒；唤醒后**转为普通丧尸**
//     （此后照常感知追击）。
//
// 🔴 这条同时把三关的门×丧尸谜题从「门宽/固定像素卡住丧尸」重定义为「开门激活门后丧尸」——触发绑门实体、
//    与尺度无关 ⇒ 解绑白昼锥 300px / 跳脸 90 / 门宽 48<52 等固定像素约束。
//    **Phase2 已据此放大三关**（教堂/难民营地 3200×2200、警察局 2800×1900，见 ExplorationLevelSize.Overrides）——
//    「教堂/难民维持不放大」的旧裁定就是被本条推翻的。⚠️ 门宽/过道宽等**物理常量仍然不缩放**（按人体与丧尸直径标定）。
//
// 本文件只出**纯判定函数**（规则单一事实源，供 Zombie.cs 与三关接线共用、供单测钉红绿）。
// 门→门后丧尸的**拓扑关联**（哪些丧尸锁在哪扇门后）住在各关几何类里
// （RuinedChurch.GraveyardWakeDoors / RefugeeCamp.WakeDoorFor / ExplorationWalls.PoliceSpotBehindHoldingDoor）。

/// <summary>探索期丧尸唤醒规则（纯逻辑）。见文件头 [SPEC-T60] 说明。</summary>
public static class ZombieActivation
{
    /// <summary>
    /// 门后特殊丧尸在其门打开前**完全冻结**（免疫视野/噪音/靠近）。普通丧尸（<paramref name="doorLocked"/>=false）从不冻结。
    /// </summary>
    public static bool IsFrozen(bool doorLocked, bool activated) => doorLocked && !activated;

    /// <summary>
    /// 该丧尸是否**响应噪音**（走 <c>Actor.HearNoise</c> 那条）。
    /// <b>只有探索期、冻结的门后特殊丧尸不响应</b>（免疫噪音，不被普通丧尸的动静连锁唤醒）；
    /// 普通丧尸 / 已激活的前特殊丧尸 / 营地丧尸（非探索，<paramref name="explorationMode"/>=false，本轴不接管）都照常响应。
    /// </summary>
    public static bool RespondsToNoise(bool explorationMode, bool doorLocked, bool activated)
        => !(explorationMode && IsFrozen(doorLocked, activated));

    /// <summary>
    /// 该丧尸是否**参与视野/靠近（嗅觉）感知**。判据同 <see cref="RespondsToNoise"/>：探索期冻结的门后特殊丧尸不参与，
    /// 其余照常（普通丧尸据此在探索期靠视野/靠近唤醒）。
    /// </summary>
    public static bool RespondsToPerception(bool explorationMode, bool doorLocked, bool activated)
        => !(explorationMode && IsFrozen(doorLocked, activated));

    /// <summary>
    /// 打开 <paramref name="openedDoor"/> 是否唤醒某门后特殊丧尸——当它的**唤醒门集** <paramref name="wakeDoors"/>
    /// 含该门、且尚未激活时为真（开无关的门不唤醒、已激活不重复）。
    /// </summary>
    public static bool DoorOpenActivates(IReadOnlyCollection<string> wakeDoors, string openedDoor, bool activated)
    {
        if (activated || wakeDoors == null || openedDoor == null)
            return false;
        foreach (string d in wakeDoors)
        {
            if (d == openedDoor)
                return true;
        }
        return false;
    }
}
