using System;
using System.Globalization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Materials.cs / MerchantTrade.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 白银货币的精度约定（用户拍板 [SPEC-B14-补6] 原话："白银精确到小数点后两位"，0.01 银为最小单位）。
// 内核以「分」（int，1 白银 = 100 分）承载——避免浮点漂移的标准货币做法；对外一律两位小数显示。
// 全仓凡白银量（库存余额 InventoryStore/商人价 MerchantShelf·MerchantBuyList/战利品投放 ExplorationCache/
// 交易结算 MerchantTrade）统一以「分」计。authored 数据仍用**整银字面量**书写、经 <see cref="FromWhole"/>
// 转分入表，既保可读又杜绝单位混淆致 100× 经济错位。
/// <summary>白银货币的分制内核与显示（1 白银 = 100 分，最小单位 0.01 银）。</summary>
public static class Silver
{
    /// <summary>1 白银 = 100 分（最小刻度 0.01 银）。</summary>
    public const int CentsPerSilver = 100;

    /// <summary>整银 → 分（authored 商人价/战利品投放用；负值 clamp 到 0）。</summary>
    public static int FromWhole(int wholeSilver) => Math.Max(0, wholeSilver) * CentsPerSilver;

    /// <summary>分 → 两位小数字符串（如 180 → "1.80"）。纯显示，不回写内核值。</summary>
    public static string Format(int cents) =>
        (cents / (double)CentsPerSilver).ToString("0.00", CultureInfo.InvariantCulture);
}
