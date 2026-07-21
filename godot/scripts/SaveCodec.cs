using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。只管「状态树 ↔ JSON 文本」，
// 「文本 ↔ 磁盘」是 SaveManager（Godot 层，user://）的事。这样版本闸门与往返一致性全都能脱引擎单测。

/// <summary>一次读档尝试的结果。失败时 <see cref="Error"/> 是**给玩家看的中文**，不是异常堆栈。</summary>
public readonly record struct SaveLoadResult(SaveData? Data, string? Error)
{
    public bool Ok => Data is not null;

    public static SaveLoadResult Success(SaveData data) => new(data, null);
    public static SaveLoadResult Fail(string error) => new(null, error);
}

/// <summary>
/// 存档编解码：状态树 ⇄ JSON 文本，外加**版本闸门**。
///
/// <para>
/// <b>版本策略：版本号 + 明确拒绝，不做迁移链。</b>
/// </para>
/// <para>
/// 理由：这游戏还在剧烈演化（一天之内加十几个系统是常态），而<b>现在没有任何真实玩家</b>。
/// 为一份不存在的旧存档写迁移代码，是在为不存在的用户付永久的维护税——每加一个字段就要多写一条迁移，
/// 而且那些迁移路径**永远不会被真正走过**（没人有旧存档），也就永远测不到，
/// 最后攒成一堆没人敢删、也没人敢信的死代码。
/// </para>
/// <para>
/// 更要紧的是：<b>半兼容读档比读不了更糟</b>。旧存档缺了"手指切除"那个字段，读回来山姆的手指就长回来了——
/// 游戏不会崩，玩家也不会收到任何提示，他只是拿到了一个**被悄悄改写过的世界**。
/// 明确说一句"这份存档是旧版本的，读不了"，诚实得多。
/// </para>
/// <para>
/// ⇒ <b>规则变更时把 <see cref="CurrentVersion"/> +1</b>，旧存档当场作废。等游戏定型、真有玩家了，再谈迁移。
/// </para>
/// </summary>
public static class SaveCodec
{
    /// <summary>
    /// 当前存档格式版本。<b>任何会让旧存档读出错误世界的改动都要 +1</b>：
    /// 新增/删除状态字段、改部位表、改枚举取值、改单位（如白银从整数改成分制）。
    /// 纯文案改动（物品描述）不用动——那些是照抄进存档的，不影响状态语义。
    /// </summary>
    // v2（批次21·impl-bedrest）：新增卧床养病——PawnSave 加卧床令+休养流水账、CampSave 加床位占用+床序号。
    // 旧档没有这些字段，读回来会是"没人躺床、当天没养过病"的脏态 ⇒ 按上面的规矩直接作废旧档，不做迁移。
    //
    // v3（T46·impl-iron）：材料「废金属」+「金属锭」合并为「铁」（用户拍板）。
    // ⚠️ 这是**第一条真的写了迁移的版本**，破了上面"只拒绝不迁移"的惯例，理由是这次的旧档**能被无损修好**：
    // 老档里的废金属/金属锭是**纯数量**（ItemSave.MaterialQuantity），合并规则是确定的算术
    // （铁 = 废×1 + 锭×<see cref="SaveMigration.IngotToIronRatio"/>），不存在"猜一个不知道的字段"那种半兼容风险。
    // ⇒ 这里恰恰**不是**上面警告的"山姆的手指长回来了"，而是"两堆金属并成一堆，一克不少"。
    // 迁移入口：<see cref="SaveMigration.Migrate"/>（在 Deserialize 的版本闸门之前跑）。
    // v5：疾病/成药删除；休养账本从相位计数改成游戏分钟。旧占比可按同值无损搬成分钟权重；
    // 若旧档真含 Disease，则无合法替代，迁移器明确拒读。
    public const int CurrentVersion = 5;

