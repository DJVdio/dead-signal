using System;
using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 NarrativeSpot.cs / SurvivorPerks.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// authored 背景设定 → 开局身体状态的单一事实源。
// 角色的经历（用户手写剧情）有些是**看得见的**：山姆九岁那年为救诺蒂被野狗撕咬，
// 失去左手小指与无名指——这不是一段可以只写在文档里的过去，它就长在开局的那只手上。
// 关系/性格仍是 authored 剧情（不做数值关系系统）；此处只承载"背景在躯体上留下的痕迹"。

/// <summary>角色 authored 背景在躯体上的开局痕迹（建角时一次性应用）。</summary>
public static class SurvivorBackstory
{
    /// <summary>山姆（"小英雄"）。开局左手缺小指、无名指。</summary>
    public const string Sam = "山姆";

    /// <summary>诺蒂（"书虫"）。山姆的义兄弟，祖母领回来的、父亲战友的儿子。</summary>
    public const string Nordi = "诺蒂";

    private static readonly string[] SamSeveredParts = { HumanBody.LeftPinky, HumanBody.LeftRing };

    /// <summary>
    /// 某角色开局即已失去（切除）的部位。山姆：<b>左手小指 + 左手无名指</b>——
    /// 九岁时一条发疯的野狗扑上来撕咬诺蒂，他冲上去救下了他，代价是这两根手指，人们从此叫他"小英雄"。
    /// 其余角色为空（无开局残缺）。
    /// </summary>
    public static IReadOnlyList<string> SeveredAtStart(string name) =>
        name == Sam ? SamSeveredParts : Array.Empty<string>();

    /// <summary>
    /// 把开局痕迹应用到躯体：切除 <see cref="SeveredAtStart"/> 的部位并重算残疾净惩罚
    /// （手指按引擎既有通则结算 −7%/指的操作惩罚，<b>不为任何人开豁免、也不额外加惩罚</b>）。
    /// 建角时调用一次；无痕迹的角色为空操作。
    /// </summary>
    public static void ApplyTo(string name, Body body)
    {
        if (body == null)
        {
            return;
        }

        foreach (string part in SeveredAtStart(name))
        {
            body.Sever(part);
        }

        body.RecalculatePenalties(); // Sever 本身不重算（战斗路径由 Effects 统一调），建角路径须自己收口
    }
}
