using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.WikiExtract;

// ═══════════════════════════════════════════════════════════════════════════
// wiki 数据抽取器 —— 把 C# 唯一事实源导出成 docs/wiki/data/*.json 供本地 wiki 网页编辑。
//
// 单向：**代码 → JSON**。只读代码、绝不改代码。用户在网页上改 JSON，之后由 agent 把改动落回 C#。
// 可重跑：物品条目走**反射/目录遍历**取（WeaponTable 新加一把枪、Materials 新加一种料，重跑即出现），
//        列（column）是手写的——因为列名要给用户看，必须是人话中文，不能是 C# 属性名。
//
// 跑法：export DOTNET_ROOT=$HOME/.dotnet && ~/.dotnet/dotnet run --project tools/WikiExtract
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>一列的元数据。<paramref name="Internal"/>=true ⇒ 网页里收进置灰「勿改」列（内部 id/代码位置）。</summary>
/// <param name="Type">text | longtext | number | bool | chip —— 决定网页的编辑控件与配色。</param>
/// <param name="Chips">chip 类型的取值→配色名映射（网页靠配色区分，不另立状态文字列）。</param>
internal sealed record Col(
    string Key,
    string Label,
    string Type = "text",
    bool Primary = false,
    bool Internal = false,
    bool ReadOnly = false,
    string? Hint = null,

    /// <summary>
    /// **这一列的改动一律要报「待同步进代码」，哪怕代码里现在是空的。**
    /// <para>
    /// 默认的文本漂移判据是"种子非空"——那是为了不让角色的 authored 剧情文本（C# 里根本没有那些句子）
    /// 每次重跑都刷一屏。但「简介」不一样：它**按定义就该有个代码字段**，
    /// 现在为空只说明**那个字段还没建**（配方 / 家具 / 角色数值 / 书籍的短简介就是这种缺口）。
    /// 用户往里写了字，恰恰是在说"这里该有个字段" —— 这必须被看见，不能当 authored 文本吞掉。
    /// </para>
    /// </summary>
    bool AlwaysSync = false,

    /// <summary>
    /// **用户的设计笔记，不进游戏、也不同步进代码。**
    /// <para>
    /// 「备注」列专用。代码里没有对应字段，**也不该有**——它不是游戏数据，是**写给 agent 看的需求**
    /// （比如「这把锤子应该能砸开保险箱」）。所以它**不进漂移报告**（没有代码位置可以同步），
    /// 但会进抽取器结尾那节 <b>📝 用户备注（待处理）</b>——见 <see cref="Program.ReportNotes"/>。
    /// </para>
    /// </summary>
    bool UserNote = false,

    /// <summary>
    /// **这一列是 config json 的镜像——wiki↔config 双向联动的字段级 join。**
    /// <para>
    /// 非空 ⇒ 这一列的值就是 <c>godot/data/config/&lt;表&gt;.json</c> 里对应条目的
    /// <c>ConfigKey</c> 字段（如 wiki 列 <c>damageMin</c> ⇒ config 字段 <c>DamageMin</c>）。
    /// 网页改了它 → wiki-serve 的 PUT 处理器按 <c>_configId</c>+这个键把值投影写回 config json；
    /// Python/别处改了 config → wiki-serve 的 GET/启动重算把值拉回 wiki 展示 json。
    /// 只标**数值/gameplay 字段**（数字、bool 等值枚举）；简介/flavor/备注**不标**（那些仍 agent 手动落回代码）。
    /// 值默认与 config **恒等可复制**；带中文显示变换的枚举（如伤害类型 锐/钝 ↔ Sharp/Blunt）另配 <see cref="ValueMap"/>。
    /// </para>
    /// </summary>
    string? ConfigKey = null,

    /// <summary>
    /// **列级 configFile 覆盖**（一表多 config 源用）。非空 ⇒ 这一列写回的是这个 config 文件，而非表级 <see cref="Category.ConfigFile"/>。
    /// <para>例：弹药表的子弹数值在 <c>ammo.json</c>（表级），箭矢乘子却在 <c>archery.json</c>（这一列级覆盖）。</para>
    /// </summary>
    string? ConfigFile = null,

    /// <summary>
    /// **id-字典在 config 文件里的嵌套路径**（点分）。非空 ⇒ 条目字典不在顶层，而在 <c>cfg[ConfigRoot]</c> 下。
    /// <para>例：<c>archery.json</c> 的箭在 <c>Arrows</c> 子对象里 ⇒ ConfigRoot="Arrows"；顶层 id-字典（weapons/armor）留空。</para>
    /// </summary>
    string? ConfigRoot = null,

    /// <summary>
    /// **config 条目本身就是标量**（<c>Dict&lt;id → 数值&gt;</c>，没有字段层）。true ⇒ 这一列 = <c>cfg[_configId]</c> 那个数，
    /// <see cref="ConfigKey"/> 不参与。例：<c>materials.json</c> 是 <c>{ "stone": 3, ... }</c>，重量列即标量条目。
    /// </summary>
    bool ConfigScalar = false,

    /// <summary>
    /// **枚举显示变换**（wiki 中文值 ↔ config 英文枚举）。键=wiki 显示值，值=config 存储值，如 <c>{"锐":"Sharp","钝":"Blunt"}</c>。
    /// wiki→config 正向查、config→wiki 反向查；wiki-serve 据此双向转换，不做恒等复制。
    /// </summary>
    IReadOnlyDictionary<string, string>? ValueMap = null,

    /// <summary>
    /// **百分比数值变换**（wiki 显示 *100、config 存分数）。true ⇒ 这一列 config 存 0.1，wiki 展示成 10（unit=%）。
    /// wiki-serve config→wiki *100、wiki→config /100（圆整抹浮点尾）。**仅当 wiki 值本身是"数字10"这种放大表示时才标**；
    /// <para>⚠️ 别跟 <c>type:"percent"</c> 列混淆——那类列 wiki 存的仍是分数(0.35)，*100 只发生在前端渲染，config 恒等，**不标本项**。</para>
    /// </summary>
    bool PercentTransform = false,

    /// <summary>
    /// **嵌套字典条目**（config 字段是 <c>Dict&lt;料key → 数量&gt;</c>，wiki 单元格是「中文名*量、中文名*量」人话串）。
    /// true ⇒ 这一列 = <c>cfg[_configId][ConfigKey]</c> 那个嵌套字典（如 recipes.json 的 <c>MaterialCosts</c>、furniture.json 的 <c>cost</c>）。
    /// wiki-serve 用 <see cref="DictNameMap"/> 在「串」与「字典」之间双向转换：config→wiki 按字典渲染成串、wiki→config 解析串成字典。
    /// </summary>
    bool ConfigDict = false,

    /// <summary>
    /// **中文料名 ↔ config key 映射**（<see cref="ConfigDict"/> 列专用）。键=wiki 显示的中文料名，值=config 字典里的英文 key。
    /// wiki-serve 据此把「木料*2」解析成 <c>{"wood":2}</c>（正向）、把 <c>{"wood":2}</c> 渲染成「木料*2」（反向查）。
    /// </summary>
    IReadOnlyDictionary<string, string>? DictNameMap = null,

    /// <summary>
    /// **页内分块的段名**（波2 合页数据契约）。非空 ⇒ 这一列只属于合页里的某个原子表分块
    /// （如「食物与烹饪」页里，热量点列属于「食材」段、工时列属于「烹饪」段）。前端据此把合页拆成分块表头。
    /// <para>两个源表都有的公共列（title/body/_id/简介/备注/同步状态…）留空 = 该列在所有分块通用。</para>
    /// </summary>
    string? Group = null);

