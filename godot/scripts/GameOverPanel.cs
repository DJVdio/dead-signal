using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 「游戏结束」模态（最小版）：全体幸存者死亡时弹出。
/// 显示结束提示 + 「重新开始」（重载场景开新一局）+「退出游戏」（<see cref="GetTree"/>.Quit()）。
/// 弹出即 <c>Engine.TimeScale = 0</c> 暂停——游戏已结束。重开前必须先把 TimeScale 复位回 1，
/// 否则新场景一起步就被冻住。
/// <para>
/// <b>出口只有这两个</b>：重开 = <c>ReloadCurrentScene</c>（就地重来，不回主菜单）、退出 = <c>Quit</c>。
/// 主菜单（<see cref="MainMenu"/>，<c>project.godot</c> 的 <c>main_scene</c>）与存档系统
/// （<see cref="SavePanel"/>/<see cref="SaveManager"/> 的相位轮转自动存档）都已建成，
/// 但本面板<b>没有</b>接「回主菜单」入口——要加的话是往这里接一个
/// <c>ChangeSceneToFile("res://scenes/MainMenu.tscn")</c>（记得同样先复位 TimeScale）。
/// </para>
/// 骨架照 <see cref="ChoicePanel"/>：CanvasLayer + <see cref="UiStyle.BuildModalShell"/>。
/// </summary>
public sealed partial class GameOverPanel : CanvasLayer
{
    private const string TitleText = "游戏结束";
    private const string SubText = "营地无人生还。";

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _, "GameOverPanel",
            overlayAlpha: 0.8f,
            panelSize: new Vector2(420, 260),
            borderColor: new Color(0.5f, 0.15f, 0.15f));

        var title = new Label();
        title.Position = new Vector2(28, 40);
        title.Size = new Vector2(364, 48);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Text = TitleText;
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(0.85f, 0.3f, 0.3f));
        panel.AddChild(title);

        var sub = new Label();
        sub.Position = new Vector2(28, 104);
        sub.Size = new Vector2(364, 48);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sub.Text = SubText;
        sub.AddThemeFontSizeOverride("font_size", 16);
        sub.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.7f));
        panel.AddChild(sub);

        var restartBtn = new Button();
        restartBtn.Text = "重新开始";
        restartBtn.Position = new Vector2(28, 190);
        restartBtn.Size = new Vector2(174, 44);
        restartBtn.Pressed += Restart;
        UiStyle.StyleButton(restartBtn, new Color(0.25f, 0.5f, 0.3f), fontSize: 16);
        panel.AddChild(restartBtn);

        var quitBtn = new Button();
        quitBtn.Text = "退出游戏";
        quitBtn.Position = new Vector2(218, 190);
        quitBtn.Size = new Vector2(174, 44);
        quitBtn.Pressed += () => GetTree().Quit();
        UiStyle.StyleButton(quitBtn, new Color(0.65f, 0.2f, 0.2f), fontSize: 16);
        panel.AddChild(quitBtn);
    }

    /// <summary>
    /// 重开一局：先复位 <c>Engine.TimeScale = 1</c>（Show 时置 0，不复位则新场景冻住），
    /// 再 <see cref="CombatFeed.Reset"/> 清 static 订阅防残留死节点引用，最后重载当前场景。
    /// 顺序要紧：TimeScale 必须先于 ReloadCurrentScene。
    /// </summary>
    private void Restart()
    {
        Engine.TimeScale = 1;
        CombatFeed.Reset();
        GetTree().ReloadCurrentScene();
    }

    /// <summary>弹出并暂停：挂到指定 HUD 层，暂停整局（不设恢复——游戏已结束）。</summary>
    public static void Show(CanvasLayer hud)
    {
        Engine.TimeScale = 0;
        hud.AddChild(new GameOverPanel());
    }
}
