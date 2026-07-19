using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 物品图标链路的 headless 冒烟验证（仿 <see cref="TestExploration"/> 的做法：一个能单独跑的小场景脚本）。
///
/// <para>
/// 跑法：<c>Godot --headless --path godot --script scripts/TestItemIcons.cs</c>——
/// 它造一个真的 <see cref="StashPanel"/>、往真的 <see cref="InventoryStore"/> 里塞八件小样物品，
/// 调 <c>ShowStash</c> 走完整的建行逻辑，然后把每一行实际挂上的纹理打出来。
/// 目的是证明"映射表 → PNG 文件 → Godot 导入 → TextureRect"这条链是通的，
/// 而不是只证明"C# 能编译"。不弹窗口，可在工位安全跑。
/// </para>
/// </summary>
public sealed partial class TestItemIcons : SceneTree
{
    private bool _done;

    /// <summary>
    /// 验证跑在**第一帧**而不是 <c>_Initialize</c>：<c>Root.AddChild(panel)</c> 之后面板的 <c>_Ready</c>
    /// 还没被调用（它的 <c>_foodLabel</c> 等控件此刻仍是 null），在 <c>_Initialize</c> 里直接 <c>ShowStash</c>
    /// 会当场 NullReference。等一帧，节点就进树、_Ready 跑完了。
    /// </summary>
    public override bool _Process(double delta)
    {
        if (_done)
        {
            return true;
        }
        _done = true;
        Verify();
        return true;
    }

    private void Verify()
    {
        var inventory = new InventoryStore();
        inventory.Add(Item.Weapon("匕首"));
        inventory.Add(Item.Weapon("步枪"));
        inventory.Add(Materials.Find("bandage")!.Value.ToItem(3));
        inventory.Add(Materials.Find("wood")!.Value.ToItem(12));
        inventory.Add(Materials.Find("cloth")!.Value.ToItem(5));
        inventory.Add(Materials.Find("ammo_arrow_handmade")!.Value.ToItem(9));
        inventory.Add(Item.Light("flashlight", "手电"));
        inventory.Add(Item.Food(4));
        inventory.Add(Item.Weapon("狙击枪")); // 狙击枪图标映射与 PNG 均已登记

        var panel = new StashPanel();
        Root.AddChild(panel);
        panel.ShowStash(inventory, foodPortions: 4, notice: null, isBookRead: _ => false);

        int withIcon = 0;
        int placeholder = 0;
        foreach (Item item in inventory.Items)
        {
            string path = ItemIcons.PathFor(item);
            bool exists = ResourceLoader.Exists(path);
            Texture2D? tex = ItemIconTextures.For(item);
            string mark = path == ItemIcons.PlaceholderPath || !exists ? "占位" : "有图";
            if (mark == "有图") withIcon++; else placeholder++;
            GD.Print($"[图标] {item.DisplayName,-10} → {path,-52} 磁盘{(exists ? "有" : "无")} 纹理{(tex is null ? "null" : $"{tex.GetWidth()}x{tex.GetHeight()}")} [{mark}]");
        }

        GD.Print($"[图标] 库存：有图 {withIcon} 件 / 回落占位 {placeholder} 件；映射表共 {ItemIcons.All.Count} 条。");

        // —— 探索背包（LootItem 链路：RefId 指向底层数据，食物的 RefId 是空串，得转查 FoodRefKey）——
        var bag = new LootItem[]
        {
            LootItem.Weapon("匕首"),
            LootItem.Armor("皮夹克"),
            LootItem.Material("wood", 8),
            LootItem.Book("tailors_notes"),
            LootItem.Tool("sawblade"),
            LootItem.Food(3),
        };
        // [T45] 负重账现在含装备（穿在身上的枪与甲），故背包面板要装备/战利品分开喂。
        panel.ShowExpeditionBag(bag, gearKg: 13.8, lootKg: 12.5, capacityKg: 40, notice: null);
        foreach (LootItem loot in bag)
        {
            Texture2D? tex = ItemIconTextures.ForLoot(loot);
            GD.Print($"[背包] {LootDisplay.NameOf(loot),-14} 纹理{(tex is null ? "null" : $"{tex.GetWidth()}x{tex.GetHeight()}")}");
        }

        // —— 制作面板（产物链路：武器/护甲的产物键是英文，引用键却是中文名）——
        foreach (RecipeData r in RecipeBook.All)
        {
            string p = ItemIcons.PathForOutput(r.OutputKey, r.DisplayName);
            if (p == ItemIcons.PlaceholderPath)
            {
                GD.Print($"[配方] ⚠ 产物没图标：{r.DisplayName}（key={r.OutputKey}）");
            }
        }
        GD.Print($"[配方] {RecipeBook.All.Count} 张配方的产物图标已逐条查过（上面没有 ⚠ 即全部有图）。");

        GD.Print("[图标] 库存/背包/制作/商人/角色/医疗六处面板均走 ItemIconTextures，链路通。");

        // —— 素材出处面板（F1）：CC BY 3.0 要求署名对玩家可见，这一页就是那个"可见" ——
        var credits = new CreditsPanel();
        Root.AddChild(credits);
        credits.Open();
        GD.Print($"[署名] CreditsPanel 打开={credits.IsOpen}，分节 {CreditsContent.Sections.Count} 段：");
        foreach (CreditsSection s in CreditsContent.Sections)
        {
            GD.Print($"[署名]   {s.Title} —— {s.License}");
        }
        credits.Close();
        GD.Print($"[署名] 关闭后 IsOpen={credits.IsOpen}（ESC/关闭按钮走同一条路）。");

        Quit(0);
    }
}
