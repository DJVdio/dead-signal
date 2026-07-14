using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 ItemIcons.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>一段素材出处（面板里的一个分节）。</summary>
/// <param name="Title">分节标题（如「物品图标」）。</param>
/// <param name="License">授权类型（如「CC BY 3.0（须署名）」）——**这一行是合规的要害，不许空**。</param>
/// <param name="Lines">正文各行：来源、作者、改动说明等。</param>
public readonly record struct CreditsSection(string Title, string License, IReadOnlyList<string> Lines);

/// <summary>
/// **游戏内素材署名的单一事实源**（纯 C#，无 Godot 依赖 ⇒ 可单测）。
///
/// <para>
/// <b>为什么不直接读 CREDITS.md</b>：那两份 md 是**仓库文档**，不是游戏资源——Godot 导出时
/// 非导入类文件不进包，玩家手里的游戏根本读不到它们。而 CC BY 3.0 要求署名对**使用者**可见，
/// 光在 GitHub 上挂一份 md 是不够的。所以游戏内显示的文本在这里内嵌，导出不导出都在。
/// </para>
///
/// <para>
/// <b>护栏</b>：<c>CreditsContentTests</c> 断言这里必须出现「game-icons.net」「CC BY 3.0」与作者名单——
/// 谁把署名删了，测试直接红。这不是洁癖：删掉它，这个项目对那 4000 多张图的使用就不再合规。
/// </para>
/// </summary>
public static class CreditsContent
{
    /// <summary>面板标题。</summary>
    public const string Title = "素材出处";

    /// <summary>面板副标题：一句话说清这一页存在的理由。</summary>
    public const string Subtitle = "这个游戏用了一些别人免费给出来的东西。这一页是把它们记在这儿。";

    /// <summary>全部分节（按显示顺序）。</summary>
    public static IReadOnlyList<CreditsSection> Sections => _sections;

    private static readonly IReadOnlyList<CreditsSection> _sections = new[]
    {
        new CreditsSection(
            "物品图标（全部 100 余张）",
            "CC BY 3.0 —— 可自由使用与修改，但必须署名。以下即为署名。",
            new[]
            {
                "来源：game-icons.net",
                "作者：Lorc、Delapouite、Skoll、Carl Olsen、John Colburn、Willdabeast、Lucas、Irongamer、Sbed",
                "　　　及 game-icons.net 的其他贡献者。",
                "改动说明（CC BY 要求标明改动）：原图为 512×512 的矢量图（黑底白形）。",
                "　　　我们剥掉黑色底板、缩放到 32×32、把边缘阈值化成硬边（得到像素风轮廓），",
                "　　　并统一填成米白色。",
            }),

        new CreditsSection(
            "幸存者头像（13 张）",
            "CC0 1.0（公共领域捐赠）—— 无署名义务；此处列出只为留个出处。",
            new[]
            {
                "来源：OpenGameArt.org —— Survivor Portraits (Post-Apocalyptic Survival)",
            }),

        new CreditsSection(
            "引擎",
            "MIT License",
            new[]
            {
                "Godot Engine（.NET 版）—— godotengine.org",
            }),
    };
}
