using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 正在搜刮的角色**头顶**的逐件搜刮进度条（世界内指示，<b>不是</b> HUD 面板）。
///
/// <para>
/// ⚠️ <b>为什么必须是世界内的、而不是一块 UI 面板</b>：用户拍板「允许玩家控制一个角色去搜刮转物品，
/// <b>然后控制另一个角色</b>」。搜刮是**派下去的一件持续的活**（像 RimWorld 派人干活、像《这是我的战争》
/// 夜里派人搜刮），不是一次把玩家锁进去的模态交互。所以进度必须长在**那个角色头上**——
/// 玩家的镜头和控制权始终自由，可以立刻切去点别人；<b>多个人同时各搜各的，就有多条这样的进度条同时挂着</b>。
/// 若做成一块底部面板，"一个人蹲着掏尸体、另一个人在门口盯着围栏外的动静"这个分工当场归零。
/// </para>
///
/// 与 <see cref="StatusIconStrip"/> / <c>ActorSprite</c> 同构：挂在 iso_layer 下、<b>不是</b> Actor 的子节点
/// （Actor 本体在不可见 LogicLayer 里，做它的子节点会跟着不可见），故每帧把 actor 的 cartesian 逻辑位置
/// 经 <c>Iso.Project</c> 投到屏幕坐标再定位到其头顶（状态图标条之上）。
///
/// 显示三样东西——玩家全靠它们决定 <b>拿到第几件就跑</b>：
/// ① 正在取出的是什么 ② 这一件还差多少（条） ③ <b>全部搜完还要等多久</b>（第三样才是决策依据）。
/// </summary>
public sealed partial class LootProgressBar : Node2D
{
    /// <summary>条宽/高（像素，占位视觉，非美术）。</summary>
    private const float BarWidth = 54f;
    private const float BarHeight = 5f;

    private Actor _actor = null!;
    private bool _bound;

    private ColorRect _barBack = null!;
    private ColorRect _barFill = null!;
    private Label _title = null!;
    private Label _remain = null!;

    private string _lastTitle = "";
    private string _lastRemain = "";

    /// <summary>绑定到这个正在搜刮的角色（一名搜刮者一条，多人并发就多条）。</summary>
    public void Bind(Actor actor)
    {
        _actor = actor;
        _bound = true;
        ZIndex = 2; // 压在状态图标条（ZIndex 1）之上

        // 头顶：状态图标条约在 y ≈ -r*3.4-22（见 StatusIconStrip），再抬一档给进度条腾位。
        float topY = -_actor.Radius * 3.4f - 44f;

        _title = MakeLabel(13, new Color(1f, 0.96f, 0.86f));
        _title.Position = new Vector2(-BarWidth * 0.5f - 20f, topY - 18f);
        _title.CustomMinimumSize = new Vector2(BarWidth + 40f, 0);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_title);

        _barBack = new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.10f, 0.85f),
            Position = new Vector2(-BarWidth * 0.5f, topY),
            Size = new Vector2(BarWidth, BarHeight),
        };
        AddChild(_barBack);

        _barFill = new ColorRect
        {
            Color = new Color(0.85f, 0.76f, 0.42f), // 暖黄：在翻东西（区别于失血红/掩体青）
            Position = new Vector2(-BarWidth * 0.5f, topY),
            Size = new Vector2(0f, BarHeight),
        };
        AddChild(_barFill);

        _remain = MakeLabel(11, new Color(0.92f, 0.88f, 0.75f));
        _remain.Position = new Vector2(-BarWidth * 0.5f - 26f, topY + BarHeight + 1f);
        _remain.CustomMinimumSize = new Vector2(BarWidth + 52f, 0);
        _remain.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_remain);

        SyncPosition();
    }

    /// <summary>
    /// 刷新这一条（CampMain 每帧对每个在搜的人调一次）。
    /// <paramref name="remainingRealSeconds"/> 是**全部搜完还要多久**（按这个人自己的效率算），
    /// 不是当前这件还要多久——玩家问的是"门口那只丧尸等得起吗"，所以显示的必须是总账。
    /// </summary>
    public void Refresh(string itemName, float itemProgress, int remainingCount, float remainingRealSeconds)
    {
        _barFill.Size = new Vector2(BarWidth * Mathf.Clamp(itemProgress, 0f, 1f), BarHeight);

        string title = $"正在取出 {itemName}";
        if (title != _lastTitle)
        {
            _lastTitle = title;
            _title.Text = title;
        }

        // 效率 0（断手）⇒ 无穷大：如实说"搜不动"，别印一个假秒数骗玩家在那儿干等。
        string remain = float.IsFinite(remainingRealSeconds)
            ? $"还剩 {remainingCount} 件 · 约 {remainingRealSeconds:0} 秒"
            : $"还剩 {remainingCount} 件 · 这双手搜不动";
        if (remain != _lastRemain)
        {
            _lastRemain = remain;
            _remain.Text = remain;
        }
    }

    public override void _Process(double delta)
    {
        if (!_bound)
        {
            return;
        }
        // 角色没了（死亡即 QueueFree）→ 同步销毁本条，避免悬挂（同 StatusIconStrip / ActorSprite）。
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.Alive)
        {
            QueueFree();
            return;
        }
        SyncPosition();
    }

    private void SyncPosition() => Position = Iso.Project(_actor.GlobalPosition);

    private static Label MakeLabel(int fontSize, Color color)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", 4);
        return l;
    }
}
