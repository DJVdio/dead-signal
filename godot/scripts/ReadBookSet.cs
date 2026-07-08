using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SkillSet.cs / HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载单个幸存者的"个人已读书集"：谁读完了哪些书（按 book id）。配方书门槛由"营地全局已读"
// 升级为"制作者本人已读"后，判据即读本对象——制作骨刀需要制作者本人读完《野外生存指南》。

/// <summary>
/// 单个幸存者的已读书集（纯逻辑，无 Godot 依赖）：持有该幸存者读完的全部 book id。
/// 与全局 <see cref="BookData.IsRead"/>（营地任意人读过即置位）相互独立——配方门槛走本对象（按制作者判定），
/// 全局态仍供库存"已读"标记等"营地视角"消费点使用。
/// </summary>
public sealed class ReadBookSet
{
    private readonly HashSet<string> _read = new();

    /// <summary>本幸存者是否读完某书（按 book id）。配方书门槛的权威判据。</summary>
    public bool HasRead(string bookId) => _read.Contains(bookId);

    /// <summary>标记本幸存者读完某书（幂等，重复调用无副作用）。阅读结算时按读者调用。</summary>
    public void MarkRead(string bookId) => _read.Add(bookId);

    /// <summary>本幸存者已读的全部 book id 只读快照，供 UI/存档读取。</summary>
    public IReadOnlyCollection<string> ReadBooks => _read;
}
