using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 「游戏结束」模态（最小版）：全体幸存者死亡时弹出。
/// 显示结束提示 + 一个「退出游戏」按钮（<see cref="GetTree"/>.Quit()）。
/// 弹出即 <c>Engine.TimeScale = 0</c> 暂停——游戏已结束，不设恢复口径。
/// 重开/回标题/存档等菜单系统本轮不做（以后另做）。
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

        var quitBtn = new Button();
        quitBtn.Text = "退出游戏";
        quitBtn.Position = new Vector2(110, 190);
        quitBtn.Size = new Vector2(200, 44);
        quitBtn.Pressed += () => GetTree().Quit();
        UiStyle.StyleButton(quitBtn, new Color(0.65f, 0.2f, 0.2f), fontSize: 16);
        panel.AddChild(quitBtn);
    }

    /// <summary>弹出并暂停：挂到指定 HUD 层，暂停整局（不设恢复——游戏已结束）。</summary>
    public static void Show(CanvasLayer hud)
    {
        Engine.TimeScale = 0;
        hud.AddChild(new GameOverPanel());
    }
}