internal sealed record Category(
    string Id,
    string Label,
    string Source,
    string Note,
    IReadOnlyList<Col> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows,
    /// <summary>
    /// 非空 ⇒ 本表的行是 <c>godot/data/config/&lt;这个文件名&gt;</c> 里条目的镜像（wiki↔config 双向联动）。
    /// 与列的 <see cref="Col.ConfigKey"/> + 行的 <c>_configId</c> 一起，让 wiki-serve 无需硬编码即可自描述地双向同步。
    /// </summary>
    string? ConfigFile = null);

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文不转义成 \uXXXX
    };

    /// <summary>
    /// **采来的草药原料**（用户拍板：归「材料」分区，不进医疗分区）—— 它们是野外采集的原料，
    /// 要加工才成药；成品（草药膏 / 蒲公英茶 / 玫瑰果茶 / 草药绷带）才留在医疗分区。
    /// <para>
    /// 这只是 <b>wiki 的分区归类</b>：C# 里它们仍挂在 <c>MaterialCategory.Medical</c> 下（本工具不改业务代码）。
    /// 代码侧要不要跟着改类别由 impl-medicine 定；真改了这里也不用动 —— 它按 key 认人，不看 C# 的类别。
    /// 第四种草药（用户只点名了三种）等 impl-medicine 的结论，到时往这个集合里加一行即可。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> RawHerbKeys = new() { "dandelion", "rosehip", "laojunxu" };

    /// <summary>「简介」列的提示语（全分区统一）。</summary>
    private const string PlayerFacingHint =
        "**玩家在游戏里看到的描述**。改了它，agent 会把新文案同步进代码。"
        + "（有几个分区代码里还没有描述字段——你照样可以写，写了就是在告诉 agent「这里该有一个」。）";

    /// <summary>「备注」列的提示语（全分区统一）。</summary>
    private const string NoteHint =
        "**你的设计笔记，不进游戏**——想到什么特殊效果就写在这（比如「这把锤子应该能砸开保险箱」）。"
        + "它不会被同步成游戏文案，但 agent 每次跑抽取器都会看到你新写的备注。";

    /// <summary>
    /// 一行的图标：**相对 <c>godot/assets/items/</c> 的路径（不带扩展名）**，如 <c>weapons/short_sword</c>。
    /// 网页据此取 <c>assets/items/&lt;这个&gt;.png</c>，缺图画占位框。
    /// <para>
    /// 图标映射的**单一事实源是 <see cref="ItemIcons"/></c>**（scout-assets 的 <c>godot/scripts/ItemIcons.cs</c>），
    /// 这里只查表、不自造 slug —— 它给「短剑」定的是 <c>short_sword</c>，我若按成员名派生会得到 <c>shortsword</c>，
    /// 两套命名就会各画各的图。查表的键各分区不同（武器/护甲/狗装＝中文名，材料/光源＝英文 key，书＝书 id，
    /// 家具＝配方产物 key），故按 <c>_id</c> → 主键名 → 产物 key 依次试。
    /// </para>
    /// <para>
    /// 查不到（角色、武器改装等不属于"物品"的分区）才退回按分区派生一个蛇形名，让它至少有个稳定的图标位。
    /// </para>
    /// </summary>
    private static string IconPathOf(string categoryId, Dictionary<string, object?> row)
    {
        foreach (string key in new[] { "_id", "name", "title", "output" })
        {
            if (row.TryGetValue(key, out object? v) && v is string s && s.Length > 0
                && ItemIcons.Find(s) is { } def)
            {
                return $"{def.Category}/{def.Slug}";
            }
        }
        string slug = SnakeCase(row.TryGetValue("_id", out object? id) ? id as string ?? "" : "");
        return slug.Length == 0 ? "" : $"{categoryId}/{slug}";
    }

    /// <summary>PascalCase → 小写蛇形；含中文则给不出文件名，返回 ""。仅用于 <see cref="ItemIcons"/> 查不到时的兜底。</summary>
    private static string SnakeCase(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (raw.Any(c => c > 127)) return "";       // 中文键：没法当文件名，不给图标位

        var sb = new StringBuilder();
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(raw[i - 1]) || (i + 1 < raw.Length && char.IsLower(raw[i + 1])))) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Trim('_');
    }

    private static int Main(string[] args)
    {
        string repoRoot = FindRepoRoot();
        string dataDir = Path.Combine(repoRoot, "docs", "wiki", "data");
        Directory.CreateDirectory(dataDir);

        // `--reset` = 丢弃表里的全部改动，从代码强制重建（危险，只在表被改乱了想推倒重来时用）。
        // 默认走 TableMerge：**表赢代码** —— 用户在网页上改的/新增的/删除的，重跑绝不会被静默抹掉。
        // （`--reset-characters` 是 impl-wiki-chars 留的旧名，保留兼容。）
        bool reset = args.Contains("--reset") || args.Contains("--reset-characters");

        // `--ack-notes` = 把当前全部「备注」记为**已处理**（agent 处理完一批之后跑）。
        // 用户之后再改那条备注 ⇒ 内容哈希变了 ⇒ **下次抽取它会重新跳出来**。
        bool ackNotes = args.Contains("--ack-notes");

        var seeds = new List<Category>
        {
            Weapons(),
            Armor(),
            DogGear(),
            Ammo(),
            Materials(),
            Medical(),
            Recipes(),
            Butchery(),
            Lights(),
            // [波2 合页] 书籍 + 日记 → 一页（沿用 id "books"；旧 diaries.json 成孤儿，重跑后删）。
            // 页内两分块：书籍（要角色整夜读、有工时/效果）与 日记（道具，无工时，只讲故事）。
            MergePages("books", "书籍与日记",
                "读书是角色的时间成本，日记是白捡的剧情——两者放一页，但**分块泾渭分明**。"
                + "「书籍」段那些要角色整夜坐着读、能解锁配方/给被动的才是书；"
                + "「日记」段是道具，玩家点开即读、不花任何角色一分钟、什么也不解锁。",
                Books(), "书籍", Diaries(), "日记"),
            WeaponMods(),
            Furniture(),
            // [波2 合页] 食物与食材 + 烹饪规则 → 一页（沿用 id "food"；旧 cooking.json 成孤儿，重跑后删）。
            // 页内两分块：食材（能下锅的东西、热量点）与 烹饪（一份饭要多少热量、工时等可调数）。
            MergePages("food", "食物与烹饪",
                "从食材到一顿饭：**食材**段是能下锅的东西和它们的热量点，**烹饪**段是把食材变成饭的几个可调数。"
                + "⚠️ 这些数字**游戏里一个都不显示**，玩家得自己试出「几只老鼠够一顿饭」——刻意设计的乐趣。",
                Food(), "食材", CookingRules(), "烹饪"),
            // [波2 新表] 种植与陷阱（菜园 / 圈套 / 捕鸟陷阱）。
            Farming(),
            GlobalRules(),
            Characters.Roster(),
            Characters.Stats(),
            // [T57] 调查点路线（网状解锁图）。种子来自 godot/data/world_graph.json —— 那份数据本身就是事实源，
            // 游戏运行时直接读它 ⇒ 用户在这张表上重排路线，agent 同步回那份 JSON 即可，**不用改任何 C# 代码**。
            WorldGraphTable.Build(repoRoot),
            // [结局] 全作四条结局骨架的**内部设计参考表**（⚠️含最大剧透·只进内部 wiki·CG 摘要逐屏抄 EndingCg 的 draft 文本）。
            EndingTable(),
        };

        // ── 「简介」和「备注」统一在这里补，16 个分区一个都不落（用户要求"每一样都应该有"）──
        //
        // 🔴 **必须在 TableMerge 之前补** —— 合并只认 `seeded.Columns` 里声明过的列，
        //    在它之后才加列，等于合并时根本不知道有「备注」这一列 ⇒ **用户写的备注会被直接丢掉**。
        //    （第一版就是这么写的，一测就丢。这正是"静默吞掉用户输入"那个病，差点又犯一次。）
        //
        // 🔴 两列的性质**完全不同**，别做成一样的：
        //   · **简介** = 玩家在游戏里看到的描述 ⇒ 有代码字段（或**应该有**）⇒ 改了要报「待同步进代码」。
        //     多数分区代码里已有描述字段（Weapon/ArmorTable/Materials/LightSource… 的 Description），
        //     那些分区**复用既有的「说明」列改名为「简介」**，不另造一列。
        //     ⚠️ **四个分区代码里根本没有描述字段**（配方 / 家具建造 / 角色数值 / 书籍的短简介）——
        //     那是**真缺口**。这里照样给它们一列（`AlwaysSync`），空着也要报：用户往里写字，
        //     恰恰是在说"这里该有个字段"，不能当 authored 文本吞掉。
        //   · **备注** = 用户写给 agent 看的设计笔记 ⇒ **代码里没有、也不该有** ⇒ 不报"待同步"
        //     （没有代码位置可以同步，报了是撒谎），但**必须被看见** ⇒ 走 UserNotes 那节专门的报告。
        seeds = seeds.Select(c =>
        {
            var cols = c.Columns.ToList();

            // 已有的描述列 → 改名为「简介」（别造重复列）；没有的 → 新建一个空的
            int descAt = cols.FindIndex(x => x.Key == "description");
            if (descAt >= 0)
            {
                cols[descAt] = cols[descAt] with { Label = "简介", AlwaysSync = true, Hint = PlayerFacingHint };
            }
            else if (!cols.Any(x => x.Key == "description"))
            {
                // 代码里没有描述字段的分区（配方/家具/角色数值/书籍…）：仍然给一列，且空着也要报
                int at = Math.Max(0, cols.FindIndex(x => x.Internal));
                if (at <= 0) at = cols.Count;
                cols.Insert(at, new Col("description", "简介", "longtext", AlwaysSync: true, Hint: PlayerFacingHint));
                foreach (Dictionary<string, object?> row in c.Rows)
                {
                    row.TryAdd("description", "");
                }
            }

            // 备注：全分区统一一列（角色分区本来就有一列叫「备注」，键是 notes —— 那是它自己的，
            // 这里的 note 是给 agent 看的设计笔记，两者不冲突）
            if (!cols.Any(x => x.Key == UserNotes.Key))
            {
                int at = Math.Max(0, cols.FindIndex(x => x.Internal));
                if (at <= 0) at = cols.Count;
                cols.Insert(at, new Col(UserNotes.Key, "备注", "note", UserNote: true, Hint: NoteHint));
                foreach (Dictionary<string, object?> row in c.Rows)
                {
                    row.TryAdd(UserNotes.Key, "");
                }
            }

            return c with { Columns = cols };
        }).ToList();


        List<Category> categories = reset
            ? seeds.Select(c => c with { Columns = c.Columns.Append(TableMerge.SyncColumn()).ToList() }).ToList()
            : seeds.Select(c => TableMerge.WithExisting(c, dataDir)).ToList();

        // 🔴 DPS 必须按**表里的当前值**重算，不能按代码的种子值算。
        // 因为表赢代码：用户可能已经把短剑的伤害从 1~15 改成 2~9（还没同步进 C#）。若 DPS 仍按代码的
        // 1~15 算，表上就会出现「伤害 2~9，每秒伤害却是按 1~15 算的」——**wiki 当场就在骗人**，
        // 而他正是拿这个数字在调平衡。故：合并之后，用行里的值重建一把 Weapon，再喂给引擎的 WeaponDps。
        // （算的人始终是引擎，这里只是把"用哪把武器的数"换成"表里那把"。）
        foreach (Category c in categories.Where(x => x.Id == "weapons"))
        {
            foreach (Dictionary<string, object?> row in c.Rows)
            {
                Weapon asShown = WeaponFromRow(row);
                row["dps"] = WeaponDps.Single(asShown);
                row["dualDps"] = WeaponDps.Dual(asShown);
                row["dpsVsLeather"] = WeaponDps.AgainstLeatherArmor(asShown);
            }
        }

        // 图标位统一在这里补：每条记录一个 _icon（相对 assets/items 的路径），各分区的 Rows 不用各写一遍。
        // **行自己声明了 _icon 就不覆盖**（含显式置空）：「角色数值」那 52 行是数字不是东西，本就不该有图标位——
        // 无条件覆盖会把 52 个永远不该存在的文件名塞进 icon-manifest.json，而那是给美术侧的「要画哪些图」契约。
        categories = categories.Select(c =>
        {
            foreach (Dictionary<string, object?> row in c.Rows)
            {
                if (!row.ContainsKey("_icon")) row["_icon"] = IconPathOf(c.Id, row);
            }
            var cols = c.Columns.ToList();
            if (!cols.Any(x => x.Key == "_icon"))
            {
                cols.Add(new Col("_icon", "图标", Internal: true, Hint: "godot/assets/items/<这个>.png（映射表在 godot/scripts/ItemIcons.cs）"));
            }
            return c with { Columns = cols };
        }).ToList();

        // 每个分区一个 JSON 文件（网页按 index.json 逐个加载；加分区＝多一个文件 + index 里多一行）。
        foreach (Category c in categories)
        {
            string path = Path.Combine(dataDir, c.Id + ".json");
            File.WriteAllText(path, Serialize(c), new UTF8Encoding(false));
            Console.WriteLine($"  {c.Id,-14} {c.Rows.Count,4} 条  → docs/wiki/data/{c.Id}.json");
        }

        // index.json：分区注册表。网页只读它就知道有哪些分区、各自的文件名。
        var index = new JsonObject
        {
            ["generatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["iconDir"] = "godot/assets/items",   // 图标约定：<iconDir>/<分区id>/<内部id>.png，缺图走占位
            // 🔴 DPS 的持握系数**由引擎带下来**，网页不许在 JS 里写死 0.70 / 1.15 之类的数。
            // 网页据此做「改一格、DPS 立刻跟着变」的实时预览；加载时它还会拿自己算的结果
            // 跟引擎写进 JSON 的 dps 逐行对账，对不上就弹横幅——**偏差会被抓出来，而不是靠祈祷**。
            // multiselect 列的**候选项**由引擎带下来（网页不硬编码武器名单——加了新武器它自己就出现）。
            ["multiselectOptions"] = new JsonObject
            {
                ["fitsWeapons"] = new JsonArray(WeaponModCatalog.AllModdableWeapons()
                    .Select(w => (JsonNode)JsonValue.Create(w.Name)!).ToArray()),
            },
            ["dpsGripFactors"] = new JsonObject
            {
                ["oneHanded"] = Round(DualWield.GripSpeedFactor(GripMode.OneHanded)),
                ["twoHanded"] = Round(DualWield.GripSpeedFactor(GripMode.TwoHanded)),
                ["dualWield"] = Round(DualWield.GripSpeedFactor(GripMode.DualWield)),
            },
            ["categories"] = new JsonArray(categories.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id,
                ["label"] = c.Label,
                ["file"] = c.Id + ".json",
                ["count"] = c.Rows.Count,
                ["source"] = c.Source,
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(dataDir, "index.json"), index.ToJsonString(JsonOpts), new UTF8Encoding(false));

        // icon-manifest.json：**要画哪些图标**的清单（给美术侧 scout-assets 的契约）。
        // 每行就是一个该存在的文件：godot/assets/items/<分区>/<文件名>.png。中文键的条目拿不出文件名，不列。
        var manifest = new JsonObject();
        foreach (Category c in categories)
        {
            var names = new JsonArray(c.Rows
                .Select(r => r.TryGetValue("_icon", out object? ic) ? ic as string : null)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => (JsonNode)JsonValue.Create(s)!)
                .ToArray());
            if (names.Count > 0) manifest[c.Id] = names;
        }
        File.WriteAllText(Path.Combine(dataDir, "icon-manifest.json"),
            new JsonObject { ["note"] = "wiki 用到的图标清单：godot/assets/items/<这里的每一项>.png。映射的单一事实源是 godot/scripts/ItemIcons.cs；PNG 由 tools/icons/build_icons.sh 生成。（自动生成，勿手改）", ["icons"] = manifest }
                .ToJsonString(JsonOpts), new UTF8Encoding(false));

        // bundle.js：file:// 直接双击打开 index.html 时的降级数据源（fetch 读不了 file://，<script> 可以）。
        // 本地服务在每次保存后会重新生成它，故两条链路不会各说各话。
        WriteBundle(dataDir, categories, index);

        Console.WriteLine($"\n共 {categories.Count} 个分区 / {categories.Sum(c => c.Rows.Count)} 条记录 → {dataDir}");

        // 待同步报告：表和代码对不上的地方——**不擅自改任何一边**，只报出来交给 agent 决定。
        // 三类：数值漂移（表里改过数）、新增待同步（表里多一条）、删除待同步（表里删了但代码还有）。
        List<string> todo = TableMerge.Drift;
        if (todo.Count > 0)
        {
            Console.WriteLine($"\n⚠️ 有 {todo.Count} 处「表 ≠ 代码」——表里的内容已原样保留，需要同步进 C#：");
            foreach (string d in todo) Console.WriteLine(d);
        }
        else
        {
            Console.WriteLine("\n表与代码一致，没有待同步项。");
        }

        ReportNotes(categories, dataDir, ackNotes);
        return 0;
    }

    /// <summary>
    /// 「📝 用户备注（待处理）」—— **只报新写的 / 改过的**，已处理过的不再刷屏。
    ///
    /// <para><b>为什么必须单开一节</b>：备注不进漂移报告（它没有代码位置可以同步），
    /// 那它就有可能**永远躺在 JSON 里没人看**。今天刚栽过一次 —— 用户写在「效果」列里的设计意图
    /// 被静默吞了一整天。**这一节就是不让它重演。**</para>
    ///
    /// <para><b>为什么不能每次报全部</b>：第二次跑就是刷屏，刷屏就会被无视，被无视就等于没有这个机制。
    /// 所以按**内容哈希**认领（见 <see cref="UserNotes"/>）：处理过的安静下去，改过的再跳出来。</para>
    /// </summary>
    private static void ReportNotes(IReadOnlyList<Category> categories, string dataDir, bool ack)
    {
        if (ack)
        {
            int n = UserNotes.Acknowledge(categories, dataDir);
            Console.WriteLine($"\n📝 已把当前 {n} 条备注全部记为「已处理」。用户之后再改，它们会重新跳出来。");
            return;
        }

        IReadOnlyList<PendingNote> pending = UserNotes.Pending(categories, dataDir);
        if (pending.Count == 0) return;

        Console.WriteLine($"\n📝 用户备注（待处理 {pending.Count} 条）—— 这些是他写给你的设计笔记，**不是游戏文案**：");
        foreach (PendingNote p in pending)
        {
            Console.WriteLine($"  · {p.Category}·{p.RowName}（{p.Id}）：{p.Text}");
        }
        Console.WriteLine("  处理完之后跑 `~/.dotnet/dotnet run --project tools/WikiExtract -- --ack-notes` 认领它们（改过的以后还会再跳出来）。");
    }

    private static string Serialize(Category c)
    {
        var o = new JsonObject
        {
            ["id"] = c.Id,
            ["label"] = c.Label,
            ["source"] = c.Source,
            ["note"] = c.Note,
        };
        // config 联动锚点（wiki↔config 双向）：表级指明镜像哪个 config json；wiki-serve 读它做投影/重算。
        if (c.ConfigFile is not null) o["configFile"] = c.ConfigFile;
        o["columns"] = new JsonArray(c.Columns.Select(col =>
            {
                var jo = new JsonObject { ["key"] = col.Key, ["label"] = col.Label, ["type"] = col.Type };
                if (col.Primary) jo["primary"] = true;
                if (col.Internal) jo["internal"] = true;
                if (col.ReadOnly) jo["readonly"] = true;
                if (col.UserNote) jo["usernote"] = true;
                if (col.ConfigKey is not null) jo["configKey"] = col.ConfigKey;
                if (col.ConfigFile is not null) jo["configFile"] = col.ConfigFile;
                if (col.ConfigRoot is not null) jo["configRoot"] = col.ConfigRoot;
                if (col.ConfigScalar) jo["configScalar"] = true;
                if (col.PercentTransform) jo["percentTransform"] = true;
                if (col.ConfigDict) jo["configDict"] = true;
                if (col.Group is not null) jo["group"] = col.Group;
                if (col.ValueMap is not null)
                {
                    var vm = new JsonObject();
                    foreach ((string k, string v) in col.ValueMap) vm[k] = v;
                    jo["valueMap"] = vm;
                }
                if (col.DictNameMap is not null)
                {
                    var dm = new JsonObject();
                    foreach ((string k, string v) in col.DictNameMap) dm[k] = v;
                    jo["dictNameMap"] = dm;
                }
                if (col.Hint is not null) jo["hint"] = col.Hint;
                return (JsonNode)jo;
            }).ToArray());
        o["rows"] = new JsonArray(c.Rows.Select(r => (JsonNode)ToNode(r)).ToArray());
        return o.ToJsonString(JsonOpts);
    }

    private static JsonObject ToNode(Dictionary<string, object?> row)
    {
        var o = new JsonObject();
        foreach ((string k, object? v) in row)
        {
            o[k] = v switch
            {
                null => null,
                string s => JsonValue.Create(s),
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                double d => JsonValue.Create(Round(d)),
                float f => JsonValue.Create(Round(f)),
                _ => JsonValue.Create(v.ToString()),
            };
        }
        return o;
    }

    /// <summary>浮点噪音收口（0.30000000000000004 → 0.3）：数值表是给人看的。</summary>
    /// <summary>
    /// 写进 JSON 的数值精度 —— **单一真源**。<see cref="TableMerge"/> 比较「表 ≠ 代码」时必须用同一个它：
    /// 一旦"序列化四舍五入了、比较却没有"，float 字段就会永远报一个修不掉的假漂移
    /// （0.9f 提升成 double 是 0.899999976…，写进 JSON 是 0.9，读回来一比就差 2.4e-8）。
    /// </summary>
    internal static double Round(double d) => Math.Round(d, 4);

    /// <summary>
    /// **合页**（波2 数据契约）：把两张平表并进一张 Category —— 列取并集、每行带 <c>group</c>=原表名，
    /// 前端据此把一页拆成按 <c>group</c> 分块的表头。
    /// <para>
    /// 列的归属：<b>两个源表都有的列</b>（title/body/_id/_anchor…）与**内部列/简介**留空 = 公共列（所有分块通用）；
    /// **只属于某一个源表的列**打上该源的 <c>group</c>，前端只在那个分块里显示它。
    /// </para>
    /// <para>合页后的 <c>id</c> 沿用其中一张源表的（另一张的旧 json 成为孤儿，重跑后由调用侧删除 + 从 index 自动消失）。</para>
    /// </summary>
    private static Category MergePages(string id, string label, string note,
        Category a, string aGroup, Category b, string bGroup)
    {
        var aKeys = a.Columns.Select(c => c.Key).ToHashSet();
        var bKeys = b.Columns.Select(c => c.Key).ToHashSet();
        // 恒为公共（不打 group）的列：内部列、简介，以及后续 seed 变换统一补的备注/同步状态/图标。
        var alwaysCommon = new HashSet<string> { "description", "userNote", "sync", "_icon" };

        string? GroupFor(Col col, string src)
            => col.Internal || alwaysCommon.Contains(col.Key) || (aKeys.Contains(col.Key) && bKeys.Contains(col.Key))
                ? null : src;

        var cols = new List<Col>();
        var seen = new HashSet<string>();
        foreach (Col col in a.Columns) if (seen.Add(col.Key)) cols.Add(col with { Group = GroupFor(col, aGroup) });
        foreach (Col col in b.Columns) if (seen.Add(col.Key)) cols.Add(col with { Group = GroupFor(col, bGroup) });

        var rows = new List<Dictionary<string, object?>>();
        foreach (Dictionary<string, object?> r in a.Rows) { r["group"] = aGroup; rows.Add(r); }
        foreach (Dictionary<string, object?> r in b.Rows) { r["group"] = bGroup; rows.Add(r); }

        return new Category(id, label, a.Source + " · " + b.Source, note, cols, rows);
    }

    /// <summary>
    /// **种植与陷阱**（波2 新表）—— 菜园 / 圈套 / 捕鸟陷阱三套的可调数，一行一个数字（同「烹饪规则」形状）。
    /// 数值全来自代码常量（<c>CropPlotLogic</c> / <c>TrapLogic</c> / <c>BirdTrapLogic</c>），一个都不编。
    /// 两个分块：<b>种植</b>（菜园）与 <b>陷阱</b>（圈套 + 捕鸟陷阱）。
    /// </summary>
    private static Category Farming()
    {
        var cols = new List<Col>
        {
            new("label", "规则", Primary: true),
            // 「数值」列 = farming.json 单例设置对象的某个字段（configScalar：cfg[_configId] 即值）。
            // 只有真外置进 farming.json 的行带 _configId（7 个陷阱命中率）；种植 4 行仍是编译期 const、无 _configId ⇒ wiki-serve 自动跳过。
            new("value", "数值", "number", ConfigScalar: true),
            new("unit", "单位", "chip", Hint: "这个数字的单位。只是给人看的标签。"),
            new("note", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        // cfgKey 非空 ⇒ 这一行外置在 farming.json（configScalar：cfg[cfgKey] 即值）；pct=true ⇒ 显示值是 config 分数 *100（wiki-serve 双向 /100/*100）。
        void Add(string group, string id, string label, double value, string unit, string note, string anchor,
                 string? cfgKey = null, bool pct = false)
        {
            var row = new Dictionary<string, object?>
            {
                ["group"] = group, ["label"] = label, ["value"] = Round(value),
                ["unit"] = unit, ["note"] = note, ["_id"] = id, ["_anchor"] = anchor, ["_icon"] = "",
            };
            if (cfgKey is not null) row["_configId"] = cfgKey;
            if (pct) row["_configPercent"] = true;
            rows.Add(row);
        }

        // —— 种植（菜园）—— 编译期 const，不外置 ⇒ 只读展示（无 _configId）——
        const string cropSrc = "godot/scripts/Farming.cs :: ";
        Add("种植", "crop_max_plants", "一座菜园最多同时种", CropPlotLogic.MaxPlants, "颗",
            "满种要一颗颗下种；每颗收完这一格要重新下种（不自动续种）。",
            cropSrc + "CropPlotSpec.MaxPlants");
        Add("种植", "crop_grow_hours", "成熟所需", CropPlotLogic.GrowGameHours, "游戏小时",
            $"种下就不用管，一直走墙钟倒计时。= {Round(CropPlotLogic.MaturesInDayNightCycles)} 个昼夜（一昼夜 24 游戏小时）。",
            cropSrc + "CropPlotLogic.GrowGameHours");
        Add("种植", "crop_seed_cost", "下种消耗（种薯＝土豆）", CropPlotLogic.SeedCost, "个",
            "种薯就是土豆，不另造「种子」材料。收成的下限也够回本 ⇒ 永不亏种子。",
            cropSrc + "CropPlotLogic.SeedCost");
        // 展示的 9 = PlantWorkMinutes（(int)(PlantActionGameHours*60)）⇒ anchor 指它，别指 PlantActionGameHours（那是 0.15h）。
        Add("种植", "crop_plant_action_minutes", "种一颗的人力工时", CropPlotLogic.PlantWorkMinutes, "游戏分钟",
            $"下种是一次人力动作（走既有工时化）。满种 16 颗 = {Round((double)CropPlotLogic.PlantWorkMinutes * CropPlotLogic.MaxPlants)} 游戏分钟一次性人力。",
            cropSrc + "CropPlotLogic.PlantWorkMinutes");
        // —— 收获产量分布（编译期 const 三段分布，不外置 ⇒ 无 _configId ⇒ 只读展示）——
        // 每颗成熟收获掷一次点：50% 出 2 / 25% 出 3 / 25% 出 1（下限=种薯成本 ⇒ 永不亏种子）。
        Add("种植", "crop_yield_out2", "收获出 2 颗的几率", Round(CropPlotLogic.Out2Chance * 100), "%",
            "每颗成熟收获时掷一次点：这个几率出 2 颗（最常见的一档）。三档几率相加 = 100%。",
            cropSrc + "CropPlotLogic.Out2Chance");
        Add("种植", "crop_yield_out3", "收获出 3 颗的几率", Round(CropPlotLogic.Out3Chance * 100), "%",
            "走运的一档：这个几率出 3 颗。",
            cropSrc + "CropPlotLogic.Out3Chance");
        Add("种植", "crop_yield_out1", "收获出 1 颗的几率", Round(CropPlotLogic.Out1Chance * 100), "%",
            "下限档：这个几率只出 1 颗（= 种薯成本 1 ⇒ 回本，永不亏种子）。",
            cropSrc + "CropPlotLogic.Out1Chance");
        Add("种植", "crop_expected_yield", "每颗期望产量", Round(CropPlotLogic.ExpectedYieldPerPlant), "颗",
            $"按上面三档几率算的每颗平均收成（下种消耗 1 ⇒ 净赚约 {Round(CropPlotLogic.NetExpectedYieldPerPlant)} 颗/颗）。",
            cropSrc + "CropPlotLogic.ExpectedYieldPerPlant");

        // —— 陷阱（圈套：抓鼠/兔）—— 命中率外置 farming.json，接双向（分数 *100 ⇒ pct）——
        const string trapSrc = "godot/scripts/TrapLogic.cs :: TrapLogic";
        Add("陷阱", "snare_base_chance", "圈套 单个基础命中率", Round(TrapLogic.BaseChance * 100), "%",
            "一天掷两次点（白天 1 + 夜晚 1）。多摆几个会相互递减（见下），避免无限叠。",
            trapSrc + ".BaseChance", cfgKey: "SnareBaseChance", pct: true);
        Add("陷阱", "snare_chance_step", "圈套 每多一个的命中递减", Round(TrapLogic.ChanceStep * 100), "%",
            "第 n 个圈套的命中率 = 基础 − 递减*(n−1)，直到下限。防止密铺陷阱刷肉。",
            trapSrc + ".ChanceStep", cfgKey: "SnareChanceStep", pct: true);
        Add("陷阱", "snare_min_chance", "圈套 命中率下限", Round(TrapLogic.MinChance * 100), "%",
            "再多的圈套，单个命中率也不会低于这条线。",
            trapSrc + ".MinChance", cfgKey: "SnareMinChance", pct: true);
        Add("陷阱", "snare_rabbit_share", "圈套 抓到兔子的比例（其余为鼠）", Round(TrapLogic.RabbitShare * 100), "%",
            "命中后再掷一次「物种点」：这个比例抓到兔子，其余抓到老鼠。⚠️ 拟定待调（用户未指定）。",
            trapSrc + ".RabbitShare", cfgKey: "SnareRabbitShare", pct: true);

        // —— 陷阱（捕鸟陷阱：抓鸽）—— 命中率外置 farming.json，接双向 ——
        const string birdSrc = "godot/scripts/Farming.cs :: BirdTrapLogic";
        Add("陷阱", "birdtrap_base_chance", "捕鸟陷阱 单个基础命中率", Round(BirdTrapLogic.BaseChance * 100), "%",
            "与圈套共用一张尺子、一天两次；抓到的是鸽子。多摆同样相互递减。",
            birdSrc + ".BaseChance", cfgKey: "BirdTrapBaseChance", pct: true);
        Add("陷阱", "birdtrap_chance_step", "捕鸟陷阱 每多一个的命中递减", Round(BirdTrapLogic.ChanceStep * 100), "%",
            "第 n 个捕鸟陷阱的命中率 = 基础 − 递减*(n−1)，直到下限。",
            birdSrc + ".ChanceStep", cfgKey: "BirdTrapChanceStep", pct: true);
        Add("陷阱", "birdtrap_min_chance", "捕鸟陷阱 命中率下限", Round(BirdTrapLogic.MinChance * 100), "%",
            "再多的捕鸟陷阱，单个命中率也不会低于这条线。",
            birdSrc + ".MinChance", cfgKey: "BirdTrapMinChance", pct: true);

        return new Category("farming", "种植与陷阱",
            "godot/scripts/Farming.cs · godot/scripts/TrapLogic.cs",
            "自给自足的两条线：**种植**（菜园，种下慢慢长）和**陷阱**（圈套抓鼠兔、捕鸟陷阱抓鸽）。"
            + "陷阱一天掷两次点（白天 1 + 夜晚 1），多摆会相互递减命中率——不能靠密铺无限刷肉。"
            + "⚠️ 这些命中率数字**游戏里不显示**：玩家得自己试出「几个陷阱够养活一个人」。"
            + "（命中率 7 项已外置 farming.json、可在此双向编辑；种植 4 项是编译期常量，只读展示。）",
            cols, rows, ConfigFile: "farming.json");
    }

    private static void WriteBundle(string dataDir, List<Category> categories, JsonObject index)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// 自动生成，勿手改（`dotnet run --project tools/WikiExtract` 重跑，或本地服务保存时刷新）。");
        sb.AppendLine("// 用途：以 file:// 直接打开 index.html 时的降级数据源（浏览器不允许 fetch 本地文件）。");
        sb.AppendLine("window.WIKI_BUNDLE = {");
        sb.AppendLine($"  index: {index.ToJsonString(JsonOpts)},");
        sb.AppendLine("  data: {");
        foreach (Category c in categories)
        {
            sb.AppendLine($"    {JsonSerializer.Serialize(c.Id)}: {Serialize(c)},");
        }
        sb.AppendLine("  }");
        sb.AppendLine("};");
        File.WriteAllText(Path.Combine(dataDir, "bundle.js"), sb.ToString(), new UTF8Encoding(false));
    }

    // ─────────────────────────── 武器 ───────────────────────────

    private static Category Weapons()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            // 🔴 「种类」原本是只读的，但它**没有完整的代码依据**：引擎里没有"弓 vs 弩"这个区分，
            //    是我按武器名里有没有「弩」字猜的（新增一把弩若不叫"弩"就归错）。
            //    与其留着一个脆弱的猜测还不让人改，不如**放开让用户当事实源** —— 他填的一律以表为准。
            new("kind", "种类", "chip",
                Hint: "默认按武器属性推导（远程/弹药/伤害类型）。⚠️ 引擎里没有「弓 vs 弩」这个区分，是按名字里有没有「弩」字猜的——猜错了就在这里改，以你填的为准。"),
            new("damageType", "伤害类型", "chip", ConfigKey: "DamageType",
                ValueMap: new Dictionary<string, string> { ["锐"] = "Sharp", ["钝"] = "Blunt" }),
            new("damageMin", "伤害下限", "number", ConfigKey: "DamageMin"),
            new("damageMax", "伤害上限", "number", ConfigKey: "DamageMax"),
            new("penetration", "穿透力", "percent", Hint: "无视多少护甲。25% = 这一击当对方的甲只有 75%。", ConfigKey: "Penetration"),
            new("bleedRate", "流血速度", "mult", Hint: "这一击造成的伤口流血速度乘子（1 = 标准；越大越危险，与受害者体质倍率正交相乘）。", ConfigKey: "BleedRateMultiplier"),
            new("bleedOnHit", "命中流血几率", "percent", Hint: "每次命中额外开一道流血伤口的几率（0 = 不额外流血）。", ConfigKey: "BleedOnHitChance"),
            new("attackInterval", "攻击间隔(秒)", "number", Hint: "越小出手越快", ConfigKey: "AttackInterval"),
            new("burstCount", "连发数", "number", ConfigKey: "BurstCount"),
            new("burstInterval", "连发间隔(秒)", "number", ConfigKey: "BurstInterval"),
            new("pelletCount", "弹丸数", "number", Hint: "霰弹枪一发几颗；每颗独立选部位、独立判定", ConfigKey: "PelletCount"),
            new("ammo", "吃什么弹药", "chip", Hint: "空 = 不吃弹药"),
            new("ammoPerAttack", "每次攻击耗弹", "number", ReadOnly: true,
                Hint: "自动算的：完全由「连发数」决定（打几发就吃几发；不吃弹药的武器恒为 0）。要改它 ⇒ 改「连发数」。"),
            new("twoHanded", "强制双手", "bool", Hint: "武器本身是否必须双手持（Weapon.TwoHanded）；单手武器也可以双手握，那是运行时的握法，不是这一列", ConfigKey: "TwoHanded"),
            new("canDualWield", "可双持", "bool", ConfigKey: "CanDualWield"),
            new("maxRange", "最大射程(像素)", "number", ConfigKey: "MaxRange"),
            new("falloffStart", "距离衰减起点(像素)", "number", ConfigKey: "FalloffStart"),
            new("falloffFloor", "最远处伤害", "percent", Hint: "打到最大射程时还剩几成伤害。", ConfigKey: "FalloffFloor"),
            new("spread", "基础散布(度)", "number", Hint: "越大越不准", ConfigKey: "BaseSpreadDegrees"),
            new("noiseRadius", "噪音半径(像素)", "number", Hint: "多远内的丧尸/劫掠者会被引来", ConfigKey: "NoiseRadius"),
            new("flightSpeed", "弹丸飞行速度", "number", Hint: "远程弹丸/箭矢的飞行速度（像素/秒）；近战武器用不上。", ConfigKey: "FlightSpeed"),
            // DPS 两列：**引擎算的**（WeaponDps），网页只显示、绝不自己写一遍公式。
            new("dps", "每秒伤害", "number", ReadOnly: true,
                Hint: "自动算的（引擎公式，手填会算错）：无甲/贴脸/无限弹药/单挑下的杀伤力天花板。改「伤害」「攻击间隔」「连发数」，它会立刻跟着变。"),
            new("dpsVsLeather", "对皮甲每秒伤害", "number", ReadOnly: true,
                Hint: "自动算的：打一个穿着「皮甲 + 长袖布衣」的人。**含**护甲三段判定、部位覆盖（头/手/脚是裸的，打到那儿等于打无甲）、穿透、霰弹逐颗独立被挡。**不含**距离衰减、噪音、弹药、清群。⚠️ 裸 DPS 是无甲天花板，这一列才看得出「打不打得动甲」。"),
            new("dualDps", "双持每秒伤害", "number", ReadOnly: true,
                Hint: "自动算的。两把同款一起打；不可双持的武器这里是「—」（要改 ⇒ 改「可双持」）。注意它不是「每秒伤害 *1.4」——双持的惩罚只落在冷却上，连发那一段不受罚。"),
            new("weight", "重量(公斤)", "number"),
            new("stockMin", "枪托近战 伤害下限", "number", ConfigKey: "StockMeleeDamageMin"),
            new("stockMax", "枪托近战 伤害上限", "number", ConfigKey: "StockMeleeDamageMax"),
            new("stockInterval", "枪托近战 间隔(秒)", "number", ConfigKey: "StockMeleeInterval"),
            new("stockPenetration", "枪托近战 穿透力", "percent", ConfigKey: "StockMeleePenetration"),
            new("stockNoiseRadius", "枪托噪音半径(像素)", "number", Hint: "抡枪托近战砸人的噪音半径；空 = 近战/弓弩无枪托（回落到全局默认 110）。", ConfigKey: "StockMeleeNoiseRadius"),
            new("stockDamageType", "枪托伤害类型", "chip", Hint: "枪托近战是钝击还是锐击（默认钝；装了刺刀/利爪型改装可为锐）。", ConfigKey: "StockMeleeDamageType",
                ValueMap: new Dictionary<string, string> { ["锐"] = "Sharp", ["钝"] = "Blunt" }),
            new("structureFactor", "砸墙倍率", "mult", Hint: "拿它砸围栏/门时，伤害要乘的倍数。可以大于 1。", ConfigKey: "StructureFactor"),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach ((string member, Weapon w) in CatalogOf<Weapon>(typeof(WeaponTable)))
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = w.Name,
                ["kind"] = WeaponKind(member, w),
                ["damageType"] = w.DamageType == DamageType.Sharp ? "锐" : "钝",
                ["damageMin"] = w.DamageMin,
                ["damageMax"] = w.DamageMax,
                ["penetration"] = w.Penetration,
                ["bleedRate"] = w.BleedRateMultiplier,
                ["bleedOnHit"] = w.BleedOnHitChance,
                ["attackInterval"] = w.AttackInterval,
                ["burstCount"] = w.BurstCount,
                ["burstInterval"] = w.BurstInterval,
                ["pelletCount"] = w.PelletCount,
                ["ammo"] = AmmoLabel(w.AmmoKey),
                ["ammoPerAttack"] = w.AmmoKey.Length == 0 ? 0 : w.BurstCount,
                ["twoHanded"] = w.TwoHanded,
                ["canDualWield"] = w.CanDualWield,
                ["maxRange"] = w.MaxRange,
                ["falloffStart"] = w.FalloffStart,
                ["falloffFloor"] = w.FalloffFloor,
                ["spread"] = w.BaseSpreadDegrees,
                ["noiseRadius"] = w.NoiseRadius,
                ["flightSpeed"] = w.FlightSpeed,
                // 🔴 引擎算的，不是这里推的（WeaponDps 是唯一事实源；网页也不许自己写公式）
                ["dps"] = WeaponDps.Single(w),
                ["dpsVsLeather"] = WeaponDps.AgainstLeatherArmor(w),   // 引擎跑蒙特卡洛，含三段判定+部位覆盖
                ["dualDps"] = WeaponDps.Dual(w),      // 不可双持 ⇒ null ⇒ 网页显示「—」，不是 0
                // 天生武器（爪击/撕咬/拳脚）不是能拿起来的东西，没有重量（ItemWeights 对未登记名会兜底给 2kg，不能当真）。
                ["weight"] = WeaponKind(member, w) == "天生武器" ? null : ItemWeights.WeaponKg(w.Name),
                ["stockMin"] = w.StockMeleeDamageMin,
                ["stockMax"] = w.StockMeleeDamageMax,
                ["stockInterval"] = w.StockMeleeInterval,
                ["stockPenetration"] = w.StockMeleePenetration,
                ["stockNoiseRadius"] = w.StockMeleeNoiseRadius,
                ["stockDamageType"] = w.StockMeleeDamageType == DamageType.Sharp ? "锐" : "钝",
                ["structureFactor"] = w.StructureFactor,
                ["description"] = w.Description,
                ["_id"] = member,
                // config 联动 join 键：PascalCase 成员名 → snake_case（= godot/data/config/weapons.json 的条目键）。
                ["_configId"] = SnakeCase(member),
                ["_anchor"] = $"src/DeadSignal.Combat/WeaponTable.cs :: WeaponTable.{member}()",
            });
        }
        return new Category("weapons", "武器",
            "src/DeadSignal.Combat/WeaponTable.cs",
            "全表武器。伤害为一次攻击的随机区间；穿透削减护甲；噪音半径决定开火后多远的敌人会被引来。"
            + "⚠️ **「每秒伤害」是杀伤力天花板，不是真实战力**：它按无甲、贴脸、无限弹药、单挑算出来，"
            + "**不含**护甲、距离衰减、开枪招来的怪、子弹够不够打、以及一枪撂倒几只。"
            + "枪的真实战力其实由弹药供给决定，而供给不在这个数字里——拿它调平衡时请记着这一点。"
            + "「天生武器」（爪击/撕咬/拳脚）是丧尸、狗和空手的攻击，不是可拾取物品。",
            cols, rows, ConfigFile: "weapons.json");
    }

    /// <summary>
    /// 用表里**当前显示的那几个数**重建一把 <see cref="Weapon"/>，只为把它喂给 <see cref="WeaponDps"/>。
    /// 只取 DPS 用得到的字段（伤害区间 / 攻击间隔 / 连发 / 弹丸 / 强制双手 / 可双持），其余一概不管。
    /// </summary>
    private static Weapon WeaponFromRow(Dictionary<string, object?> row)
    {
        double Num(string key, double fallback = 0)
            => row.TryGetValue(key, out object? v) && v is not null && double.TryParse(v.ToString(), out double d) ? d : fallback;
        bool Flag(string key)
            => row.TryGetValue(key, out object? v) && v is bool b && b;

        return new Weapon
        {
            Name = row.GetValueOrDefault("name") as string ?? "",
            DamageMin = Num("damageMin"),
            DamageMax = Num("damageMax"),
            AttackInterval = Num("attackInterval", 1),
            BurstCount = (int)Num("burstCount", 1),
            BurstInterval = Num("burstInterval"),
            PelletCount = (int)Num("pelletCount", 1),
            TwoHanded = Flag("twoHanded"),
            CanDualWield = Flag("canDualWield"),
        };
    }

    private static string WeaponKind(string member, Weapon w)
    {
        if (member is "ZombieClaw" or "DogBite" or "Fists") return "天生武器";
        if (w.AmmoKey == AmmoKeys.Arrow) return w.Name.Contains('弩') ? "弩" : "弓";
        if (w.IsRanged) return "枪械";
        return w.DamageType == DamageType.Sharp ? "近战锐器" : "近战钝器";
    }

    private static string AmmoLabel(string ammoKey) => ammoKey switch
    {
        "" => "",
        AmmoKeys.ShortBullet => "短子弹",
        AmmoKeys.MediumBullet => "中子弹",
        AmmoKeys.LongBullet => "长子弹",
        AmmoKeys.Buckshot => "鹿弹",
        AmmoKeys.Arrow => "箭",
        _ => ammoKey,
    };

    // ─────────────────────────── 护甲 / 服装 ───────────────────────────

    private static Category Armor()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            // 🔴 「装备槽」和「保护部位」是**两回事**，不能混成一列（用户澄清）：
            //    穿在哪 ≠ 护到哪。板甲占「装甲层 + 裤子」两个槽，护的却是胸腹双臂双腿。
            //    ⚠️ 这里**没有"甲层"这个概念**——贴身层/外套层/装甲层是上身的**三个槽**，不是层级。
            //    （引擎的 ArmorSlot 是**伤害层序**，与"占哪个槽"无关，代码里也写着"别混"。它不给用户看。）
            new("equipSlot", "装备槽", "chip",
                Hint: "这件东西穿在哪（引擎真读它：决定能不能穿、和什么冲突）。上身有三个槽：贴身层／外套层／装甲层——它们是槽，不是层级，可以同时各穿一件。多个槽用「、」隔开。"),
            // [波1·item1] 保护部位：**Wiki 可编辑、但不自动投影代码**。
            // 真正运行时仍由 ArmorTable/ApparelCatalog 消费；这列允许设计稿先改人话显示，agent 再按需落回代码。
            // 不设置 ConfigKey/ConfigScalar，wiki-serve 不会把它写进 armor.json config 或任何运行时配置。
            new("covers", "保护部位",
                Hint: "这件东西实际护到身上哪些地方。可直接改 Wiki 里的显示稿；此列不会自动同步到代码/config，需 agent 另行核对后落地。已折叠显示：「双X」= 左右都护；「手(含指)/脚(含趾)」= 连同该手指/脚趾一起护。没护到的部位命中时一点也不挡。"),
            new("paired", "成对装备", "bool",
                Hint: "手套/鞋子这种：物品本身不分左右，**一件只占一个槽、只护一只手（脚）——要护全得装两件**。「保护部位」列显示的是装在其中一侧时护的部位。"),
            new("sharpDefense", "锐防", "number", ConfigKey: "SharpDefense"),
            new("bluntDefense", "钝防", "number", ConfigKey: "BluntDefense"),
            new("weight", "重量(公斤)", "number", ConfigKey: "Weight"),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach ((string member, ArmorLayer a) in CatalogOf<ArmorLayer>(typeof(ArmorTable)))
        {
            if (member.StartsWith("Dog", StringComparison.Ordinal)) continue; // 狗装备另有一套槽，单列一个分区
            rows.Add(ArmorRow(member, a));
        }
        return new Category("armor", "护甲服装",
            "src/DeadSignal.Combat/ArmorTable.cs（防护数值）+ godot/scripts/ApparelSlots.cs（穿在哪、护到哪）",
            "人穿的衣服与护甲。**「穿在哪」和「护到哪」是两回事**：板甲占「装甲层 + 裤子」两个槽，护的是胸腹双臂双腿。"
            + "没被护到的部位，命中时一点也不挡。"
            + "手套和鞋子是**成对装备**——一件只护一只手（脚），要护全得做两件。"
            + "「腐皮」是丧尸天生的，不是能穿的衣服。",
            cols, rows, ConfigFile: "armor.json");
    }

    /// <summary>
    /// 一件护甲的行。**穿在哪（槽）和护到哪（部位）都从 <see cref="ApparelCatalog"/> 抽**，不手工归类
    /// —— 手工拼是上一版把两个概念混成一列、还凭空造出"甲层"的根因。
    /// </summary>
    private static Dictionary<string, object?> ArmorRow(string member, ArmorLayer a)
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get(a.Name);

        // 装备槽：成对品声明的是**两个候选槽**（左手/右手），一件只占其中一个。
        string slots = def is null ? "" : string.Join('、', def.Slots.Select(DisplayNames.Of).Distinct());

        // 保护部位：**成对品必须按"装在一侧"取**，否则会写成"左手、右手"——那是彻头彻尾的误导
        // （一只劳保手套只护一只手）。非成对品取它的固定覆盖。
        IReadOnlySet<string>? covers = def is null
            ? a.CoversParts
            : def.Paired
                ? def.CoversFor(def.Slots.First())   // 任取一侧：另一侧同构，靠「成对装备」列讲清
                : def.CoversFor(def.Slots.FirstOrDefault());

        return new Dictionary<string, object?>
        {
            ["name"] = a.Name,
            ["equipSlot"] = slots,
            ["covers"] = covers is null ? "" : FoldCovers(covers, def?.Paired ?? false),
            ["paired"] = def?.Paired ?? false,
            ["sharpDefense"] = a.SharpDefense,
            ["bluntDefense"] = a.BluntDefense,
            ["weight"] = a.Weight,
            ["description"] = a.Description,
            ["_id"] = member,
            // config 联动 join 键：PascalCase 成员名 → snake_case（= godot/data/config/armor.json 的条目键）。
            ["_configId"] = SnakeCase(member),
            ["_anchor"] = $"src/DeadSignal.Combat/ArmorTable.cs :: ArmorTable.{member}()"
                          + "（数值）；godot/scripts/ApparelSlots.cs :: ApparelCatalog（装备槽/保护部位）",
        };
    }

    /// <summary>
    /// [波1·item1] **保护部位的显示折叠**（纯抽取器展示层，引擎模型一律不碰）。
    /// <para>输入是引擎给的一串中文部位名（逐指逐趾、分左右），输出折成人话：</para>
    /// <list type="bullet">
    ///   <item>手掌 + 该侧五指 → 「左手(含指)」；脚掌 + 该侧五趾 → 「左脚(含趾)」（指/趾并进掌，不再逐个列）。</item>
    ///   <item>「左X」与「右X」同时在 → 折成「双X」（双臂 / 双大腿 / 双小腿 / 双眼 / 双耳 / 双脚…）。</item>
    ///   <item>成对装备（手套/鞋）：covers 只列代表性的一侧 ⇒ 落单的「左X/右X」去掉方位前缀，并加尾注
    ///         「（穿哪只护哪只，需两件护全）」。</item>
    /// </list>
    /// 保持输入的原有顺序（「双X」落在「左X」原位、丢掉「右X」），不重排。
    /// </summary>
    private static string FoldCovers(IEnumerable<string> rawParts, bool paired)
    {
        var parts = rawParts.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();

        // 手指：含「手」且以「指」结尾（左手小指…），但手掌「左手/右手」本身不算；脚趾：以「趾」结尾（左脚五趾…）。
        bool IsFinger(string p) => p.EndsWith("指", StringComparison.Ordinal) && p.Contains('手') && p != "左手" && p != "右手";
        bool IsToe(string p) => p.EndsWith("趾", StringComparison.Ordinal);

        // ① 指/趾并进手掌/脚掌那一项；其余原样，保持顺序
        var tokens = new List<string>();
        foreach (string p in parts)
        {
            if (IsFinger(p) || IsToe(p)) continue;
            if (p is "左手" or "右手")
            {
                string side = p[..1];
                bool hasFinger = parts.Any(x => IsFinger(x) && x.StartsWith(side, StringComparison.Ordinal));
                tokens.Add(hasFinger ? $"{side}手(含指)" : p);
            }
            else if (p is "左脚" or "右脚")
            {
                string side = p[..1];
                bool hasToe = parts.Any(x => IsToe(x) && x.StartsWith(side, StringComparison.Ordinal));
                tokens.Add(hasToe ? $"{side}脚(含趾)" : p);
            }
            else tokens.Add(p);
        }

        // ② 左右合并：「左X」若有配对的「右X」（首字换成右）⇒ 合成「双X」，丢掉右项
        var folded = new List<string>();
        var dropped = new HashSet<string>();
        foreach (string t in tokens)
        {
            if (dropped.Contains(t)) continue;
            if (t.StartsWith("左", StringComparison.Ordinal))
            {
                string rhs = "右" + t[1..];
                if (tokens.Contains(rhs))
                {
                    folded.Add("双" + t[1..]);
                    dropped.Add(rhs);
                    continue;
                }
            }
            folded.Add(t);
        }

        // ③ 成对装备：covers 只列一侧 ⇒ 落单的「左X/右X」去方位前缀，并加尾注
        if (paired)
        {
            folded = folded
                .Select(t => (t.StartsWith("左", StringComparison.Ordinal) || t.StartsWith("右", StringComparison.Ordinal)) ? t[1..] : t)
                .ToList();
        }

        string joined = string.Join('、', folded);
        if (paired) joined += "（穿哪只护哪只，需两件护全）";
        return joined;
    }

    // ─────────────────────────── 狗装备 ───────────────────────────

    private static Category DogGear()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("slot", "穿戴部位", "chip"),
            new("sharpDefense", "锐防", "number"),
            new("bluntDefense", "钝防", "number"),
            new("weight", "重量(公斤)", "number"),
            new("carryBonus", "额外携带容量(公斤)", "number", Hint: "只有口袋狗衣有"),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach ((string key, DogGearDef d) in DogGearCatalog.Defs)
        {
            ArmorLayer? a = d.Armor();
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = d.DisplayName,
                ["slot"] = DisplayNames.Of(d.Slot),
                ["sharpDefense"] = a?.SharpDefense,
                ["bluntDefense"] = a?.BluntDefense,
                ["weight"] = a?.Weight,
                ["carryBonus"] = d.CarryCapacityBonus,
                ["description"] = d.Description,
                ["_id"] = key,
                ["_anchor"] = "godot/scripts/DogApparel.cs :: DogGearCatalog.Defs（护甲值在 ArmorTable.Dog*）",
            });
        }
        return new Category("dog-gear", "狗装备",
            "godot/scripts/DogApparel.cs + src/DeadSignal.Combat/ArmorTable.cs",
            "布鲁斯（狗）能穿的五件套：身体一件 + 头一件。要道格与布鲁斯的羁绊到 2 级才能做。",
            cols, rows);
    }

    // ─────────────────────────── 弹药与箭 ───────────────────────────

    private static Category Ammo()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("kind", "类型", "chip"),
            // 子弹的「几发/零件」在 ammo.json（表级源）；箭的五个乘子在 archery.json 的 Arrows 子对象里（列级覆盖 + 嵌套根）。
            new("yieldPerPart", "1 个子弹零件造几发", "number", Hint: "箭不吃子弹零件，故为空", ConfigKey: "YieldPerBulletPart"),
            new("damageMult", "伤害倍率", "mult", Hint: "箭反过来改写弓的属性：最终伤害 = 弓的伤害 * 这个数",
                ConfigKey: "DamageMult", ConfigFile: "archery.json", ConfigRoot: "Arrows"),
            new("penetrationMult", "破甲倍率", "mult", Hint: "最终穿透力 = 弓的穿透力 * 这个数",
                ConfigKey: "PenetrationMult", ConfigFile: "archery.json", ConfigRoot: "Arrows"),
            new("rangeMult", "射程倍率", "mult", Hint: "最终射程 = 弓的射程 * 这个数",
                ConfigKey: "RangeMult", ConfigFile: "archery.json", ConfigRoot: "Arrows"),
            new("cooldownMult", "冷却倍率", "mult", Hint: "大于 1 = 出手更慢",
                ConfigKey: "CooldownMult", ConfigFile: "archery.json", ConfigRoot: "Arrows"),
            new("spreadMult", "散布倍率", "mult", Hint: "大于 1 = 更不准",
                ConfigKey: "SpreadMult", ConfigFile: "archery.json", ConfigRoot: "Arrows"),
            new("craftable", "可制作", "bool",
                ConfigKey: "Craftable", ConfigFile: "archery.json", ConfigRoot: "Arrows"),
            new("weight", "单位重量(公斤)", "number"),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (MaterialDef m in DeadSignal.Godot.Materials.InCategory(MaterialCategory.Ammo))
        {
            ArrowDef? arrow = ArrowTable.Find(m.Key);
            int yieldPer = BulletParts.YieldPer(m.Key);
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = m.DisplayName,
                ["kind"] = arrow is not null ? "箭" : "子弹",
                ["yieldPerPart"] = yieldPer > 0 ? yieldPer : (object?)null,
                ["damageMult"] = arrow?.DamageMult,
                ["penetrationMult"] = arrow?.PenetrationMult,
                ["rangeMult"] = arrow?.RangeMult,
                ["cooldownMult"] = arrow?.CooldownMult,
                ["spreadMult"] = arrow?.SpreadMult,
                ["craftable"] = arrow?.Craftable ?? (yieldPer > 0),
                ["weight"] = ItemWeights.MaterialKg(m.Key),
                ["description"] = m.Description,
                ["_id"] = m.Key,
                // config 联动 join 键：材料 key 同时是 ammo.json（子弹）与 archery.json Arrows（箭）的条目键。
                ["_configId"] = m.Key,
                ["_anchor"] = arrow is not null
                    ? $"src/DeadSignal.Combat/Archery.cs :: ArrowTable（倍率）+ godot/scripts/Materials.cs（名称/说明）"
                    : "godot/scripts/Materials.cs :: Materials（名称/说明）+ src/DeadSignal.Combat/Ammo.cs :: BulletParts.YieldPer（制作比）",
            });
        }
        return new Category("ammo", "弹药与箭",
            "godot/scripts/Materials.cs + src/DeadSignal.Combat/Ammo.cs + src/DeadSignal.Combat/Archery.cs",
            "四种子弹（短/中/长/鹿弹）全部从「子弹零件」造，1 个零件造几发决定了这把枪贵不贵。"
            + "四种箭是另一回事——**箭反过来改写弓的属性**：最终属性 = 弓的基础属性 * 这里的倍率。箭可回收，子弹不能。",
            cols, rows, ConfigFile: "ammo.json");
    }

    // ─────────────────────────── 材料 ───────────────────────────

    private static Category Materials()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            // [波1·item2] 类别只读：它来自代码枚举 MaterialCategory，不是用户手改的字段。
            // 放开可编辑会让 TableMerge「表赢代码」按住旧值（老的「Food/零件」就是这么显示不掉的）；
            // 要给某材料换类目，改 godot/scripts/Materials.cs 的 MaterialCategory，重跑即刷新。
            new("category", "类别", "chip", ReadOnly: true,
                Hint: "来自代码里的材料类别枚举（只读）。要改归类，改 Materials.cs 的 MaterialCategory。"),
            // materials.json 是 { "stone": 3, ... } 形态（Dict<id→数值>，没有字段层）⇒ 标量条目，重量列即条目本身。
            new("weight", "单位重量(公斤)", "number", ConfigScalar: true),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (MaterialDef m in DeadSignal.Godot.Materials.All)
        {
            // 弹药与医疗品各自单列分区；但**采来的草药原料归「材料」**（用户拍板：蒲公英/玫瑰果/老君须
            // 是采集来的原料，要加工才成药 —— 成品药膏/药茶才留在医疗分区）。
            if (m.Category is MaterialCategory.Ammo) continue;
            if (m.Category is MaterialCategory.Medical && !RawHerbKeys.Contains(m.Key)) continue;
            // 食材归「食物与食材」分区（那里才有热量点这一列）。
            // 玫瑰果/蒲公英是**两栖的**——既下得了锅又进得了药，故它们按上一行的草药规则留在材料里，
            // 同时也出现在食物分区。这不是重复，是它们本来就有两个身份（代码注释里也这么写的）。
            // 🔴 [T67] 但**只跳过"下得了锅"的食材** —— 老鼠/鸟这类 Food 类却**已不在 FoodCalories** 的
            //    （用户："老鼠和鸟不能直接入锅了，而是要先宰杀"）不属于食物分区（它们没有热量点可列），
            //    它们现在是**宰杀的原料**⇒ 归「材料」分区，否则它们会从整个 wiki 里消失（材料表跳过、食物表也没有）。
            if (m.Category is MaterialCategory.Food && FoodCalories.Has(m.Key)) continue;
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = m.DisplayName,
                ["category"] = MaterialCategoryLabel(m.Category),
                ["weight"] = ItemWeights.MaterialKg(m.Key),
                ["description"] = m.Description,
                ["_id"] = m.Key,
                ["_configId"] = m.Key,   // materials.json 的条目键就是材料 key
                ["_anchor"] = "godot/scripts/Materials.cs :: Materials（重量在 godot/scripts/CarryWeight.cs :: ItemWeights）",
            });
        }
        return new Category("materials", "材料",
            "godot/scripts/Materials.cs",
            "配方吃的基础材料。弹药与医疗品也是材料，但太重要了，各自单开了一个分区。",
            cols, rows, ConfigFile: "materials.json");
    }

    /// <summary>
    /// 材料类别 → **人话中文标签**（候选 10 类，用户拍板）。只在「材料」分区用（<see cref="Materials"/> 里那一处）。
    /// <para>
    /// 🔴 [波1·item2] 两处**英文泄漏**在此堵死：<c>Food</c> 此前没有分支 ⇒ 走 <c>_ =&gt; c.ToString()</c> ⇒
    /// 老鼠 / 鸟的类别在表里直接显示成英文「Food」；<c>Component</c> 是本波新枚举，同样要给中文。
    /// 材料表的「类别」列已改为**只读**（值来自代码枚举，不由用户手改），故这里改一次标签、重跑即全表刷新，
    /// 不会被 TableMerge 的「表赢代码」按住旧值（那正是「Food/零件」一直显示不掉的根因）。
    /// </para>
    /// </summary>
    private static string MaterialCategoryLabel(MaterialCategory c) => c switch
    {
        MaterialCategory.Wood => "木材",
        MaterialCategory.Cloth => "织物",
        MaterialCategory.Metal => "金属",
        MaterialCategory.Leather => "皮革",
        MaterialCategory.Chemical => "化学品",
        MaterialCategory.Component => "精密零件",   // [波1·item2] 机械/子弹/武器零件
        MaterialCategory.Misc => "有机杂料",         // 骨头/石料/绳子/羽毛
        MaterialCategory.Medical => "药材",          // 材料分区里只出现采集来的草药原料（蒲公英/玫瑰果/老君须）
        MaterialCategory.Food => "猎物",             // [波1·item2] 老鼠/鸟（原走 c.ToString() 泄漏成「Food」）
        MaterialCategory.Currency => "货币",
        MaterialCategory.Ammo => "弹药",
        _ => c.ToString(),
    };

    // ─────────────────────────── 医疗与草药 ───────────────────────────

    private static Category Medical()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("use", "用途", "chip"),
            new("treats", "治什么", "chip"),
            // 手术耗材数值在 health.json 的 SurgerySupplies 子对象里；药品数值在 Medicines 子对象里
            // （同一行 _configId=物品 key 只会命中其中一个——耗材没 Efficacy、药品没 Points，另一侧 reconcile 自动跳过）。
            new("surgeryPoints", "手术点数", "number", Hint: "手术要凑够点数才做得成",
                ConfigKey: "Points", ConfigRoot: "SurgerySupplies"),
            new("exclusive", "手术独占", "bool", Hint: "独占的（急救包）不能和别的耗材一起用"),
            // 手术耗材的感染减免：health.json 的 SurgerySupplies.<key>.InfectionChanceMultiplier（草药绷带 0.75，其余 1）。
            new("infectionMult", "感染几率乘子", "mult", Hint: "用这味手术耗材时，该处伤口感染几率要乘的倍数（1 = 不影响；草药绷带 0.75）。空 = 非手术耗材。",
                ConfigKey: "InfectionChanceMultiplier", ConfigRoot: "SurgerySupplies"),
            // 治疗效率：config 存分数(0.35)，wiki 也存分数（type:percent 只让前端渲染成 35%）⇒ **恒等**，不加 PercentTransform。
            new("efficacy", "治疗效率", "percent", Hint: "抗生素 100% 是满效；草药是它的零头。",
                ConfigKey: "Efficacy", ConfigRoot: "Medicines"),
            // 药效基数：health.json 的 Medicines.<key>.Potency（0..1；乘固定照护系数、再乘治疗效率得单次病情消退量）。
            new("potency", "药效基数", "number", Hint: "这味药一次照护的病情消退基数 0..1（成药 0.6、抗生素/草药 0.5）。空 = 非药品。",
                ConfigKey: "Potency", ConfigRoot: "Medicines"),
            // 恶化减缓：抽取器对 ≥1.0 的值显示空（"不影响恶化"的约定），与 config 恒等投影冲突 ⇒ 暂不双向，留 agent 手动。
            new("worsenMult", "恶化减缓", "mult", Hint: "当天的恶化速度要乘的倍数——越小越能拖住病情。空 = 不影响恶化。"),
            // 玫瑰果茶专属：喝下当日昼夜恢复效率 +N 个百分点（加算）。数值真源是 health.json **顶层**标量
            // RosehipTeaHealBonusPct（全局单值，不挂在任何物品条目下）——双向 join 一律按行的 _configId 键入某个
            // id-字典，顶层标量无处安放，故这一列**只读展示**：要改它得去 health.json 顶层，不在本表投影。
            new("rosehipHealBonus", "当日恢复加成(pp)", "number", ReadOnly: true,
                Hint: "只对玫瑰果茶：喝下当日昼夜恢复效率 +这么多个百分点（加算，非乘算）。真源在 health.json 顶层 RosehipTeaHealBonusPct（全局单值，改它去 health.json，本表只读）。"),
            // 重量在 materials.json（标量条目 {key:数值}），与「材料」表同源；这一列级 configFile 覆盖表级 health.json（一表多源）。
            new("weight", "单位重量(公斤)", "number", ConfigFile: "materials.json", ConfigScalar: true),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (MaterialDef m in DeadSignal.Godot.Materials.InCategory(MaterialCategory.Medical))
        {
            if (RawHerbKeys.Contains(m.Key)) continue;   // 采来的草药原料归「材料」分区（用户拍板）

            SurgerySupply? s = SurgeryCatalog.For(m.Key);
            Medicine? med = MedicineCatalog.For(m.Key);
            string use = s is not null ? "手术耗材" : med is not null ? "药品" : "草药原料";
            string treats = s is not null
                ? string.Join('、', s.Value.Treats.Select(ConditionLabel))
                : med is not null ? ConditionLabel(med.Value.Treats) : "";
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = m.DisplayName,
                ["use"] = use,
                ["treats"] = treats,
                ["surgeryPoints"] = s?.Points,
                ["exclusive"] = s?.Exclusive,
                ["infectionMult"] = s?.InfectionChanceMultiplier,   // 手术耗材才有；草药绷带 0.75、其余 1
                ["efficacy"] = med?.Efficacy,
                ["potency"] = med?.Potency,                         // 药品才有
                // 玫瑰果茶专属·health.json 顶层全局单值（只读列）；别的物品该格留空
                ["rosehipHealBonus"] = m.Key == "rosehip_tea"
                    ? GameConfigCatalog.Section<HealthConfig>().RosehipTeaHealBonusPct : (object?)null,
                ["worsenMult"] = med is { WorsenMultiplier: < 1.0 } ? med.Value.WorsenMultiplier : (object?)null,
                ["weight"] = ItemWeights.MaterialKg(m.Key),
                ["description"] = m.Description,
                ["_id"] = m.Key,
                ["_configId"] = m.Key,   // health.json 的 SurgerySupplies/Medicines 条目键 = 物品 key（materials.json 同键）
                ["_anchor"] = "godot/scripts/Materials.cs（名称/说明）+ godot/scripts/HealthConditions.cs :: SurgeryCatalog / MedicineCatalog（数值）",
            });
        }
        return new Category("medical", "医疗与草药",
            "godot/scripts/Materials.cs + godot/scripts/HealthConditions.cs",
            "流血和骨折靠手术（凑手术点数），感染和疾病靠吃药。草药是抗生素用光之后的退路——治得慢，但采得到。",
            cols, rows, ConfigFile: "health.json");
    }

    private static string ConditionLabel(HealthConditionType t) => t switch
    {
        HealthConditionType.Bleeding => "流血",
        HealthConditionType.Fracture => "骨折",
        HealthConditionType.Infection => "感染",
        HealthConditionType.Disease => "疾病",
        _ => t.ToString(),
    };

    // ─────────────────────────── 配方 ───────────────────────────

    private static Category Recipes()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("category", "类别", "chip"),
            // [item9] 产物类型：**按做出来的东西属于哪一大类**（武器/护甲/弹药/医疗/材料/家具…）派生的只读列。
            // 与左边「类别」（RecipeCategory：按什么**工艺**做——木工/化学/缝纫…）是两条正交轴，用户拍板"并存"。
            // 判据 = CraftOutputFactory 的产物分派真源（反射它的私有集合 + Materials 目录 + 狗装备键 + 野外设施集）——
            // 与游戏"这条配方造出来的到底是把武器还是件护甲"完全同口径，新配方自动归类。行的 group 也取它 ⇒ 前端按产物类型分块。
            new("productType", "产物类型", "chip", ReadOnly: true,
                Hint: "做出来的东西属于哪一大类（武器/护甲/弹药/医疗/材料/家具…），按产物自动判定（只读）。它跟左边的「类别」是两回事——「类别」说的是**用什么工艺做**（木工/化学/缝纫…），这一列说的是**做出来的是什么**。"),
            new("output", "产物", Hint: "做出来的是什么（内部 key，引擎真读它）。⚠️ 改它等于把这条配方换成做另一件东西——慎改。"),
            new("outputQty", "产量", "number", ConfigKey: "OutputQuantity"),
            // 「材料」是 dict{料→量}（recipes.json 的 MaterialCosts 嵌套字典）——configDict 嵌套投影接双向：
            // wiki 串「木料*2、布*1」↔ config {"wood":2,"cloth":1}，中文料名↔key 走 DictNameMap（随列下发给 wiki-serve）。
            new("materials", "材料", Hint: "格式：木料*2、布*1", ConfigKey: "MaterialCosts", ConfigDict: true,
                DictNameMap: MaterialDictNameMap(RecipeBook.All.SelectMany(r => r.MaterialCosts.Keys))),
            new("tools", "工作台工具", "chip", Hint: "空 = 徒手就能做"),
            // [波1·item7] 制作地点：从「工作台工具」+「制作者门槛」派生的只读汇总列——一眼看出这条配方在哪做。
            new("craftLocation", "制作地点", "chip", ReadOnly: true,
                Hint: "自动派生（只读）：装了工具就在工作台、茶在烹饪台、宰杀台升级在宰杀台、陷阱/菜园/沙袋在野外空地徒手搭。要改，改配方的工具/门槛。"),
            new("books", "要读过的书"),
            new("workMinutes", "工时", "hours", Hint: "有人站在工作台前干这么久（游戏内时间）。一天有 8 个相位，夜里那个生产相位大约能推进几小时——超过它就得跨夜接着做。", ConfigKey: "WorkMinutes"),
            new("crafterGate", "制作者门槛", Hint: "空 = 谁都能做。人话说明；引擎真正读的门槛 id 在置灰的「勿改」列里。"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            // 引擎真读的门槛 id（cook_station_absent 之类）——**收进置灰「勿改」列，不许出现在给人看的那一列**。
            new("_crafterGateIds", "门槛 id（勿改）", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (RecipeData r in RecipeBook.All)
        {
            string productType = RecipeProductType(r);
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = r.DisplayName,
                ["category"] = RecipeCategoryLabel(r.Category),
                ["productType"] = productType,
                // 行的 group = 产物类型 ⇒ 前端（波2 renderTable）按产物类型分块渲染（同 farming/character-stats 的 row.group 契约）。
                ["group"] = productType,
                ["output"] = r.OutputKey,
                ["outputQty"] = r.OutputQuantity,
                ["materials"] = string.Join('、', r.MaterialCosts.Select(kv => $"{MaterialName(kv.Key)}*{kv.Value}")),
                ["tools"] = string.Join('、', r.RequiredTools.Select(DisplayNames.Of)),
                ["craftLocation"] = CraftLocation(r),
                ["books"] = string.Join('、', r.RequiredBookIds.Select(BookTitle)),
                ["workMinutes"] = r.WorkMinutes,
                ["crafterGate"] = r.RequiredCrafterGates is null ? "" : string.Join('、', r.RequiredCrafterGates.Select(GateLabel)),
                ["_id"] = r.Id,
                ["_configId"] = r.Id,   // recipes.json 的条目键就是配方 Id
                ["_crafterGateIds"] = r.RequiredCrafterGates is null ? "" : string.Join('、', r.RequiredCrafterGates),
                ["_anchor"] = $"godot/scripts/Recipe.cs :: RecipeBook（Id = \"{r.Id}\"）",
            });
        }
        return new Category("recipes", "配方",
            "godot/scripts/Recipe.cs",
            "做一件东西要过三道门槛：工作台上装着对的工具、制作者读过对的书、库存里有够的材料。"
            + "工时是「人站在工作台前推进多少游戏分钟」，不是点一下就出。",
            cols, rows, ConfigFile: "recipes.json");
    }

    // ─────────────────────────── 宰杀配方 ───────────────────────────

    /// <summary>
    /// 宰杀是多产物工序，不塞进 RecipeData（RecipeData 只有一个 OutputKey）。
    /// 这里直接从 ButcheryLogic 的配方清单抽取，避免 Wiki 把「一刀出肉+副产物」拆成两条互相独立的假配方。
    /// </summary>
    private static Category Butchery()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("facility", "设施", "chip", ReadOnly: true),
            new("input", "原料", "chip", ReadOnly: true),
            new("outputs", "产出", "text", ReadOnly: true),
            new("baseWorkMinutes", "基础工时", "number", ReadOnly: true,
                Hint: "这是未计算刀具/设施加速前的基础工时；实际工时会按刀具和设施速度缩短。"),
            new("doubleYieldChance", "双倍产出概率", "number", ReadOnly: true,
                Hint: "只有宰杀台有这条额外判定；简易宰杀点为 0。"),
            new("description", "简介", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (ButcherRecipe r in ButcheryLogic.Recipes)
        {
            string facility = r.Tier == ButcherTier.Table ? "宰杀台" : "简易宰杀点";
            string outputs = $"{MaterialName(r.ByproductKey)}*{r.ByproductQuantity}、{MaterialName(r.MeatKey)}*{r.MeatQuantity}";
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = $"{facility}：{r.QuarryName}",
                ["facility"] = facility,
                ["input"] = r.QuarryName,
                ["outputs"] = outputs,
                ["baseWorkMinutes"] = ButcheryLogic.BaseMinutes,
                ["doubleYieldChance"] = r.Tier == ButcherTier.Table
                    ? ButcheryLogic.TableDoubleYieldChance * 100
                    : 0,
                ["description"] = r.Tier == ButcherTier.Table
                    ? "宰杀台基础产出；命中双倍判定时，肉和副产物一起翻倍。"
                    : "简易宰杀点基础产出，不触发双倍判定。",
                ["_id"] = r.Id,
                ["_anchor"] = $"godot/scripts/Butchery.cs :: ButcheryLogic（Id = \"{r.Id}\"）",
            });
        }

        return new Category("butchery", "宰杀配方",
            "godot/scripts/Butchery.cs",
            "宰杀不是普通单产物制作：一只猎物上案板后，同时产出肉和副产物。"
            + "简易宰杀点处理老鼠/鸟；宰杀台按 Wiki 的三条基础产出处理老鼠、兔子、鸟，并保留 20% 双倍产出判定。",
            cols, rows);
    }

    /// <summary>
    /// 「造→摆」的野外设施配方 id —— 无工具、无站点门槛，在营地空地上徒手搭出来的东西
    /// （陷阱 / 菜园 / 沙袋）。列在这里才把它们和「工作台徒手活」区分开。
    /// </summary>
    private static readonly HashSet<string> FieldRecipeIds = new(StringComparer.Ordinal)
    {
        "snare_trap",   // 圈套陷阱
        "bird_trap",    // 捕鸟陷阱
        "crop_plot",    // 菜园
        "sandbag",      // 沙袋（防御工事，同属野外空地搭建）
    };

    /// <summary>
    /// [波1·item7] **制作地点**——从 <see cref="RecipeData.RequiredTools"/> + <see cref="RecipeData.RequiredCrafterGates"/> 纯派生（不改 Recipe.cs 业务）。
    /// <para>优先级：</para>
    /// <list type="number">
    ///   <item>装了工作台工具（卡尺/锯片/烧杯）⇒ 「工作台（<i>工具名</i>）」——工具优先于门槛
    ///         （如「改装台」自身要卡尺，虽带 mod_bench_absent 门槛，仍在工作台上做）。</item>
    ///   <item>门槛 <c>cook_station_present</c>（茶要在灶上煮）⇒ 「烹饪台」；
    ///         <c>butcher_point_present</c>（升级宰杀点）⇒ 「宰杀台」。</item>
    ///   <item>门槛以 <c>_absent</c> 结尾 ⇒ 这条配方本身在**造那座设施**（烹饪台 / 简易宰杀点）
    ///         ⇒ 在营地空地上徒手搭 ⇒ 「野外（徒手）」。</item>
    ///   <item>无工具、无站点门槛：属 <see cref="FieldRecipeIds"/>（陷阱/菜园/沙袋）⇒ 「野外（徒手）」；否则 ⇒ 「工作台（徒手）」。</item>
    /// </list>
    /// <para>⚠️ [DECISION] 任务口径原文是「butcher_* → 宰杀台」，这里细化为：<c>butcher_point_present</c>（升级动作，在宰杀点上做）→ 宰杀台；
    /// <c>butcher_absent</c>（**建**简易宰杀点这条配方本身）→ 野外（徒手）——不然会读成「在宰杀台上造出宰杀台」的悖论。同理 <c>cook_station_absent</c> → 野外（徒手）。</para>
    /// </summary>
    private static string CraftLocation(RecipeData r)
    {
        if (r.RequiredTools.Count > 0)
        {
            return $"工作台（{string.Join('、', r.RequiredTools.Select(DisplayNames.Of))}）";
        }

        IReadOnlyList<string> gates = r.RequiredCrafterGates ?? Array.Empty<string>();
        if (gates.Contains("cook_station_present")) return "烹饪台";
        if (gates.Contains("butcher_point_present")) return "宰杀台";
        if (gates.Any(g => g.EndsWith("_absent", StringComparison.Ordinal))) return "野外（徒手）";

        return FieldRecipeIds.Contains(r.Id) ? "野外（徒手）" : "工作台（徒手）";
    }

    private static string RecipeCategoryLabel(RecipeCategory c) => c switch
    {
        RecipeCategory.Woodwork => "木工",
        RecipeCategory.Precision => "精工/弓弩",
        RecipeCategory.Chemistry => "化学",
        RecipeCategory.Tailoring => "缝纫",
        RecipeCategory.Misc => "杂项",
        _ => c.ToString(),
    };

    // [item9] 产物类型分类 —— **完全照 CraftOutputFactory.Create 的分派顺序**（那是游戏"这条配方造出来是什么"的唯一真源）：
    //   材料目录命中 → 再按 MaterialCategory 细分（弹药/医疗/材料）；否则查武器集/护甲集（护甲集里狗装备键单拎"狗装备"）/光源集；
    //   都不是 ⇒ 落 Furniture 那条兜底分支（Item.Material + FurnitureFlavor）——再按野外设施集拆"陷阱与工事"vs"家具与设施"。
    // 三个 *Outputs 集是 CraftOutputFactory 的私有 static 字段，反射取（同 LightCatalog 反射 LightSource._byKey 的先例）：
    // 游戏那边增补一个产物 key，这里自动跟着归类，不必手抄。
    private static readonly Lazy<IReadOnlySet<string>> _weaponOutputs = new(() => CraftOutputSet("WeaponOutputs"));
    private static readonly Lazy<IReadOnlySet<string>> _armorOutputs = new(() => CraftOutputSet("ArmorOutputs"));
    private static readonly Lazy<IReadOnlySet<string>> _lightOutputs = new(() => CraftOutputSet("LightOutputs"));
    private static readonly HashSet<string> _dogGearKeys = new(DogGearCatalog.AllKeys, StringComparer.Ordinal);

    private static IReadOnlySet<string> CraftOutputSet(string fieldName)
    {
        FieldInfo f = typeof(CraftOutputFactory).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"CraftOutputFactory.{fieldName} 不见了——产物分类字段换实现了，改这里。");
        return (IReadOnlySet<string>)f.GetValue(null)!;
    }

    private static string RecipeProductType(RecipeData r)
    {
        string key = r.OutputKey;

        // ① 材料目录命中（弹药/医疗都归在 Materials 里，走 MaterialCategory 细分）
        if (DeadSignal.Godot.Materials.Find(key) is { } mat)
        {
            return mat.Category switch
            {
                MaterialCategory.Ammo => "弹药",
                MaterialCategory.Medical => "医疗",
                _ => "材料",   // 木/布/金属/皮/化学/杂项/精密零件（含子弹零件）等
            };
        }

        // ② 武器
        if (_weaponOutputs.Value.Contains(key)) return "武器";

        // ③ 护甲（狗装备键单拎出来）
        if (_armorOutputs.Value.Contains(key)) return _dogGearKeys.Contains(key) ? "狗装备" : "护甲/服装";

        // ④ 光源
        if (_lightOutputs.Value.Contains(key)) return "光源";

        // ⑤ 兜底（家具/设施）：野外徒手搭的设施（陷阱/菜园/沙袋）单拎"陷阱与工事"，其余为"家具与设施"
        return FieldRecipeIds.Contains(r.Id) ? "陷阱与工事" : "家具与设施";
    }

    /// <summary>材料 key → 中文名（查不到就原样回退：产物 key 可能是武器/护甲名而非材料）。</summary>
    private static string MaterialName(string key)
        => DeadSignal.Godot.Materials.Find(key)?.DisplayName ?? key;

    /// <summary>
    /// **中文料名 → config key** 映射（<see cref="Col.ConfigDict"/> 材料列专用），从给定料键集派生。
    /// <para>
    /// 用来让 wiki-serve 在「木料*2、布*1」人话串与 <c>{"wood":2,"cloth":1}</c> 嵌套字典之间双向转换——
    /// 它无法调用 C# 的 <see cref="MaterialName"/>，故抽取器把这张表随列一起下发（<see cref="Col.DictNameMap"/>）。
    /// </para>
    /// <para>🔴 <b>碰撞即 fail-fast</b>：若两个 key 渲染成同一个中文名，反向解析会二义 ⇒ 这一列本就无法可靠双向，直接抛让人看见。</para>
    /// </summary>
    private static IReadOnlyDictionary<string, string> MaterialDictNameMap(IEnumerable<string> keys)
    {
        var map = new Dictionary<string, string>();
        foreach (string key in keys.Distinct())
        {
            string name = MaterialName(key);
            if (map.TryGetValue(name, out string? existing) && existing != key)
            {
                throw new InvalidOperationException(
                    $"材料 dict 双向映射中文名碰撞：「{name}」同时是 key「{existing}」与「{key}」——这一列无法可靠双向解析，"
                    + "须给其中一个改显示名再标 ConfigDict。");
            }
            map[name] = key;
        }
        return map;
    }

    private static string BookTitle(string bookId)
        => BookLibrary.All().FirstOrDefault(b => b.Id == bookId)?.Title is { } t ? $"《{t}》" : bookId;

    /// <summary>
    /// 制作者门槛 id → <b>人话</b>。
    ///
    /// <para>🔴 <b>这张表禁代码腔</b>（CLAUDE.md：数值表「中文名主键、人话说明，不出现类名/英文 id/引擎术语」）。
    /// [T59] 之前这里只映射了 <c>doug_bond_l2</c>，另外两个门槛
    /// （<c>cook_station_absent</c> / <c>mod_bench_absent</c>）直接走 <c>_ =&gt; gate</c> 兜底，
    /// 于是**两个英文内部 id 就这么坐在了给用户看的中文表里**。用户看见后<b>把烹饪台那一格清空了</b>——
    /// 抽取器随即把它报成「制作者门槛被改成空」，看起来像是「用户想拆掉唯一性限制」。
    /// <b>那是个显示 bug 引发的误读，不是设计改动</b>：真拆了限制，玩家就能造出第二座烹饪台。
    /// ⇒ 修的是<b>显示</b>：这里补齐人话，原始 id 收进置灰的 <c>_crafterGateIds</c>「勿改」列。</para>
    /// </summary>
    private static string GateLabel(string gate) => gate switch
    {
        "doug_bond_l2" => "道格，且与布鲁斯的羁绊达到 2 级",
        "cook_station_absent" => "营地里还没有烹饪台（已有就不能再造第二座）",
        "mod_bench_absent" => "营地里还没有改装台（已有就不能再造第二台）",
        // [T67] 采集/种植/诱捕支柱的三道门槛
        "cook_station_present" => "营地里已经有一座烹饪台（茶要在灶上煮）",
        "butcher_absent" => "营地里还没有宰杀设施（已有就不能再造）",
        "butcher_point_present" => "营地里已有简易宰杀点（升级成宰杀台的前提）",

        // 🔴 新加门槛却忘了在这里给它一句人话 ⇒ 英文 id 会漏进用户的表（就是上面那个 bug）。
        //    宁可当场吵出来，也不要再静默泄一个代码腔进去。
        _ => LoudUnknownGate(gate),
    };

    /// <summary>没登记人话的门槛：照旧回退成 id（表还能用），但把它喊进待办报告里，逼人补上。</summary>
    private static string LoudUnknownGate(string gate)
    {
        TableMerge.Drift.Add(
            $"  [⚠️ 工具缺陷] 制作者门槛「{gate}」**没有人话映射** ⇒ 这个英文内部 id 会直接显示在用户的表里"
            + "（数值表禁代码腔）。请去 Program.GateLabel 给它补一句中文。");
        return gate;
    }

    // ─────────────────────────── 光源 ───────────────────────────

    private static Category Lights()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("kind", "类型", "chip", Hint: "手持的占一只手；固定的钉在营地里"),
            new("intensity", "亮度", "percent", Hint: "照亮自己，也照亮别人眼里的你。"),
            new("radius", "照亮半径(像素)", "number"),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (LightProfile p in LightCatalog())
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = p.DisplayName,
                ["kind"] = p.Kind == LightKind.Handheld ? "手持" : "固定",
                ["intensity"] = p.Intensity,
                ["radius"] = p.Radius,
                ["description"] = p.Description,
                ["_id"] = p.Key,
                ["_anchor"] = "godot/scripts/LightSource.cs :: LightSource",
            });
        }
        return new Category("lights", "光源",
            "godot/scripts/LightSource.cs",
            "夜里看得见东西，全靠这几样。代价是：点着光的人，在黑暗里也是最显眼的那个。",
            cols, rows);
    }

    /// <summary>LightSource 没有公开的「全部光源」入口，反射拿它的私有目录 —— 将来新加光源也能自动出现。</summary>
    private static IEnumerable<LightProfile> LightCatalog()
    {
        FieldInfo f = typeof(LightSource).GetField("_byKey", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("LightSource._byKey 不见了——光源目录换实现了，改这里。");
        var map = (IReadOnlyDictionary<string, LightProfile>)f.GetValue(null)!;
        return map.Values;
    }

    // ─────────────────────────── 书籍 ───────────────────────────

    private static Category Books()
    {
        var cols = new List<Col>
        {
            new("title", "书名", Primary: true),
            new("readHours", "读完要几小时", "number"),
            // 列名「效果」而非「解锁什么」（用户拍板）：书不只有"解锁配方"一种作用——《弓与箭之道》
            // 给的是箭矢回收率 25%→50% 的被动，那是效果，不是解锁。字段 key 保持 unlocks 不动（内部层，用户看不见）。
            // 「效果」一列吃下书的**所有**作用（解锁配方 / 被动加成 / 手术点数…）——用户拍板：
            // 不给每种效果单开一列，否则表越来越宽、大半格子还是空的。
            //
            // ⚠️ 原「手术加成点数」列**是可编辑的**，删掉它就等于砍掉用户调那个数的入口。
            // 所以那个数字**写进这一列的文本里**（"手术点数 +5"）——编辑入口没丢：
            // 用户改这句话，agent 照着同步进 MedicalBookPoints。原始值另存进置灰锚点列供精确回写。
            new("unlocks", "效果", Hint: "读完这本书带来什么：解锁的配方、给的被动加成、让手术更有把握……都写这一列，用人话。数字直接写在句子里，agent 会照它同步进代码。"),
            new("body", "正文", "longtext"),
            // 玩家在游戏里看到的短简介：代码里已有字段（BookData.Description），复用「简介」列。
            new("description", "简介", "longtext"),
            new("_id", "内部 id", Internal: true),
            // 手术点数不再单独占一列（已并进「效果」），但**原始数值留在这里**：
            // agent 回写代码时要有个精确锚点，不能从散文里猜数字。
            new("_surgeryPoints", "手术点数(原始值)", "number", Internal: true,
                Hint: "真值在 godot/scripts/HealthConditions.cs :: MedicalBookPoints。想调它，改「效果」列里的那个数即可。"),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        // 🔴 [T59] **只列真正的"书"** —— 日记是道具，不是书（它没有"阅读工时"，那一列对它全是错的）。
        //    日记另开一张表（见 Diaries()）。用户口径：书给角色读、日记给玩家读。
        foreach (BookData b in BookLibrary.Manuals())
        {
            int pts = MedicalBookPoints.For(b.Id);
            rows.Add(new Dictionary<string, object?>
            {
                ["title"] = b.Title,
                ["readHours"] = b.ReadHours,
                ["unlocks"] = BookEffect(b.Id, pts),
                ["body"] = b.Body,
                ["description"] = b.Description,
                ["_id"] = b.Id,
                ["_surgeryPoints"] = pts > 0 ? pts : (object?)null,
                ["_anchor"] = "godot/scripts/BookData.cs :: BookLibrary"
                              + (pts > 0 ? "；手术点数在 godot/scripts/HealthConditions.cs :: MedicalBookPoints" : ""),
            });
        }
        return new Category("books", "书籍",
            "godot/scripts/BookData.cs",
            "本作没有技能系统——**能力只由角色的专属效果和读过的书承载**。配方解锁只看谁读过哪本书。"
            + "「读完要几小时」是**角色的时间**：读书的人整夜占着座位，那一夜他不能站岗、也不能干活——这就是书的代价。"
            + "（日记不在这张表里：它是给玩家看的道具，不花角色一分钟，见「日记与笔记」。）",
            cols, rows);
    }

    // ─────────────────────────── 日记与笔记（道具，非书）───────────────────────────

    /// <summary>
    /// [T59] <b>日记单独一张表</b>（用户拍板：「日记不是书」）。
    /// <para>它<b>没有「读完要几小时」这一列</b>——日记不由角色去读，**根本没有工时这回事**；
    /// 也没有「效果」列——它什么都不解锁，只讲故事。表里只有<b>正文</b>（authored，用户手写）。</para>
    /// </summary>
    private static Category Diaries()
    {
        var cols = new List<Col>
        {
            new("title", "标题", Primary: true),
            new("body", "正文", "longtext",
                Hint: "玩家在库存里点开就能看的全文（看的时候游戏是暂停的）。这是剧情文本，你写什么就是什么。"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (BookData b in BookLibrary.Diaries())
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["title"] = b.Title,
                ["body"] = b.Body,
                ["_id"] = b.Id,
                ["_anchor"] = "godot/scripts/BookData.cs :: BookLibrary（BookData.Diary）",
            });
        }
        return new Category("diaries", "日记与笔记",
            "godot/scripts/BookData.cs",
            "**它们不是书，是道具。** 玩家在库存里点开就能看全文，游戏冻结着看，**不花任何角色的时间**——"
            + "捡到就等于读到了。它们什么也不解锁，只讲故事。"
            + "（对照：「书籍」表里那些要角色整夜坐着读的，才是书。）",
            cols, rows);
    }

    /// <summary>
    /// 一本书的**全部**效果，合成一句人话（用户拍板：不给每种效果单开一列）。
    /// 手术点数**写进文本里**，因为原来那一列是可编辑的——删了列，这句话就是新的编辑入口。
    /// </summary>
    private static string BookEffect(string bookId, int surgeryPoints)
    {
        var parts = new List<string>();

        string recipes = RecipesUnlockedBy(bookId);
        if (recipes.Length > 0) parts.Add("解锁配方：" + recipes);

        // 手术加点可以带条件（《野外生存指南》只在徒手时算），所以这句话必须把条件也说出来——
        // 否则表上写着"+6"、游戏里用了绷带却没加，用户会以为是 bug。
        if (surgeryPoints > 0)
        {
            parts.Add(MedicalBookPoints.RequiresNoSupplies(bookId)
                ? $"被动：不使用任何手术材料时，手术加成点数 +{surgeryPoints}（教的是没器械时的土办法；一旦投了正规耗材就不加）"
                : $"被动：读过它的人做手术更有把握（手术点数 +{surgeryPoints}）");
        }

        // 书籍被动不全是配方或手术点：尖峰时刻的骨折恢复、尖峰时刻·三的野外识别也要进入同一列，
        // 否则 Wiki 会把已经接线的能力误报成“只有配方”。
        if (bookId == BookLibrary.PeakHourId)
        {
            parts.Add("被动：骨折恢复速度 +15%");
        }
        if (bookId == BookLibrary.PeakHourThreeId)
        {
            parts.Add("被动：认得葛根和大黄，可在野外交互采集");
        }

        return string.Join("；", parts);
    }

    private static string RecipesUnlockedBy(string bookId)
    {
        string[] unlocked = RecipeBook.All
            .Where(r => r.RequiredBookIds.Contains(bookId))
            .Select(r => r.DisplayName)
            .ToArray();
        if (unlocked.Length > 0) return string.Join('、', unlocked);
        return bookId == "way_of_bow_and_arrow" ? "被动：箭矢回收率 25% → 50%" : "";
    }

    // ─────────────────────────── 武器改装 ───────────────────────────

    private static Category WeaponMods()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            // 🔴 [SPEC-B21] 装配约束已从「武器大类」换成「**逐把枪的白名单**」（用户拍板）。
            //    原来那两列（大类 chip + 只读的"可装于"展开）**合并成这一列** —— 它就是引擎真读的东西，
            //    勾掉一把枪，那把枪当场就装不上这个改装了。不再有"看得见但改不了"的展开列。
            new("fitsWeapons", "可装于哪些武器", "multiselect",
                Hint: "勾上的武器才装得上这个改装。**这就是引擎真读的约束**——不是展示，改了立刻生效。"),
            new("part", "占用部位", "chip", Hint: "一个部位只能装一件；不同部位可以同时装"),
            // 🔴 [待扩·config-niche 裁定] 「数值改动」列**不接双向**（保持 agent 手动），根因是结构性不可逆，非遗漏：
            //   ① weaponmods.json 的 Stats 只存 {StatName: value}，**运算符（加/乘/覆盖 +/*/=）不在 json**——它在
            //      WeaponModCatalog.cs 的 C# 里按 stat 推断。这一列的人话串同时编码了运算符，config 里没有可映射的字段。
            //   ② 串还混入**非 config 字段**：重量倍率（WeightMultiplier 在 json，但同串渲染）、允许单手持有（bool·纯代码）、
            //      攻击 N 次后失去（实例状态·纯代码）——串携带的信息严格多于 config，无干净 1:1。
            //   ③ 枚举名↔中文（DamageMin↔伤害下限）映射也在 C#。要双向须写"结构化 stats 解析器"，本就说明"几乎永远显示待同步"。
            new("stats", "数值改动", Hint: "装上这件改装后，武器的哪些数值怎么变。格式：「伤害下限 +2、穿透 *1.2」。加/乘/覆盖分别写 +、*、=。"),
            new("form", "近战型态", "chip", Hint: "改写枪托近战的打法；一把枪只能装一条带型态的改装"),
            new("materials", "材料", Hint: "格式：铁*2、布*1"),
            new("workMinutes", "工时", "hours", Hint: "有人站在改装台前干这么久（游戏内时间）。"),
            new("note", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (WeaponMod m in WeaponModCatalog.All())
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = m.Name,
                // 白名单：按武器表的**声明顺序**排（不是字母序）——这样看起来和武器分区一致
                ["fitsWeapons"] = string.Join('、',
                    WeaponModCatalog.AllModdableWeapons()
                        .Where(w => m.FitsWeapons.Contains(w.Name))
                        .Select(w => w.Name)),
                ["part"] = DisplayNames.Of(m.Part),
                ["stats"] = StatsLabel(m),
                ["form"] = m.Form is null ? "" : DisplayNames.Of(m.Form.Value),
                ["materials"] = string.Join('、', m.MaterialCosts.Select(kv => $"{MaterialName(kv.Key)}*{kv.Value}")),
                ["workMinutes"] = m.WorkMinutes,
                ["note"] = m.Note,
                // 改装现在有稳定的 Id（bayonet / claw_stock…）⇒ 用它当代码锚，比中文名可靠
                ["_id"] = m.Id,
                ["_anchor"] = $"godot/scripts/WeaponModCatalog.cs :: WeaponModCatalog（Id = \"{m.Id}\"）",
            });
        }
        return new Category("weapon-mods", "武器改装",
            "godot/scripts/WeaponModCatalog.cs",
            "给武器加零件。一个部位只装得下一件，装不下的会被拒绝。"
            + "「可装于哪些武器」是**引擎真读的装配约束**——勾掉一把枪，它当场就装不上了，不是摆设。"
            + "⚠️「数值改动」这一列你写的是**人话**，而代码那边是结构化字段（比如你写「攻击速度+5%」，引擎里是「攻击间隔 *0.95」）"
            + "⇒ 这一列**几乎永远会显示成「待同步」，那只是两种写法的差异，不代表真的没落地**。要确认，看代码注释或问 agent。"
            + "🔴 **一把枪只能装一种近战改装**（刺刀型 / 利爪型 / 创伤型 **三选一**）——它们各自把这把枪的近战打法整个换掉，"
            + "同时装两个就等于给同一把枪写了两套互相打架的近战定义。装第二个时会被当场拒绝，并告诉你跟哪一个冲突。"
            + "⚠️ 弓弩**已经不能装枪械改装了**（它们曾因一个 bug 被引擎当成「枪」）。"
            + "消防斧已按「和长剑同档」勾进锐器改装（6 条里的 5 条）——**唯独「镂空剑刃」没勾**："
            + "斧子靠的就是那颗沉头，镂空把它挖空了，就成了一把很差的剑。",
            cols, rows);
    }

    /// <summary>
    /// 一条改装的「数值改动」列（代码侧渲染）。
    ///
    /// <para>
    /// 🔴 <b>[T47] 必须把 <c>Stats</c> 之外的三个字段也渲染出来</b>，否则一旦有人跑 <c>--reset</c>，
    /// 用户写在表上的「<b>重量 −25%~+50%</b>」「<b>攻击三次后失去该改装</b>」「<b>允许单手持有</b>」
    /// 会被**悄悄抹掉** —— 它们不是 <see cref="StatMod"/>（重量是消费层概念、次数是实例状态、持握是结构 bool），
    /// 从前的渲染只扫 <c>m.Stats</c>，看不见它们。而重量恰恰是用户这套设计的**核心代价轴**。
    /// </para>
    /// </summary>
    private static string StatsLabel(WeaponMod m)
    {
        var parts = m.Stats.Select(StatModLabel).ToList();

        if (System.Math.Abs(m.WeightMultiplier - 1.0) > 1e-9)
        {
            parts.Add($"重量 *{Round(m.WeightMultiplier)}");
        }
        if (m.AllowsOneHanded)
        {
            parts.Add("允许单手持有");
        }
        if (m.UsesBeforeBreak is { } uses)
        {
            parts.Add($"攻击 {uses} 次后失去该改装");
        }

        return string.Join('、', parts);
    }

    private static string StatModLabel(StatMod s)
    {
        string stat = s.Stat switch
        {
            WeaponStat.DamageMin => "伤害下限",
            WeaponStat.DamageMax => "伤害上限",
            WeaponStat.Penetration => "穿透",
            WeaponStat.AttackInterval => "攻击间隔",
            WeaponStat.BaseSpreadDegrees => "基础散布",
            WeaponStat.MaxRange => "最大射程",
            WeaponStat.FalloffStart => "衰减起点",
            WeaponStat.FalloffFloor => "最远伤害系数",
            WeaponStat.StockMeleeDamageMin => "枪托伤害下限",
            WeaponStat.StockMeleeDamageMax => "枪托伤害上限",
            WeaponStat.StockMeleeInterval => "枪托间隔",
            WeaponStat.StockMeleePenetration => "枪托穿透",
            // ⚠ 这一条从前漏了 ⇒ 会退化成 ToString() 吐出英文枚举名（StockMeleeNoiseRadius）到用户表上。
            //   三种近战型态都会写它（枪托噪音随刺剑/消防斧/尖头锤改变），所以它必须有中文名。
            WeaponStat.StockMeleeNoiseRadius => "枪托噪音半径",
            _ => s.Stat.ToString(),
        };
        string op = s.Op switch
        {
            StatOp.Add => s.Value >= 0 ? "+" : "",
            StatOp.Mul => "*",
            StatOp.Set => "=",
            _ => " ",
        };
        return $"{stat} {op}{Round(s.Value)}";
    }

    // ─────────────────────────── 家具建造 ───────────────────────────

    // ─────────────────────────── 食物与食材 ───────────────────────────

    /// <summary>
    /// 食材热量点表。**这是 wiki 唯一能看到热量点的地方 —— 游戏里不显示。**
    /// <para>
    /// 用户拍板：每种食材几点热量、一份饭要多少热量，是**玩家自己探索试错**的核心乐趣。所以游戏内
    /// 不足一份时按钮只是不可用、不解释原因；零头浪费不提示；只有够两份时才显示产物 *2。
    /// 而这张表是**给设计者调数值的**，两件事不矛盾——但别把这里的口径带进游戏 UI。
    /// </para>
    /// </summary>
    private static Category Food()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("calories", "热量点", "number", Hint: "这一个单位的食材能贡献多少热量。一份饭要 16 点（装了炊具更少）"),
            new("portions", "几个够做一份饭", "number", ReadOnly: true,
                Hint: "自动算的：一份饭要多少热量 ÷ 这个食材的热量点（向上取整）。要改 ⇒ 改「热量点」，或去「烹饪规则」改一份饭要多少热量。"),
            new("description", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (FoodCalories.FoodDef f in FoodCalories.All)
        {
            MaterialDef? m = DeadSignal.Godot.Materials.Find(f.Key);
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = m?.DisplayName ?? f.Key,
                ["calories"] = f.Calories,
                // 向上取整：够不够一份是「凑满 16 点」，7 点的面粉要 3 袋（2 袋才 14 点，不够）。
                ["portions"] = f.Calories > 0
                    ? (int)Math.Ceiling(CookingLogic.BasePortionCost / (double)f.Calories)
                    : (object?)null,
                ["description"] = m?.Description ?? "",
                ["_id"] = f.Key,
                ["_anchor"] = "godot/scripts/CookingLogic.cs :: FoodCalories（热量点）+ godot/scripts/Materials.cs（名称/说明）",
            });
        }
        return new Category("food", "食物与食材",
            "godot/scripts/CookingLogic.cs + godot/scripts/Materials.cs",
            "能下锅的东西。凑够一份饭的热量，就能在烹饪台做出一份食物——凑不够就做不成。"
            + "⚠️ 热量点**只在这张表里看得到**：游戏里玩家看不见任何数字，得自己试出来「几只老鼠才够一顿饭」，"
            + "那是刻意设计的乐趣，别把这些数字搬进游戏 UI。"
            + "（玫瑰果和蒲公英同时也是草药——末日里没人挑食，所以它们在「材料」分区里也有一份。）",
            cols, rows);
    }

    /// <summary>烹饪的几个可调数（一行一个数字，同「角色数值」的形状）。</summary>
    private static Category CookingRules()
    {
        var cols = new List<Col>
        {
            new("label", "规则", Primary: true),
            new("value", "数值", "number"),
            new("unit", "单位", "chip", Hint: "这个数字的单位。只是给人看的标签。"),
            new("note", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["label"] = "一份饭要多少热量",
                ["value"] = CookingLogic.BasePortionCost,
                ["unit"] = "热量点",
                ["note"] = "基础值。装了炊具会更少。份数 = 总热量 ÷ 这个数，向下取整——凑不满一份就做不成，多出来的零头浪费掉（游戏里不提示）。",
                ["_id"] = "base_portion_cost",
                ["_anchor"] = "godot/scripts/CookingLogic.cs :: CookingLogic.BasePortionCost",
            },
            // [批次21·T14] 锅与烤架的减免**已拆成两个独立值**（原先共用一个常量 ⇒ 改一个另一个跟着变，调不动）。
            // 现在这两行可以分别改，互不影响。
            new()
            {
                ["label"] = "装上「锅」省下的热量",
                ["value"] = CookingLogic.PotDiscount,
                ["unit"] = "热量点",
                ["note"] = "烹饪台两个槽位之一。装上它，每做一份饭就少要这么多热量。与烤架**各是各的数**，可以分别调。",
                ["_id"] = "pot_discount",
                ["_anchor"] = "godot/scripts/CookingLogic.cs :: CookingLogic.PotDiscount",
            },
            new()
            {
                ["label"] = "装上「烤架」省下的热量",
                ["value"] = CookingLogic.GrillDiscount,
                ["unit"] = "热量点",
                ["note"] = "烹饪台另一个槽位。两个槽都装满，一份饭省下的就是这两个数相加。与锅**各是各的数**，可以分别调。",
                ["_id"] = "grill_discount",
                ["_anchor"] = "godot/scripts/CookingLogic.cs :: CookingLogic.GrillDiscount",
            },
            new()
            {
                ["label"] = "做一份饭的工时",
                ["value"] = CookingLogic.WorkMinutesPerPortion,
                ["unit"] = "游戏分钟",
                ["note"] = "得有人站在烹饪台前把活干完。做两份就干两份的活——没有「一锅端」的规模效应。",
                ["_id"] = "work_minutes_per_portion",
                ["_anchor"] = "godot/scripts/CookingLogic.cs :: CookingLogic.WorkMinutesPerPortion",
            },
        };

        return new Category("cooking", "烹饪规则",
            "godot/scripts/CookingLogic.cs",
            "烹饪的几个可调数。⚠️ 同「食物与食材」分区：这些数字**游戏里一个都不显示**，玩家得自己试出来。",
            cols, rows);
    }

    /// <summary>
    /// **全局规则** —— 不属于任何单件物品、也不属于任何一个角色，但**对所有人一体适用**的那些数。
    ///
    /// <para><b>它为什么必须存在</b>：这些数以前**散落在代码常量里，用户在 wiki 上根本看不到、也改不了**。
    /// 更糟的是——「没座位读书 *0.9」曾被错列在**诺蒂名下**，让人以为那是她的专属效果。
    /// 一条全员通则被当成某个角色的特性，用户就会调错数值（以为"只影响她一个人"）。</para>
    ///
    /// <para>⚠️ 收进来的必须是**真有代码锚点**的常量，一个都不许编。</para>
    /// </summary>
    private static Category GlobalRules()
    {
        var cols = new List<Col>
        {
            new("label", "规则", Primary: true),
            // 「数值」列 = perks.json 单例设置对象的某个字段（configScalar：cfg[_configId] 即值）。
            // 只有真外置进 perks.json 的行带 _configId（读书/护士基线三条）；其余仍是代码常量、无 _configId ⇒ wiki-serve 自动跳过。
            new("value", "数值", "number", ConfigScalar: true),
            new("unit", "单位", "chip", Hint: "这个数字的单位。只是给人看的标签。"),
            new("note", "说明", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        // mod = 当前所在模块（页内分块 group）：每个 —— 段开头设一次，Add 闭包按引用取用。
        string mod = "";
        // cfgKey 非空 ⇒ 这一行外置在 config（默认表级 perks.json；cfgFile 覆盖到别的源，如 health.json/hunger.json…）；
        // pct=true ⇒ 显示值是 config 分数 *100（wiki-serve 双向 /100/*100）；cfgFile ⇒ 行级 _configFile 覆盖表级；
        // cfgRoot ⇒ 行级 _configRoot：条目字典不在 config 顶层，而在 cfg[cfgRoot] 嵌套子对象下（如 body.json 的 Disability 段）。
        void Add(string id, string label, double value, string unit, string note, string anchor,
                 string? cfgKey = null, bool pct = false, string? cfgFile = null, string? cfgRoot = null)
        {
            var row = new Dictionary<string, object?>
            {
                ["group"] = mod,
                ["label"] = label, ["value"] = Round(value), ["unit"] = unit, ["note"] = note,
                ["_id"] = id, ["_anchor"] = anchor, ["_icon"] = "",
            };
            if (cfgKey is not null) row["_configId"] = cfgKey;
            if (pct) row["_configPercent"] = true;
            if (cfgFile is not null) row["_configFile"] = cfgFile;
            if (cfgRoot is not null) row["_configRoot"] = cfgRoot;
            rows.Add(row);
        }

        mod = "读书";
        // —— 读书（全员，不是谁的专属）—— 外置 perks.json ——
        Add("read_no_seat", "没座位读书的速度", Math.Round(ReadingSpeed.NoSeatMultiplier * 100, 4), "%",
            "站着/蹲着读书比坐着慢。**这是所有人都一样的**——不是哪个角色的专属效果。座位家具（板凳/木椅）就是为它存在的。",
            "godot/scripts/SurvivorPerks.cs :: ReadingSpeed.NoSeatMultiplier", cfgKey: "ReadingNoSeatMultiplier", pct: true);
        Add("read_no_prereq", "没读前置书就读它的速度", Math.Round(ReadingSpeed.MissingPrerequisiteMultiplier * 100, 4), "%",
            "跳级去啃进阶书：读得完，但慢到离谱（耗时 5 倍）。**不禁止，只是让你自己算这笔账。**",
            "godot/scripts/SurvivorPerks.cs :: ReadingSpeed.MissingPrerequisiteMultiplier",
            cfgKey: "ReadingMissingPrerequisiteMultiplier", pct: true);

        mod = "持握";
        // —— 持握（全员）——
        Add("dual_wield_speed", "双持时的攻速", Math.Round(DualWield.AttackSpeedFactor * 100, 4), "%",
            "两只手各拿一把单手武器时，**每只手**都变慢。两把一起打的总输出仍然更高——代价是精度和这个攻速。",
            "src/DeadSignal.Combat/DualWield.cs :: DualWield.AttackSpeedFactor");
        Add("dual_wield_spread", "双持时远程的散布", Math.Round(DualWield.RangedSpreadFactor, 4), "*",
            "双持开枪更不准（误差角乘这个数）。近战不受影响——近战本来就必中。",
            "src/DeadSignal.Combat/DualWield.cs :: DualWield.RangedSpreadFactor");

        mod = "医疗";
        // —— 医疗（全员基线）——
        Add("surgery_base_default", "常人的手术基础点数", NightingalePerk.DefaultSurgeryBasePoints, "点",
            "**所有人都能做手术，不看技能**——人人自带这些点。⚠️ 这是**全员基线**，不是南丁格尔的特长；"
            + "她的特长是「她本人的基础点更高」和「3 级给全营加点」，那两条在「角色数值」里。",
            "godot/scripts/SurvivorPerks.cs :: NightingalePerk.DefaultSurgeryBasePoints",
            cfgKey: "NightingaleDefaultSurgeryBasePoints");   // 原始点数，非比例 ⇒ 不 pct

        mod = "掩体";
        // —— 掩体（全员，含敌人）——
        Add("cover_chance", "半身掩体挡下整发的概率", Math.Round(CoverLogic.DefaultCoverChance * 100, 4), "%",
            "贴着沙袋/椅子/围栏时，远程有这个概率**整发打空**（不是减伤）。⚠️ **双向对称**：劫掠者躲在你的沙袋后面，你打它也吃这一下。绕到侧后就白躲。",
            "godot/scripts/CoverLogic.cs :: CoverLogic.DefaultCoverChance");
        Add("cover_radius", "贴多近才算在掩体后", CoverLogic.AdjacencyRadius, "像素",
            "身体要**贴上去**才算——站在掩体附近不算。",
            "godot/scripts/CoverLogic.cs :: CoverLogic.AdjacencyRadius");

        mod = "流血";
        // —— 流血（谁流得快、谁流得死）——
        // ⚠️ 这里必须 Round 到 **4** 位（同下面几行的兄弟项）：引擎里的真值是 1/3 ＝ 33.3333…%，
        // 舍到 1 位会种下 33.3，而 TableMerge 是按 4 位精度比对的（Program.Round）⇒ 表里填真值 33.3333 就永远
        // 报一条「表 33.3333 ≠ 代码 33.3」的**假漂移，且怎么改都消不掉**（用户改表 → 表赢 → 下次再报）。
        // 这正是 TableMerge 顶上那条注释警告过的病：「序列化四舍五入了、比较却没有」。种子与比对必须同精度。
        Add("bleed_zombie_rate", "丧尸的流血速度", Math.Round(BleedModel.ZombieBleedRateMultiplier * 100, 4), "%",
            "**丧尸只按常人的这个比例流血**——行尸走肉，血液循环本就不像活人。"
            + "⚠️ 这个数是**锐器强度的总闸门**：调高它，砍两刀站着等丧尸流干就行，伤害和护甲会一起失去意义；"
            + "调低它，匕首/刺剑这种「靠放血赢」的武器会直接废掉（它们对丧尸的胜利几乎全来自失血）。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.ZombieBleedRateMultiplier");
        Add("bleed_weight_lethal", "大伤口的流血速度", Math.Round(BleedModel.RateWeightOf(BleedTier.Lethal) * 100, 4), "%",
            "**躯干/头/颈/手臂/大腿**——只有这些大部位的伤口能把人**放干致死**。基准 100%。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.RateWeightOf");
        Add("bleed_weight_minor", "手脚伤口的流血速度", Math.Round(BleedModel.RateWeightOf(BleedTier.Minor) * 100, 4), "%",
            "**手/脚**的伤口流得慢，而且**永远流不死**（见下面那条下限）——只会溃烂感染。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.RateWeightOf");
        Add("bleed_weight_micro", "指/趾/眼/面/耳伤口的流血速度", Math.Round(BleedModel.RateWeightOf(BleedTier.Micro) * 100, 4), "%",
            "**擦伤级**的小口子，流得极慢，同样**永远流不死**。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.RateWeightOf");
        Add("bleed_nonlethal_floor", "小伤口最多把血抽到", Math.Round(BleedModel.NonLethalBloodFloorRatio * 100, 4), "%",
            "手/脚/指的伤口**只能把储血抽到这条线为止**——抽不到昏迷线（25%），更抽不到 0。"
            + "这就是「小伤口不致命」的硬保证：它们让你虚弱，但永远不是压死你的最后一根稻草。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.NonLethalBloodFloorRatio");
        Add("bleed_medium_threshold", "一刀打掉部位血量的多少就变中流血", Math.Round(BleedModel.MediumThreshold * 100, 4), "%",
            "**一次**伤害超过这个比例（按被打中那个部位的最大生命值算），口子就从小流血升成**中流血**。"
            + "护甲挡掉大半之后只渗进去一点点的剐蹭，比例很小 ⇒ 只算小流血 —— **甲厚就流得少，是这么来的**。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.MediumThreshold");
        Add("bleed_large_threshold", "一刀打掉部位血量的多少就变大流血", Math.Round(BleedModel.LargeThreshold * 100, 4), "%",
            "**一次**伤害超过这个比例 ⇒ 直接**大流血**（最高一级，再挨打也不会更高）。砍在没穿甲的身上就是这一档。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.LargeThreshold");
        Add("bleed_rate_small", "小流血的流速（相对普通伤口）", Math.Round(BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Small) * 100, 4), "%",
            "只要锐器进了肉就至少是小流血。它流得很慢 —— 挨一堆浅爪不会被放干。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.SeverityRateOf");
        Add("bleed_rate_medium", "中流血的流速（相对普通伤口）", Math.Round(BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Medium) * 100, 4), "%",
            "**两个小流血会合成一个中流血**（同一个部位再挨一刀，口子是接到一起的，不是多出一道）。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.SeverityRateOf");
        Add("bleed_rate_large", "大流血的流速（相对普通伤口）", Math.Round(BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Large) * 100, 4), "%",
            "**封顶的一级**：两个中流血、或一小一中，都会合成大流血；到了大流血就**不会再往上升**了。"
            + "一处大流血放干一个常人要一分钟左右，**两处就能在一场仗打完之前把人流死**。",
            "src/DeadSignal.Combat/BleedModel.cs :: BleedModel.SeverityRateOf");

        // ─────────────────────────── 波2：settings 入 wiki（外置 config，行级 _configFile 接双向）───────────────────────────
        // 值一律经 GameConfigCatalog/CombatCatalog 读**活配置**（单一事实源＝各 config json），不写死字面量。
        // 扁平 {字段:值} 的 config（hunger/horde/military/nightwatch/southtrial/health）可复用 value 列 configScalar +
        // 行级 _configFile 接双向；body 是嵌套（Disability 子对象），当前列级 root=顶层 ⇒ 只做只读展示（不带 _configId）。

        // —— 饥饿（hunger.json，惩罚是分数 ⇒ pct）——
        mod = "饥饿";
        var hunger = GameConfigCatalog.Section<HungerConfig>();
        Add("hunger_malnourished", "营养不良的能力惩罚", Math.Round(hunger.MalnourishedPenalty * 100, 4), "%",
            "最饿的一档：干活/战斗等能力打这么大的折扣（乘算）。饿肚子是硬代价。",
            "godot/scripts/HungerConfig.cs :: MalnourishedPenalty", cfgKey: "MalnourishedPenalty", pct: true, cfgFile: "hunger.json");
        Add("hunger_ravenous", "极度饥饿的能力惩罚", Math.Round(hunger.RavenousPenalty * 100, 4), "%",
            "第二档饿。能力乘这个折扣。", "godot/scripts/HungerConfig.cs :: RavenousPenalty",
            cfgKey: "RavenousPenalty", pct: true, cfgFile: "hunger.json");
        Add("hunger_hungry", "饥饿的能力惩罚", Math.Round(hunger.HungryPenalty * 100, 4), "%",
            "刚开始饿。能力乘这个折扣。", "godot/scripts/HungerConfig.cs :: HungryPenalty",
            cfgKey: "HungryPenalty", pct: true, cfgFile: "hunger.json");

        // —— 尸潮（horde.json，多为原始计数/间隔）——
        mod = "尸潮";
        var horde = GameConfigCatalog.Section<HordeConfig>();
        Add("horde_deadline_day", "尸潮总攻的天数", horde.DeadlineDay, "天",
            "撑到这一天，最终尸潮压境——主线的硬时限。", "godot/scripts/HordeConfig.cs :: DeadlineDay",
            cfgKey: "DeadlineDay", cfgFile: "horde.json");
        Add("horde_wave_base", "第一波的基础规模", horde.WaveBase, "只",
            "尸潮第一波的丧尸数基准。", "godot/scripts/HordeConfig.cs :: WaveBase",
            cfgKey: "WaveBase", cfgFile: "horde.json");
        Add("horde_wave_growth", "每波递增", horde.WaveGrowth, "只/波",
            "一波比一波多这么多。", "godot/scripts/HordeConfig.cs :: WaveGrowth",
            cfgKey: "WaveGrowth", cfgFile: "horde.json");
        Add("horde_wave_camp_factor", "营地人口对波规模的加成", horde.WaveCampFactor, "只/人",
            "营地每多一个人，波规模加这么多——树大招风。", "godot/scripts/HordeConfig.cs :: WaveCampFactor",
            cfgKey: "WaveCampFactor", cfgFile: "horde.json");
        Add("horde_wave_cap", "单波规模上限", horde.WaveCap, "只",
            "再怎么涨，一波也不会超过这个数。", "godot/scripts/HordeConfig.cs :: WaveCap",
            cfgKey: "WaveCap", cfgFile: "horde.json");
        Add("horde_wave_interval", "两波之间的间隔", horde.WaveInterval, "游戏小时",
            "清完这一波，隔这么久下一波来。", "godot/scripts/HordeConfig.cs :: WaveInterval",
            cfgKey: "WaveInterval", cfgFile: "horde.json");
        Add("horde_wave_clear_threshold", "算作「清波」的剩余数", horde.WaveClearThreshold, "只",
            "场上丧尸压到这个数以下，就算这一波清了，开始倒计时下一波。", "godot/scripts/HordeConfig.cs :: WaveClearThreshold",
            cfgKey: "WaveClearThreshold", cfgFile: "horde.json");
        Add("horde_max_concurrent_siege", "同时围城的丧尸上限", horde.MaxConcurrentSiege, "只",
            "同一时刻场上最多这么多围城丧尸（性能/难度双闸）。", "godot/scripts/HordeConfig.cs :: MaxConcurrentSiege",
            cfgKey: "MaxConcurrentSiege", cfgFile: "horde.json");

        // —— 军队（military.json）——
        mod = "军队";
        var military = GameConfigCatalog.Section<MilitaryConfig>();
        Add("military_raid_delay_days", "军队袭营的延迟天数", military.MilitaryRaidDelayDays, "天",
            "触发条件满足后，军队隔这么多天才真的打上门。", "godot/scripts/MilitaryConfig.cs :: MilitaryRaidDelayDays",
            cfgKey: "MilitaryRaidDelayDays", cfgFile: "military.json");

        // —— 夜袭潜行（nightwatch.json，权重/半径/倍率，多为原始值）——
        mod = "夜袭潜行";
        var nw = GameConfigCatalog.Section<NightWatchConfig>();
        Add("nw_stealth_darkness_weight", "潜行力·黑暗权重", nw.StealthDarknessWeight, "权重",
            "越黑越难被发现——这个权重决定黑暗对潜行力的贡献。", "godot/scripts/NightWatchConfig.cs :: StealthDarknessWeight",
            cfgKey: "StealthDarknessWeight", cfgFile: "nightwatch.json");
        Add("nw_stealth_apparel_weight", "潜行力·着装权重", nw.StealthApparelWeight, "权重",
            "穿得越隐蔽越难被发现。", "godot/scripts/NightWatchConfig.cs :: StealthApparelWeight",
            cfgKey: "StealthApparelWeight", cfgFile: "nightwatch.json");
        Add("nw_stealth_distance_weight", "潜行力·距离权重", nw.StealthDistanceWeight, "权重",
            "离得越远越难被发现。", "godot/scripts/NightWatchConfig.cs :: StealthDistanceWeight",
            cfgKey: "StealthDistanceWeight", cfgFile: "nightwatch.json");
        Add("nw_stealth_distance_reference", "距离权重的参考距离", nw.StealthDistanceReference, "像素",
            "距离潜行力按这个参考距离归一。", "godot/scripts/NightWatchConfig.cs :: StealthDistanceReference",
            cfgKey: "StealthDistanceReference", cfgFile: "nightwatch.json");
        Add("nw_stealth_cover_weight", "潜行力·掩体权重", nw.StealthCoverWeight, "权重",
            "贴着掩体潜行更不易被发现。", "godot/scripts/NightWatchConfig.cs :: StealthCoverWeight",
            cfgKey: "StealthCoverWeight", cfgFile: "nightwatch.json");
        Add("nw_vision_weight", "守夜·视觉权重", nw.VisionWeight, "权重",
            "守夜者靠看发现入侵者的权重。", "godot/scripts/NightWatchConfig.cs :: VisionWeight",
            cfgKey: "VisionWeight", cfgFile: "nightwatch.json");
        Add("nw_hearing_weight", "守夜·听觉权重", nw.HearingWeight, "权重",
            "守夜者靠听发现入侵者的权重。", "godot/scripts/NightWatchConfig.cs :: HearingWeight",
            cfgKey: "HearingWeight", cfgFile: "nightwatch.json");
        Add("nw_hearing_base_range", "守夜·基础听觉半径", nw.HearingBaseRange, "像素",
            "守夜者能听到多远的动静（基准）。", "godot/scripts/NightWatchConfig.cs :: HearingBaseRange",
            cfgKey: "HearingBaseRange", cfgFile: "nightwatch.json");
        Add("nw_preemptive_strike_mult", "先手偷袭伤害倍率", nw.PreemptiveStrikeMultiplier, "*",
            "潜行成功先动手，这一下伤害乘这个数。", "godot/scripts/NightWatchConfig.cs :: PreemptiveStrikeMultiplier",
            cfgKey: "PreemptiveStrikeMultiplier", cfgFile: "nightwatch.json");
        Add("nw_silent_theft_min", "无声窃取件数下限", nw.SilentTheftMinUnits, "件",
            "潜行偷东西一次至少拿走这么多。", "godot/scripts/NightWatchConfig.cs :: SilentTheftMinUnits",
            cfgKey: "SilentTheftMinUnits", cfgFile: "nightwatch.json");
        Add("nw_silent_theft_max", "无声窃取件数上限", nw.SilentTheftMaxUnits, "件",
            "潜行偷东西一次最多拿走这么多。", "godot/scripts/NightWatchConfig.cs :: SilentTheftMaxUnits",
            cfgKey: "SilentTheftMaxUnits", cfgFile: "nightwatch.json");

        // —— 南境审判（southtrial.json）——
        mod = "南境审判";
        var south = GameConfigCatalog.Section<SouthTrialConfig>();
        Add("south_pass_threshold", "南境审判通过所需票数", south.PassThreshold, "票",
            "南境审判要凑够这么多票才算过。", "godot/scripts/SouthTrialConfig.cs :: PassThreshold",
            cfgKey: "PassThreshold", cfgFile: "southtrial.json");

        // —— 致残（body.json 的嵌套 Disability 子对象，接双向：行级 _configRoot="Disability" 下钻 + 分数 *100 ⇒ pct）——
        mod = "致残";
        var disability = CombatCatalog.Section<BodyConfig>().Disability;
        Add("body_single_limb_penalty", "断一手/一腿的能力折损", Math.Round(disability.SingleLimbPenalty * 100, 4), "%",
            "少一整条手臂或腿，相关能力打这么大的折扣（乘算）。",
            "src/DeadSignal.Combat/BodyConfig.cs :: BodyDisability.SingleLimbPenalty（body.json）",
            cfgKey: "SingleLimbPenalty", pct: true, cfgFile: "body.json", cfgRoot: "Disability");
        Add("body_finger_penalty", "每断一根手指的能力折损", Math.Round(disability.FingerPenalty * 100, 4), "%",
            "同一只手每少一根手指累加这么多折扣（缺两指的山姆＝0.86）。",
            "src/DeadSignal.Combat/BodyConfig.cs :: BodyDisability.FingerPenalty（body.json）",
            cfgKey: "FingerPenalty", pct: true, cfgFile: "body.json", cfgRoot: "Disability");
        Add("body_toe_penalty", "每断一根脚趾的能力折损", Math.Round(disability.ToePenalty * 100, 4), "%",
            "同一只脚每少一根脚趾累加这么多折扣。",
            "src/DeadSignal.Combat/BodyConfig.cs :: BodyDisability.ToePenalty（body.json）",
            cfgKey: "ToePenalty", pct: true, cfgFile: "body.json", cfgRoot: "Disability");

        // —— 感染（health.json，连乘竞速链；扁平顶层字段接双向，嵌套/派生的做只读展示）——
        mod = "感染";
        var health = GameConfigCatalog.Section<HealthConfig>();
        Add("infection_chance_large", "大伤口感染几率", Math.Round(health.InfectionBaseChanceLarge * 100, 4), "%",
            "**躯干/头/颈/大部位**的伤口，不处理时染上感染的基础几率。感染是要靠手术/药竞速压下去的。",
            "godot/scripts/HealthConfig.cs :: InfectionBaseChanceLarge", cfgKey: "InfectionBaseChanceLarge", pct: true, cfgFile: "health.json");
        Add("infection_chance_medium", "中伤口感染几率", Math.Round(health.InfectionBaseChanceMedium * 100, 4), "%",
            "中等伤口的基础感染几率。", "godot/scripts/HealthConfig.cs :: InfectionBaseChanceMedium",
            cfgKey: "InfectionBaseChanceMedium", pct: true, cfgFile: "health.json");
        Add("infection_chance_small", "小伤口感染几率", Math.Round(health.InfectionBaseChanceSmall * 100, 4), "%",
            "小伤口的基础感染几率。", "godot/scripts/HealthConfig.cs :: InfectionBaseChanceSmall",
            cfgKey: "InfectionBaseChanceSmall", pct: true, cfgFile: "health.json");
        Add("infection_operated_factor", "处理过伤口的感染几率倍率", health.OperatedInfectionFactor, "*",
            "上过手术/清创的伤口，感染几率乘这个数（<1 ⇒ 处理能压低感染）。", "godot/scripts/HealthConfig.cs :: OperatedInfectionFactor",
            cfgKey: "OperatedInfectionFactor", cfgFile: "health.json");
        Add("infection_immune_window_factor", "免疫窗内的感染几率倍率", health.ImmuneWindowInfectionFactor, "*",
            "免疫力满值后的窗口期内，新伤口感染几率乘这个数——近乎免疫。", "godot/scripts/HealthConfig.cs :: ImmuneWindowInfectionFactor",
            cfgKey: "ImmuneWindowInfectionFactor", cfgFile: "health.json");
        Add("infection_immune_window_days", "免疫满后的免疫窗时长", health.ImmuneWindowDays, "天",
            "免疫力打满后，这段时间内新伤口几乎不感染；过了窗口（＝这么多天，1 天＝24 小时）就清空、恢复常态。",
            "godot/scripts/HealthConfig.cs :: ImmuneWindowDays", cfgKey: "ImmuneWindowDays", cfgFile: "health.json");
        Add("infection_worsen_per_day", "感染每日恶化速度", Math.Round(health.InfectionWorsenPerDay * 100, 2), "%/天",
            "不压制的话，感染严重度每天涨这么多（＝ 1/6，约 6 天走满）——竞速的另一头。"
            + "⚠️ 只读展示：真值 1/6 是无限循环小数，过不了百分比往返，改它请直接改 health.json 的 InfectionWorsenPerDay。",
            "godot/scripts/HealthConfig.cs :: InfectionWorsenPerDay（health.json，只读展示）");
        // —— 感染·派生/嵌套（只读展示，不接双向）——
        Add("infection_herbal_bandage", "草药绷带的感染几率倍率", 0.75, "*",
            "用草药绷带包扎，该处感染几率乘 0.75。⚠️ 只读展示：真值在 health.json 的嵌套 SurgerySupplies.herbal_bandage.InfectionChanceMultiplier（「医疗与草药」表可编辑）。",
            "godot/scripts/HealthConfig.cs :: SurgerySupplies[herbal_bandage].InfectionChanceMultiplier（body 展示）");
        Add("infection_nurse_cumulative", "南丁格尔满级后的全营感染几率", 0.765, "*",
            "护士 2 级(*0.9)＋3 级(*0.85)叠加 ⇒ 全营新伤口感染几率乘 0.765。⚠️ 只读展示：两档真值在「角色数值」南丁格尔行。",
            "godot/scripts/SurvivorPerks.cs :: NightingalePerk.Level2/Level3InfectionReduction（派生展示）");

        return new Category("global-rules", "全局规则",
            "src/DeadSignal.Combat/DualWield.cs · BleedModel.cs · godot/scripts/SurvivorPerks.cs · CoverLogic.cs · 各 config json（hunger/horde/military/nightwatch/southtrial/health/body）",
            "**对所有人一体适用**的规则——不属于任何一件武器，也不属于任何一个角色。按模块分块：读书/持握/医疗/掩体/流血，"
            + "以及外置进 config 的 饥饿/尸潮/军队/夜袭潜行/南境审判/致残/感染。"
            + "以前这些数只活在代码里，wiki 上看不到、也改不了。"
            + "⚠️ 别把这里的东西误当成谁的专属效果：「没座位读书慢 10%」是**每个人**都一样的，不是诺蒂的技能。",
            // 表级 perks.json：仅带 _configId 的行（读书两条+护士基线一条，已外置进 perks.json）双向；
            // 双持/掩体/流血等仍是代码常量、无 _configId ⇒ wiki-serve 只读它们的展示值、不投影。
            cols, rows, ConfigFile: "perks.json");
    }

    /// <summary>
    /// **结局** —— 全作四条结局骨架的**内部设计参考表**（⚠️ 含全作最大剧透，按 README 禁剧透纪律**只进内部 wiki**）。
    ///
    /// <para><b>authored 纪律</b>：本表只如实反映代码里已有的事实（<see cref="EndingCg"/> 的分段 CG 文本、
    /// <see cref="SouthEscapeEnding"/> 的南逃谢幕序列、<see cref="FamilyEscapeWin"/> 的 WIN 序列）——
    /// 不引申、不自造剧情。CG 摘要一律**逐屏抄** EndingCg 的字段（每段一屏），标注 draft 待 author 润色。</para>
    ///
    /// <para><b>触发条件</b>只作文字描述：具体数值（南境审判通过票数 / 军队袭营延迟天数 / 尸潮总攻天数）
    /// 已在「全局规则」表外置进 config，本表不重复外置——所以**不接双向**（无 _configId / configFile）。</para>
    /// </summary>
    private static Category EndingTable()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            new("kind", "种类", "chip", Hint: "好结局 WIN / 坏结局 / 失败路由（把玩家推向坏结局的中间态，本身非终局）。"),
            new("trigger", "触发条件", "longtext",
                Hint: "怎么走到这条结局（文字描述）。具体数值（南境通过票数/军队延迟天数/尸潮总攻天数）见「全局规则」表，这里不重复。"),
            new("cg", "CG 摘要（draft）", "longtext",
                Hint: "逐屏抄自代码 EndingCg 的分段 CG 文本（每段一屏渐显）。**draft 占位草稿·待 author 润色**——正史事实不改。"),
            new("_id", "内部 id", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        // 逐屏 CG 文本 → 一格（每段一屏渐显；如实抄 EndingCg 字段，不改一字）。draft 前缀提醒待润色。
        static string Screens(params IReadOnlyList<string>[] acts)
            => "【draft·逐屏文本待 author 润色】" + string.Join("　‖　",
                acts.Select(a => string.Join(" / ", a)));

        var rows = new List<Dictionary<string, object?>>();
        void Add(string id, string name, string kind, string trigger, string cg, string anchor)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = name, ["kind"] = kind, ["trigger"] = trigger, ["cg"] = cg,
                ["_id"] = id, ["_anchor"] = anchor, ["_icon"] = "",
            });
        }

        // 🟢 好结局 WIN：举家南逃（FamilyEscapeWin.WinCg = 启程行军旁白 + 峡谷前被迎接的胜利段）。
        Add("family-escape-win", "举家南逃（好结局 WIN）", "好结局 WIN",
            "联系南方 → 南境审判三问全部答满、总分达到通过线 → 回营电台确认启程 → 全营列队南逃 → "
            + "抵达峡谷前，对方大桥**落下**、有人迎接，全员过桥无一落下 → 过渡到第二幕「峡谷营地」（很久远排期）。"
            + "（通过所需票数见「全局规则·南境审判」。）",
            Screens(EndingCg.FamilyDepartureNarration, EndingCg.FamilyEscapeWin),
            "godot/scripts/FamilyEscapeWin.cs :: FamilyEscapeWin.WinCg（EndingCg.FamilyDepartureNarration + FamilyEscapeWin）");

        // 🔴 坏结局·军袭南逃：回复军方 → 军人屠营 → 随机一人半残南逃 → 峡谷谢幕。CG-A 屠营旁白 + CG-B 峡谷谢幕。
        Add("military-raid-south-escape", "军袭·单角色南逃（坏结局）", "坏结局",
            "回复军方求救 → 延迟数天后（第 n+2 天）白天，军人带顶级装备屠尽全营 → 随机一名半残幸存者向南逃 → "
            + "峡谷前谢幕（大桥**未落**、两哨兵冷眼看着）。军方为何屠杀全篇不解释（留白）。"
            + "（军队袭营延迟天数见「全局规则·军队」。）",
            Screens(EndingCg.MilitaryRaidMassacre, EndingCg.SouthEscapeFarewell),
            "godot/scripts/EndingCg.cs :: MilitaryRaidMassacre（CG-A）+ SouthEscapeFarewell（CG-B）；序列 SouthEscapeEnding（MilitaryRaid）");

        // 🔴 坏结局·第 40 天尸潮南逃：与军袭复用同一「南逃谢幕」序列，施暴方换成尸潮。CG-A 为冻结演出无独立旁白定稿，CG-B 共用峡谷谢幕。
        Add("horde-siege-south-escape", "第 40 天尸潮·单角色南逃（坏结局）", "坏结局",
            "撑到第 40 天仍未南逃 → 无限尸潮踏平营地 → 随机一名幸存者半残向南逃 → 同款峡谷前谢幕。"
            + "与军袭结局**复用同一条「南逃谢幕」序列**（SouthEscapeEnding），只是施暴方换成尸潮。"
            + "（尸潮总攻天数见「全局规则·尸潮」。）",
            // CG-A＝尸潮屠营的冻结演出（无独立旁白定稿）；CG-B 复用峡谷谢幕。旧 CG① 阴云文本为遗留可玩全灭路线、第 40 天已不再走。
            "【draft·逐屏文本待 author 润色】CG-A＝尸潮屠营的冻结演出（无独立旁白定稿）　‖　"
            + "CG-B（峡谷谢幕·与军袭复用）：" + string.Join(" / ", EndingCg.SouthEscapeFarewell)
            + "　‖　〔遗留〕旧 CG①（可玩全灭路线·第 40 天已不再命中）：" + string.Join(" / ", EndingCg.HordeSiege),
            "godot/scripts/SouthEscapeEnding.cs :: SouthEscapeTrigger.HordeSiege；CG-B EndingCg.SouthEscapeFarewell；遗留 EndingCg.HordeSiege");

        // ⚪ 失败路由（非终局）：南境审判没过 → 解锁回复军方 → 最终落进军袭/40 天坏结局。
        Add("south-trial-failure", "南方失败（失败路由·非终局）", "失败路由",
            "南境审判三问总分未达通过线 → 不通过 → 解锁「回复军方」选项 → 最终落进军袭或第 40 天坏结局。"
            + "**本身不是结局**，是把玩家推向坏结局的路由态。",
            "无独立 CG（路由态·非终局）。后续 CG 走军袭 / 第 40 天坏结局。",
            "godot/scripts/SouthTrial.cs（未过）→ RadioMainline.ReopenAfterSouthFailure（解锁回复军方）");

        return new Category("endings", "结局",
            "godot/scripts/EndingCg.cs · FamilyEscapeWin.cs · SouthEscapeEnding.cs · SouthTrial.cs · RadioMainline.cs",
            "⚠️ **内部设计参考 · 含全作最大剧透** —— 按 README 禁剧透纪律，本表**只进内部 wiki**，不得以任何形式对外/进 README。\n\n"
            + "全作四条结局的骨架总览：一条好结局 WIN（举家南逃）、两条坏结局（军袭南逃 / 第 40 天尸潮南逃，复用同一「南逃谢幕」序列）、"
            + "一条失败路由（南方没过 → 把玩家推向坏结局）。**CG 摘要逐屏抄自代码 EndingCg 的分段文本，全部为 draft 占位草稿，待 author 润色**；"
            + "触发条件里的数值（南境通过票数/军队延迟天数/尸潮总攻天数）已在「全局规则」表，这里只作文字描述、不重复外置。",
            cols, rows);
    }

    private static Category Furniture()
    {
        var cols = new List<Col>
        {
            new("name", "名称", Primary: true),
            // 「建造材料」是 dict{料→量}（furniture.json 的 cost 嵌套字典）——configDict 嵌套投影接双向：
            // wiki 串「木料*16、钉子*8」↔ config {"wood":16,"nails":8}，中文料名↔key 走 DictNameMap。
            new("materials", "建造材料", Hint: "格式：木料*16、钉子*8", ConfigKey: "cost", ConfigDict: true,
                DictNameMap: MaterialDictNameMap(
                    FurnitureBuildCost.All.Select(FurnitureBuildCost.Of)
                        .Where(c => c is not null).SelectMany(c => c!.Keys))),
            new("buildMinutes", "建造工时", "hours", Hint: "有人在营地里干这么久（游戏内时间）。", ConfigKey: "buildMinutes"),
            new("salvage", "拆了能还回多少", ReadOnly: true,
                Hint: "自动算的：通用规则是还一半（向下取整），木材例外——一半变木料、一半变废木料。要改 ⇒ 改「建造材料」。（想改返还比例本身，那是引擎规则，跟 agent 说。）"),
            // 玩家在游戏里看到的短简介：代码里已有字段（FurnitureBuildCost.Description(key)），复用「简介」列（同 Books()）。
            new("description", "简介", "longtext"),
            new("_id", "内部 id", Internal: true),
            new("_configId", "config 键", Internal: true),
            new("_anchor", "代码位置", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (string key in FurnitureBuildCost.All)
        {
            IReadOnlyDictionary<string, int>? cost = FurnitureBuildCost.Of(key);
            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = key,
                ["materials"] = cost is null ? "" : string.Join('、', cost.Select(kv => $"{MaterialName(kv.Key)}*{kv.Value}")),
                ["buildMinutes"] = FurnitureBuildCost.BuildMinutes(key),
                ["salvage"] = cost is null ? "" : string.Join('、', SalvageLogic.YieldOfFurniture(key).Select(kv => $"{MaterialName(kv.Key)}*{kv.Value}")),
                ["description"] = FurnitureBuildCost.Description(key),
                ["_id"] = key,
                ["_configId"] = key,   // furniture.json 的条目键就是家具中文名（工作台/床…），非 SnakeCase
                ["_anchor"] = "godot/scripts/FurnitureBuildCost.cs :: FurnitureBuildCost",
            });
        }
        return new Category("furniture", "家具建造",
            "godot/scripts/FurnitureBuildCost.cs",
            "营地里造得出、也拆得掉的东西。拆除返还向下取整——所以拆了再建，永远是亏的。",
            cols, rows, ConfigFile: "furniture.json");
    }

    // ─────────────────────────── 通用：反射遍历一张 catalog ───────────────────────────

    /// <summary>
    /// 遍历某个静态 catalog 类上「无参、返回 T」的公开静态方法 —— <b>这就是"可重跑"的来源</b>：
    /// 往 WeaponTable 里加一把新枪，重跑本工具它自己就出现在 JSON 里，不用改这里。
    /// 返回 (方法名, 实例)；方法名即内部 id，也是回写代码时的定位锚。
    /// </summary>
    private static IEnumerable<(string Member, T Value)> CatalogOf<T>(Type catalog) where T : class
    {
        foreach (MethodInfo m in catalog.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.ReturnType != typeof(T)) continue;
            if (m.GetParameters().Length != 0) continue;
            if (m.IsSpecialName) continue; // 属性 getter 之类
            if (m.Invoke(null, null) is T v) yield return (m.Name, v);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根（DeadSignal.sln）");
    }
}
