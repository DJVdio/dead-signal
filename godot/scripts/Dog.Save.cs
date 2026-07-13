using DeadSignal.Combat;

namespace DeadSignal.Godot;

// Dog 的存档面（partial，独立文件）。狗的身体（Body）继承自 Actor，是 protected——
// 外人够不着，故这两个方法必须住在 Dog 里面。

public sealed partial class Dog
{
    /// <summary>导出布鲁斯的身体状态（他也会被咬、被砸断腿）。</summary>
    public BodySnapshot CaptureBody() => Body.Capture();

    /// <summary>读档：把身体状态盖回去。</summary>
    public void RestoreBody(BodySnapshot s) => Body.Restore(s);
}
