using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>
/// 「座位家具」的纯逻辑地基（无 Godot 依赖，供单测先红后绿）：一组营地座位点的坐标 + 占用登记，
/// 以及「就近取一个空座、标记占用」「读完释放」的簿记。座位的实体建造/寻路（非实心可站点、
/// 走到坐下）在 Godot 消费层（<c>CampMain</c>）适配 Vector2 调用本类。
///
/// 读书等指派活动流程：读者 <see cref="ClaimNearest"/> 认领离自己最近的空座 → 寻路走到该座坐下读 →
/// 读完 <see cref="Release"/> 释放。座位不足时 Claim 返回 -1，调用方按「无座」惩罚处理。座位类型随登记保存，
/// 供沙发等升级座位把效果投影到消费层。
/// </summary>
public enum SeatKind
{
    /// <summary>板凳/木椅/营地预置座位：有座但不附加升级效果。</summary>
    Standard,

    /// <summary>沙发：坐着读书吃沙发专属乘子。</summary>
    Sofa,
}

public sealed class SeatRegistry
{
    private readonly struct Seat
    {
        public readonly double X;
        public readonly double Y;
        public readonly bool Occupied;
        public readonly bool Active;
        public readonly SeatKind Kind;
        public Seat(double x, double y, bool occupied, SeatKind kind, bool active = true)
        {
            X = x;
            Y = y;
            Occupied = occupied;
            Kind = kind;
            Active = active;
        }
        public Seat WithOccupied(bool occupied) => new(X, Y, occupied, Kind, Active);
        public Seat Deactivate() => new(X, Y, occupied: false, Kind, active: false);
    }

    private readonly List<Seat> _seats = new();

    /// <summary>登记一个座位点（cartesian 坐标），返回其稳定下标（供 Claim/Release/PositionOf 引用）。</summary>
    public int Add(double x, double y, SeatKind kind = SeatKind.Standard)
    {
        _seats.Add(new Seat(x, y, occupied: false, kind: kind));
        return _seats.Count - 1;
    }

    /// <summary>座位总数。</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (Seat s in _seats)
            {
                if (s.Active)
                    n++;
            }
            return n;
        }
    }

    /// <summary>当前空闲座位数。</summary>
    public int FreeCount
    {
        get
        {
            int n = 0;
            foreach (Seat s in _seats)
            {
                if (s.Active && !s.Occupied)
                {
                    n++;
                }
            }
            return n;
        }
    }

    /// <summary>下标 <paramref name="index"/> 座位是否被占用（越界返回 true，视作不可用）。</summary>
    public bool IsOccupied(int index)
        => index < 0 || index >= _seats.Count || !_seats[index].Active || _seats[index].Occupied;

    /// <summary>读取座位类型；越界或已移除的座位返回普通座位。</summary>
    public SeatKind KindOf(int index)
        => index < 0 || index >= _seats.Count || !_seats[index].Active
            ? SeatKind.Standard
            : _seats[index].Kind;

    /// <summary>取下标 <paramref name="index"/> 座位的 cartesian 坐标（越界返回 (0,0)）。</summary>
    public (double x, double y) PositionOf(int index)
    {
        if (index < 0 || index >= _seats.Count || !_seats[index].Active)
        {
            return (0, 0);
        }
        Seat s = _seats[index];
        return (s.X, s.Y);
    }

    /// <summary>
    /// 就近认领：在所有**空闲**座位里挑离 (fromX,fromY) 最近者，标记占用并返回其下标；无空座返回 -1。
    /// 距离比平方即可（单调），省开方。
    /// </summary>
    public int ClaimNearest(double fromX, double fromY)
    {
        int best = -1;
        double bestSq = double.PositiveInfinity;
        for (int i = 0; i < _seats.Count; i++)
        {
            Seat s = _seats[i];
            if (!s.Active || s.Occupied)
            {
                continue;
            }
            double dx = s.X - fromX, dy = s.Y - fromY;
            double sq = dx * dx + dy * dy;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }
        if (best >= 0)
        {
            _seats[best] = _seats[best].WithOccupied(true);
        }
        return best;
    }

    /// <summary>释放下标 <paramref name="index"/> 座位（越界忽略；重复释放幂等）。</summary>
    public void Release(int index)
    {
        if (index >= 0 && index < _seats.Count)
        {
            _seats[index] = _seats[index].WithOccupied(false);
        }
    }

    /// <summary>移除座位家具（拆沙发等）：稳定下标失效，既有占用一并清除。</summary>
    public void Remove(int index)
    {
        if (index >= 0 && index < _seats.Count)
        {
            _seats[index] = _seats[index].Deactivate();
        }
    }
}