    private static readonly JsonSerializerOptions Options = new()
    {
        // 人类可读 > 体积。存档要能直接打开看、能手改来复现 bug——这个阶段可调试性压倒一切。
        WriteIndented = true,
        // 枚举存**字符串**而非数字：数字会在枚举中间插一个新值时**静默错位**
        // （DayPhase 里加一个内部流程节点，所有旧存档的"黄昏"就会错位，还不报错）。
        Converters = { new JsonStringEnumConverter() },
        // 未知字段静默忽略（System.Text.Json 默认行为，此处显式写出以表明是有意的）：
        // 手改存档时留个注释字段不会把整个档读废。
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>状态树 → JSON 文本。写版本号是本方法的责任，调用方不用管。</summary>
    public static string Serialize(SaveData data)
    {
        data.Version = CurrentVersion;
        return JsonSerializer.Serialize(data, Options);
    }

    /// <summary>
    /// JSON 文本 → 状态树，**过版本闸门**。
    /// 三种失败都返回人话：空档 / 版本不符 / JSON 损坏。绝不抛给上层，也绝不返回半个世界。
    /// </summary>
    public static SaveLoadResult Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SaveLoadResult.Fail("存档是空的。");
        }

        // [T46] 版本闸门**之前**先试着升级：能被无损、确定地修好的旧档要迁移，迁不动的才拒读。
        // 迁移失败一律返回人话错误（绝不返回半个世界）——见 SaveMigration 的类注释。
        int version = PeekVersion(json);
        if (version != CurrentVersion && SaveMigration.CanMigrate(version))
        {
            if (!SaveMigration.TryMigrate(json, version, out string? upgraded, out string? migrationError))
            {
                return SaveLoadResult.Fail(migrationError!);
            }
            json = upgraded!;
        }

        SaveData? data;
        try
        {
            data = JsonSerializer.Deserialize<SaveData>(json, Options);
        }
        catch (JsonException e)
        {
            return SaveLoadResult.Fail($"存档已损坏，读不出来（{e.Message}）。");
        }

        if (data is null)
        {
            return SaveLoadResult.Fail("存档已损坏，读不出来。");
        }

        if (data.Version != CurrentVersion)
        {
            // 明确拒绝。不猜、不补默认值、不"尽力而为"——半个世界比没有世界更坏。
            return SaveLoadResult.Fail(
                data.Version < CurrentVersion
                    ? $"这份存档是旧版本的（v{data.Version}），当前版本 v{CurrentVersion} 读不了它。游戏还在开发中，规则改动会让旧存档失效。"
                    : $"这份存档来自更新的版本（v{data.Version}），当前版本 v{CurrentVersion} 读不了它。");
        }

        return SaveLoadResult.Success(data);
    }

    /// <summary>
    /// 只把摘要抠出来（存档列表用）：列 8 个存档不该反序列化 8 棵完整世界树。
    /// <b>不过版本闸门</b>——旧存档也要能在列表里显示出来（并标明"版本过旧"），而不是凭空消失。
    /// </summary>
    public static SaveMeta? PeekMeta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (!doc.RootElement.TryGetProperty(nameof(SaveData.Meta), out JsonElement meta))
            {
                return null;
            }
            return meta.Deserialize<SaveMeta>(Options);
        }
        catch (JsonException)
        {
            return null;   // 损坏的存档在列表里显示为"损坏"，不该让整个列表打不开
        }
    }

    /// <summary>只读版本号（不解析其余部分）。读不出返回 -1。</summary>
    public static int PeekVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return -1;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return doc.RootElement.TryGetProperty(nameof(SaveData.Version), out JsonElement v)
                && v.TryGetInt32(out int n) ? n : -1;
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    /// <summary>
    /// 这份存档能不能读（列表上给"读取"按钮置灰用）。
    /// <para>
    /// 🔴 <b>必须把"可迁移的旧档"也算成能读</b>：<c>SaveManager.GetSlotInfo</c> 拿它去决定读取按钮灰不灰
    /// —— 写了迁移却在这里对 v2 返回 <c>false</c>，按钮就永远是灰的，<b>迁移代码一行都跑不到</b>（静默失效）。
    /// </para>
    /// </summary>
    public static bool IsCompatible(string? json) => SaveMigration.CanMigrate(PeekVersion(json));
}
