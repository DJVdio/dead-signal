using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 物品图标映射（<see cref="ItemIcons"/>）的护栏。
///
/// <para>
/// 这里**不断言"每一件物品都必须有图标"**：图标是表现层的事，缺一张不该把战斗规则的门禁打红，
/// 也不该在别人往武器表里加一把新枪时突然拦住他。缺失一律走占位图（见 <see cref="ItemIcons.PathFor(Item)"/>），
/// 缺了哪些用 <see cref="ItemIcons.MissingFor"/> 盘点——下面那条 <c>已知物品全表_当前全部有图标映射</c>
/// 用的就是它：它是**对账**，红了说明"新物品还没配图标"，改一行映射表即可，而不是逻辑坏了。
/// </para>
/// </summary>
public class ItemIconsTests
{
    /// <summary>库里现有的全部物品引用键（武器名/护甲名/材料键/光源键/书 id/家具产物键）。</summary>
    private static IEnumerable<string> AllItemRefKeys()
    {
        foreach (Weapon w in WeaponTable.Arsenal()) yield return w.Name;
        foreach (Weapon w in WeaponTable.ArcheryArsenal()) yield return w.Name;
        yield return "骨刀"; // 可制作武器，无 Weapon 工厂（见 WeaponTable.DescriptionOf 的注释）

        foreach (MaterialDef m in Materials.All) yield return m.Key;
        foreach (string k in DogGearCatalog.AllKeys) yield return k;
        foreach (BookData b in BookLibrary.All()) yield return b.Id;
    }

    [Fact]
    public void Slug_全表唯一且只用ASCII小写下划线()
    {
        var slugs = ItemIcons.All.Values.Select(d => d.Slug).ToList();

        Assert.Equal(slugs.Count, slugs.Distinct().Count());
        Assert.All(slugs, s => Assert.Matches("^[a-z0-9_]+$", s));
    }

    [Fact]
    public void 素材出处_形如作者斜杠图名()
    {
        // Source 是 game-icons 仓库里的 "作者/图名"（无扩展名）——build_icons.sh 直接拿它拼下载 URL。
        Assert.All(ItemIcons.All.Values, d => Assert.Matches("^[a-z0-9-]+/[a-z0-9-]+$", d.Source));
    }

    [Fact]
    public void 路径_按分区与slug拼出res路径()
    {
        Assert.Equal("res://assets/items/weapons/dagger.png", ItemIcons.PathFor("匕首"));
        Assert.Equal("res://assets/items/materials/wood.png", ItemIcons.PathFor("wood"));
        Assert.Equal("res://assets/items/lights/flashlight.png", ItemIcons.PathFor("flashlight"));
        Assert.Equal("res://assets/items/books/book_archery.png", ItemIcons.PathFor("way_of_bow_and_arrow"));
    }

    [Fact]
    public void 查不到的引用键_回退占位图而不是抛异常()
    {
        Assert.Equal(ItemIcons.PlaceholderPath, ItemIcons.PathFor("这东西不存在"));
        Assert.Equal(ItemIcons.PlaceholderPath, ItemIcons.PathFor((string?)null));
        Assert.Equal(ItemIcons.PlaceholderPath, ItemIcons.PathFor((Item)null!));
    }

    [Fact]
    public void 食物没有引用键_也能查到那张统一的口粮图标()
    {
        // Item.Food 的 RefKey 恒为 null（食物按份数计，不指向任何目录条目），
        // 若按 RefKey 查就会掉进占位图——PathFor(Item) 要认出食物类别，转查 FoodRefKey。
        Item food = Item.Food(3);
        Assert.Null(food.RefKey);
        Assert.Equal("res://assets/items/food/ration.png", ItemIcons.PathFor(food));
    }

    [Fact]
    public void 一件武器物品_查到它的武器图标()
    {
        Assert.Equal("res://assets/items/weapons/rifle.png", ItemIcons.PathFor(Item.Weapon("步枪")));
        Assert.Equal("res://assets/items/materials/bandage.png", ItemIcons.PathFor(Materials.Find("bandage")!.Value.ToItem(2)));
    }

    [Fact]
    public void 每张配方的产物_都有图标()
    {
        // 配方产物是新物品最常见的来路（家具/工作台/自制武器都从这儿来），且它的键与库存引用键**不是同一个**
        // （武器 bone_knife → 引用键「骨刀」），所以单查 AllItemRefKeys 抓不到它——这条断言专管这一路。
        var missing = RecipeBook.All
            .Where(r => ItemIcons.PathForOutput(r.OutputKey, r.DisplayName) == ItemIcons.PlaceholderPath)
            .Select(r => $"{r.DisplayName}(key={r.OutputKey})")
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"这些配方产物还没配图标（在 godot/scripts/ItemIcons.cs 的映射表里各加一行，再跑 tools/icons/build_icons.sh）：{string.Join("、", missing)}");
    }

    [Fact]
    public void 已知物品全表_当前全部有图标映射()
    {
        IReadOnlyList<string> missing = ItemIcons.MissingFor(AllItemRefKeys());

        Assert.True(
            missing.Count == 0,
            $"这些物品还没配图标（在 godot/scripts/ItemIcons.cs 的映射表里各加一行，再跑 tools/icons/build_icons.sh）：{string.Join("、", missing)}");
    }

    [Fact]
    public void 已登记图标_Png文件全部存在且为32像素()
    {
        foreach (IconDef def in ItemIcons.All.Values)
        {
            string path = Path.Combine(RepoRoot(), "godot", "assets", "items", def.Category, def.Slug + ".png");
            Assert.True(File.Exists(path), path);
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            stream.Position = 16;
            Assert.Equal(32, ReadBigEndianInt32(reader));
            Assert.Equal(32, ReadBigEndianInt32(reader));
        }
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (System.BitConverter.IsLittleEndian)
            System.Array.Reverse(bytes);
        return System.BitConverter.ToInt32(bytes, 0);
    }

    private static string RepoRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
