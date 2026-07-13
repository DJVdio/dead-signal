using Godot;

namespace DeadSignal.Godot;

// Corpse 的存档面（partial，独立文件）：把绘制参数（颜色/半径）暴露成只读属性。
// 它们平时是私有的绘制细节，但存档要能把"那具穿牛仔外套的丧尸尸体"照原样摆回来——
// 颜色是玩家分辨"这是同伴还是丧尸"的唯一线索，不能靠默认值糊过去。

public sealed partial class Corpse
{
    /// <summary>尸体颜色（同伴/劫掠者/丧尸画出来不一样）。</summary>
    public Color BodyTint => _body;

    /// <summary>尸体绘制半径。</summary>
    public float BodyRadius => _r;
}
