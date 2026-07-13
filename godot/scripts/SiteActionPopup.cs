using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>右键菜单</b>：对着一扇门 / 一段围栏右键，弹一小列可选动作（撬锁 / 静默拆除 / 破坏 …）。
/// 内容规则全在纯逻辑 <see cref="SiteActions"/>，本类只负责**把它画出来、把点击回传**。
///
/// <para>
/// <b>为什么用 <see cref="PopupMenu"/> 而不是 <c>ChoicePanel</c></b>：<c>ChoicePanel</c> 是**模态大面板**
/// （教学关"处置幸存者"三选一，会冻结时标）——拿它来问"这门你是撬还是砸"太重了。
/// 用户偏好简化，主 agent 也明令"别搞繁琐"：一个贴着鼠标弹出的小列表，选完即走，<b>不暂停、不遮挡战场</b>。
/// </para>
///
/// <para>
/// <b>⚠️ 不可用的选项照样列出来，只是灰掉</b>（<see cref="SiteActionOption.Enabled"/>=false）。
/// 直接把"撬锁"藏掉，玩家会以为"这门撬不了"；而真相是"你没带铁丝"。<b>列出来 + 写明原因，才教得会他。</b>
/// </para>
///
/// <para>
/// 菜单**不暂停游戏**：你在这儿犹豫的每一秒，门外那群东西都在往前挪。这是有意的。
/// </para>
/// </summary>
public sealed partial class SiteActionPopup : PopupMenu
{
    /// <summary>玩家选定了一个动作（只在 <see cref="SiteActionOption.Enabled"/> 的项上触发）。</summary>
    public event Action<SiteAction>? ActionChosen;

    private readonly List<SiteAction> _index = new(); // 菜单项下标 → 动作

    public override void _Ready()
    {
        // 贴着鼠标的小列表；不抢焦点、不暂停。
        Transparent = false;
        IdPressed += OnIdPressed;
    }

    /// <summary>
    /// 在 <paramref name="screenPos"/> 处弹出菜单。<paramref name="title"/> 是目标名（"仓库的门" / "围栏"），
    /// 让玩家确认自己点中的是什么。
    /// </summary>
    public void ShowFor(string title, IReadOnlyList<SiteActionOption> options, Vector2 screenPos)
    {
        Clear();
        _index.Clear();

        // 标题行（不可选）：告诉玩家他正在对**哪个东西**下手。
        AddItem(title);
        int titleIdx = ItemCount - 1;
        SetItemDisabled(titleIdx, true);
        SetItemAsSeparator(titleIdx, false);
        _index.Add(SiteAction.OpenDoor); // 占位（标题行永不触发：它 disabled）
        AddSeparator();
        _index.Add(SiteAction.OpenDoor); // 占位（分隔符同样不触发）

        foreach (SiteActionOption o in options)
        {
            // 一行把「代价」说完：动作名 + 提示（安静/很响、几秒、成功率、还剩几根铁丝）。
            // 玩家在按下之前就该知道自己在选什么——这正是"噪音 vs 效率"取舍能成立的前提。
            string label = o.Enabled ? $"{o.Label}　—　{o.Hint}" : $"{o.Label}　—　{o.DisabledReason}";
            AddItem(label, id: _index.Count);
            int idx = ItemCount - 1;
            if (!o.Enabled)
            {
                SetItemDisabled(idx, true); // 灰掉但**仍然可见**（见类注）
            }
            _index.Add(o.Action);
        }

        ResetSize();
        Position = new Vector2I((int)screenPos.X, (int)screenPos.Y);
        Popup();
    }

    private void OnIdPressed(long id)
    {
        int i = (int)id;
        if (i >= 0 && i < _index.Count)
        {
            ActionChosen?.Invoke(_index[i]);
        }
    }
}
