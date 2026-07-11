using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 LightSource.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 一个场景(营地/探索关)里已点亮的**固定光源**集合 + 位置查询。运行时(CampMain/ExplorationLevel)
// 从 camp.json / 关卡数据把固定光源灌进来，vision-render / enemy-vision 经 StrongestAt 查某点最强贡献。
// 手持光源随持有者移动、每帧位置变，不常驻本集合——查询时用 extraLights 临时并入(见 StrongestAt 重载)。

/// <summary>
/// 一个场景的固定光源场：持一批 <see cref="PlacedLight"/>，供「给定位置返回最强光源贡献」查询。
/// 固定光源建/预置时 <see cref="AddFixed"/> 灌入；手持光源(随人动)不入场，查询时经重载临时并入。
/// </summary>
public sealed class LightField
{
    private readonly List<PlacedLight> _lights = new();

    /// <summary>当前固定光源(只读视图)。</summary>
    public IReadOnlyList<PlacedLight> Lights => _lights;

    /// <summary>光源盏数。</summary>
    public int Count => _lights.Count;

    /// <summary>直接加一盏已构造的光源。</summary>
    public void Add(PlacedLight light) => _lights.Add(light);

    /// <summary>
    /// 按光源键(对齐 <see cref="LightSource"/> 目录 / camp.json lights.key)在 (<paramref name="x"/>,<paramref name="y"/>)
    /// 落一盏固定光源。键未登记则不加、返回 <c>false</c>(容错坏数据)。
    /// </summary>
    public bool AddFixed(string key, float x, float y)
    {
        var profile = LightSource.Find(key);
        if (profile is null)
        {
            return false;
        }
        _lights.Add(new PlacedLight(x, y, profile.Value));
        return true;
    }

    /// <summary>清空(换场/重建关卡时)。</summary>
    public void Clear() => _lights.Clear();

    /// <summary>
    /// 由一批 (光源键, x, y) 构建一个固定光源场(运行时从 camp.json / 关卡数据 parse 后调用)。
    /// 键未登记的条目静默跳过(容错坏数据)。
    /// </summary>
    public static LightField FromFixed(IEnumerable<(string Key, float X, float Y)> entries)
    {
        var field = new LightField();
        if (entries != null)
        {
            foreach (var (key, x, y) in entries)
            {
                field.AddFixed(key, x, y);
            }
        }
        return field;
    }

    /// <summary>**查询入口**：给定位置在本场固定光源里最强的一盏贡献 ∈[0,1]。</summary>
    public float StrongestAt(float x, float y)
        => LightSource.StrongestContribution(x, y, _lights);

    /// <summary>
    /// **查询入口**(含手持光源)：固定光源与 <paramref name="extraLights"/>(如场上正持光源的角色，位置随帧变)
    /// 并入后取最强贡献。手持光源不常驻本集合，由消费方每帧收集持光角色的 <see cref="PlacedLight"/> 传入。
    /// </summary>
    public float StrongestAt(float x, float y, IEnumerable<PlacedLight> extraLights)
    {
        float fixedBest = LightSource.StrongestContribution(x, y, _lights);
        if (extraLights == null)
        {
            return fixedBest;
        }
        float extraBest = LightSource.StrongestContribution(x, y, extraLights);
        return fixedBest >= extraBest ? fixedBest : extraBest;
    }
}
