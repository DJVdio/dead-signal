using Godot;
using System;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 固定造型下的真实武器动作画廊：七类持械动作 × 八方向 × 蓄力/出手/收招。
/// 覆盖七名具名幸存者和八种袭击者外观，只用于视觉验收，不进入玩家流程。
/// </summary>
public sealed partial class WeaponActionAudit : Node2D
{
    internal sealed record AuditSubject(string Key, string DisplayName, int RaiderModelIndex = -1);

    internal static readonly AuditSubject[] Characters =
    {
        new("sam", "山姆"),
        new("notty", "诺蒂"),
        new("christine", "克莉丝汀"),
        new("rat", "耗子"),
        new("doug", "道格"),
        new("nightingale", "南丁格尔"),
        new("pete", "皮特"),
        new("raider01", "袭击者 01", 0),
        new("raider02", "袭击者 02", 1),
        new("raider03", "袭击者 03", 2),
        new("raider04", "袭击者 04", 3),
        new("raider05", "袭击者 05", 4),
        new("raider06", "袭击者 06", 5),
        new("raider07", "袭击者 07", 6),
        new("raider08", "袭击者 08", 7),
    };

    private static readonly (string Label, string Weapon, WeaponAttackPose Pose)[] Cases =
    {
        ("单手挥砍", "棍棒", WeaponAttackPose.OneHandSwing),
        ("单手戳刺", "刺剑", WeaponAttackPose.OneHandThrust),
        ("单手射击", "手枪", WeaponAttackPose.OneHandShot),
        ("双手挥砍", "长剑", WeaponAttackPose.TwoHandSwing),
        ("双手戳刺", "草叉", WeaponAttackPose.TwoHandThrust),
        ("双手射击", "步枪", WeaponAttackPose.TwoHandShot),
        ("拉弓射箭", "长弓", WeaponAttackPose.BowShot),
    };

    private static readonly string[] DirectionLabels =
        { "南", "西南", "西", "西北", "北", "东北", "东", "东南" };

    private string? _screenshotPath;
    private int _renderedFrames;

    public override void _Ready()
    {
        string requested = System.Environment.GetEnvironmentVariable("DEAD_SIGNAL_AUDIT_CHARACTER") ?? "sam";
        AuditSubject? character = Characters.FirstOrDefault(entry =>
            string.Equals(entry.Key, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.DisplayName, requested, StringComparison.Ordinal));
        if (character is null)
            throw new InvalidOperationException($"未知动作验收角色：{requested}");

        _screenshotPath = System.Environment.GetEnvironmentVariable("DEAD_SIGNAL_AUDIT_SCREENSHOT");
        if (!string.IsNullOrWhiteSpace(_screenshotPath))
            SetProcess(true);

        DisplayServer.WindowSetSize(new Vector2I(1900, 1040));
        DisplayServer.WindowSetTitle($"Dead Signal · {character.DisplayName} 武器动作验收");
        AddLabel($"{character.DisplayName} · 固定个人造型 · 真实武器动作验收", new Vector2(24, 12), 22);
        int phase = Math.Clamp(ParsePhase(
            System.Environment.GetEnvironmentVariable("DEAD_SIGNAL_AUDIT_PHASE")), 0, 2);
        string[] phaseLabels = { "蓄力", "出手", "收招" };
        AddLabel($"当前关键帧：{phaseLabels[phase]}", new Vector2(24, 43), 15);

        for (int direction = 0; direction < 8; direction++)
            AddLabel(DirectionLabels[direction], new Vector2(188 + direction * 213, 70), 15, 192);

        float[] keyFrames = { 0.12f, 0.45f, 0.82f };
        for (int poseIndex = 0; poseIndex < Cases.Length; poseIndex++)
        {
            (string label, string weaponName, WeaponAttackPose pose) = Cases[poseIndex];
            float feetY = 190 + poseIndex * 123;
            AddLabel($"{label}\n{weaponName}", new Vector2(18, feetY - 58), 14, 145);

            for (int direction = 0; direction < 8; direction++)
            {
                Actor actor = CreateActor(character, weaponName);
                var sprite = new ActorSprite();
                AddChild(sprite);
                sprite.Bind(actor);
                sprite.SetAuditDirectionColumn(direction);
                sprite.SetAuditAttackFrame(pose, keyFrames[phase]);
                sprite.EnterCinematic();
                sprite.Position = new Vector2(266 + direction * 213, feetY);
                sprite.Scale = Vector2.One * 1.55f;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (string.IsNullOrWhiteSpace(_screenshotPath) || ++_renderedFrames < 3)
            return;

        Error result = GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        if (result != Error.Ok)
            GD.PushError($"动作验收截图保存失败：{_screenshotPath} ({result})");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
        SetProcess(false);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(1900, 1040)), new Color(0.025f, 0.032f, 0.03f));
        for (int row = 0; row < Cases.Length; row++)
        {
            float y = 91 + row * 123;
            DrawRect(new Rect2(160, y, 1715, 112), new Color(0.07f, 0.08f, 0.075f), true);
            for (int direction = 0; direction < 8; direction++)
                DrawRect(new Rect2(160 + direction * 213, y, 207, 112),
                    new Color(0.30f, 0.27f, 0.18f), false, 1f);
        }
    }

    private static Actor CreateActor(AuditSubject subject, string weaponName)
    {
        if (subject.RaiderModelIndex >= 0)
        {
            Weapon weapon = ModdedWeaponRegistry.WeaponByName(weaponName)
                ?? throw new InvalidOperationException($"动作画廊找不到武器：{weaponName}");
            return Raider.Create(
                new Rect2(0, 0, 1, 1),
                () => Array.Empty<Actor>(),
                displayName: subject.DisplayName,
                weapon: weapon,
                visualRng: new SequenceRandomSource(subject.RaiderModelIndex + 0.1));
        }

        Pawn pawn = Pawn.Create(subject.DisplayName, StartingWeapon.None, Colors.White);
        if (!pawn.EquipWeapon(weaponName, Hand.Right))
            throw new InvalidOperationException($"动作画廊无法装备：{weaponName}");
        return pawn;
    }

    private static int ParsePhase(string? value)
        => int.TryParse(value, out int phase) ? phase : 1;

    private void AddLabel(string text, Vector2 position, int fontSize, float width = 0f)
    {
        var label = new Label
        {
            Text = text,
            Position = position,
            HorizontalAlignment = width > 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        if (width > 0)
            label.Size = new Vector2(width, 48);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.92f, 0.89f, 0.78f));
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 3);
        AddChild(label);
    }
}
