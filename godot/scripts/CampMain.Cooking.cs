using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

// [批次21·T14] 烹饪机制的营地接线（CampMain 的 partial，同 CampMain.Save.cs 的做法）。
//
// 规则本身全在纯逻辑 CookingLogic.cs 里（热量点表 / 份数通式 / 炊具减免 / 门槛）；本文件只做**空间与执行**：
// 砌灶（实心 + 挖导航洞 + 重烘焙）、开面板、扣料下单、工时完工把份数加进 CampResources.Food。
//
// ⚠️⚠️ **信息隐藏**：食材几点热量、一份饭要几点、还差多少、浪费了多少 —— 玩家**一个数字都看不到**。
// 他能看到的只有产物那行的「食物 ×N」和"按钮点不点得动"。本文件里凡是要 Show 给玩家的字，
// 都先过 CookingLogic.PlayerFacingText（它对"热量不够"刻意返回 null ＝ 沉默）。别在这儿另起 toast 解释。

public sealed partial class CampMain : Node2D
{
    // ---------------- 烹饪（批次21·T14）----------------
    //
    // 烹饪台 = **固定位置**设施（用户拍板：「改装台、烹饪台不允许跨越，但他们是营地内固定位置…烹饪台放在厨房」）。
    // 在工作台上造出来 → 完工**不进库存**，直接砌在厨房锚点（CookStation.AnchorX/Y，＝住宅西侧那个角）。
    // 玩家**没有"放置"这个动作** ⇒ 不接 PlacementRules 的运行时校验；锚点由 FixedFacilityAnchorTests 做**设计期**自检
    //（它实心、挖导航洞、玩家挪不动 ⇒ 锚点若压进禁建带就是一条永远纠正不了的死路）。

    /// <summary>全营那座烹饪台的炊具装配态（锅 / 烤架；field 初始化，早于读档就绪）。</summary>
    private readonly CookStationState _cookStation = new();

    private CookingPanel _cookingPanel = null!;
    private bool _cookingOpen;          // 烹饪面板是否开着（与其它模态一样持有时标冻结）
    private int _prevCookingSpeed;      // 开面板前的时钟速度档（关闭时还原）
    private bool _prevCookingPaused;    // 开面板前世界是否暂停（还原保真）

    /// <summary>营地里有没有一座烹饪台（做饭的唯一场所；拆了就没了）。</summary>
    private bool HasCookStation => _furniture.ContainsKey(CookStation.PropName);

    /// <summary>烹饪台的固定锚点矩形（厨房＝住宅西侧那个角）。</summary>
    private static Rect2 CookStationAnchorRect => new(
        CookStation.AnchorX, CookStation.AnchorY, CookStation.Width, CookStation.Height);

    /// <summary>建面板 + 接事件（在 _Ready 里调，同其它面板）。</summary>
    private void SetupCookingPanel()
    {
        _cookingPanel = new CookingPanel { Layer = 20 };
        AddChild(_cookingPanel);
        _cookingPanel.Visible = false;
        _cookingPanel.CookRequested += OnCookRequested;
        _cookingPanel.CookwareInstallRequested += OnCookwareInstall;
        _cookingPanel.CookwareRemoveRequested += OnCookwareRemove;
        _cookingPanel.Closed += CloseCooking;
    }

    // ================= 建造：配方完工 → 在厨房锚点砌一座灶 =================

    /// <summary>
    /// 配方「烹饪台」完工：直接在厨房的固定锚点砌起来（**不进库存**——一座砌好的灶揣不进兜里）。
    /// </summary>
    private void CompleteCookStationBuild()
    {
        if (HasCookStation)
        {
            return;   // 一座就够（配方本来就被 CookStation.AbsentGate 灰掉了，此为双保险）
        }

        SpawnCookStation(CookStationAnchorRect);
        RebakeNavigation();   // 它实心、挖了导航洞、不可跨越 —— 寻路图得知道
        _campToast.Show("灶砌好了，就在住宅的厨房那头。从今往后，生的东西有地方变熟。", CampToast.Ok);
        GD.Print($"[烹饪台] 完工，落于厨房锚点 {CookStationAnchorRect.Position}");
    }

    /// <summary>
    /// 在 <paramref name="rect"/> 砌起烹饪台的实体（碰撞 + 视觉 + 导航洞 + 可拆登记 + 可点击容器）。
    /// **实心**（与工作台/改装台同构，和沙袋相反——沙袋刻意不建碰撞、不挖洞）。
    /// 供**完工建造**与**读档复原**共用（读档的导航重烘焙由 RestorePlacedFurniture 统一做）。
    /// </summary>
    private void SpawnCookStation(Rect2 rect)
    {
        string key = CookStation.PropName;
        var style = new PixelStyle { color = new[] { 0.30, 0.26, 0.24 }, jitter = 0.14 };  // 熏黑的灶
        var visuals = new List<Node2D>();

        StaticBody2D body = AddSolid(rect, style, seed: 29, (float)_heights.prop, cell: 200f, visuals);
        _furniture[key] = new FurnitureInstance { Rect = rect, Body = body, Visuals = visuals };

        // 可点击：选中角色右键前往 → 开烹饪面板（见 ExecuteContainerInteract 的 "cookstation" 分支）。
        _containers.Add(new ContainerRef { Name = key, Rect = rect, Role = "cookstation" });
    }

    // ================= 面板 =================

    /// <summary>开（或刷新）烹饪面板：首次打开冻结时标。</summary>
    private void OpenCooking()
    {
        if (!_cookingOpen)
        {
            CapturePanelTimeState(out _prevCookingSpeed, out _prevCookingPaused);
            _cookingOpen = true;
        }
        RefreshCooking();
        _cookingPanel.Visible = true;
    }

    /// <summary>重刷烹饪面板（下单/装卸炊具/完工后调）。</summary>
    private void RefreshCooking()
        => _cookingPanel.ShowFor(
            _cookStation,
            ControllableCrafters(),   // 掌勺的人：同制作，取存活且可控的幸存者
            _inventory,
            HasCookStation,
            JobAt(FacilityJobKeys.MainCookStation));

    private void CloseCooking()
    {
        _cookingPanel.Visible = false;
        _cookingOpen = false;
        _campToast.Hide();
        RestorePanelTimeState(_prevCookingSpeed, _prevCookingPaused);
    }

    // ================= 炊具：装 / 卸 =================

    /// <summary>装一件炊具进烹饪台的槽（从库存扣一件）。每装一件，每份饭省点热量——<b>但这个数不告诉玩家</b>。</summary>
    private void OnCookwareInstall(CookwareSlot slot)
    {
        if (!HasCookStation)
        {
            _campToast.Show("还没有灶，装了也没处放。", CampToast.Bad);
            return;
        }
        if (_cookStation.Has(slot))
        {
            return;   // 已经装了（按钮本来就该是「卸下」，此为双保险）
        }

        string itemKey = CookStation.ItemKeyOf(slot);
        if (!_inventory.TrySpendMaterial(itemKey, 1))
        {
            _campToast.Show($"库里没有{DisplayNames.Of(slot)}——先做一个出来。", CampToast.Bad);
            return;
        }

        _cookStation.Install(slot);
        // ⚠️ 只说"装上了"，**不说它省几点热量**——那是玩家该自己试出来的（多做几锅就知道了）。
        _campToast.Show($"{DisplayNames.Of(slot)}架上了。", CampToast.Ok);
        RefreshCooking();
        if (_stashOpen) OpenStash(null);
    }

    /// <summary>把一件炊具从烹饪台上卸下来（还回库存，可再装回去；拆灶不在此列）。</summary>
    private void OnCookwareRemove(CookwareSlot slot)
    {
        if (!_cookStation.Remove(slot))
        {
            return;   // 本来就没装
        }

        string itemKey = CookStation.ItemKeyOf(slot);
        string display = DisplayNames.Of(slot);
        _inventory.Add(Item.Material(itemKey, display, 1, CraftOutputFactory.Create(itemKey, 1).First().Description));
        _campToast.Show($"{display}取下来了。", CampToast.Ok);
        RefreshCooking();
        if (_stashOpen) OpenStash(null);
    }

    // ================= 下单：扣食材 → 起一条工时任务 =================

    /// <summary>
    /// 面板「开火」→ 判定（<see cref="CookingLogic.Plan"/>）→ **开工即扣食材**（锁定，同制作/拆解/改装的既有语义）
    /// → 起一条 <c>cook:&lt;份数&gt;</c> 的工时任务，挤进全营那条单任务队列。产出（份数）留待完工。
    ///
    /// <para>⚠️ <b>份数在下单这一刻就定死了</b>（编进任务 id）：干活干到一半有人把锅卸走，这一锅**不会**缩水——
    /// 料已经下进去了。这既符合直觉，也免掉一整类"结算时规则变了"的坑。</para>
    /// </summary>
    private void OnCookRequested(IReadOnlyDictionary<string, int> pot, Pawn cook)
    {
        if (!CanStartFacilityJob(FacilityJobKeys.MainCookStation, cook, out string busyWhy))
        {
            _campToast.Show(busyWhy, CampToast.Bad);
            RefreshCooking();
            return;
        }

        CookPlan plan = CookingLogic.Plan(
            HasCookStation, pot, _cookStation.Installed, _inventory.MaterialCount,
            BookPassiveEffects.FoodCaloriesReduction(cook.HasReadBook));

        if (!plan.CanCook)
        {
            // ★ 只说"说得出口"的原因（没灶 / 空锅 / 库里不够）；**热量不够时 PlayerFacingText 返回 null ⇒ 一声不吭**。
            //   这不是漏了提示，这是本机制的支点（用户拍板：热量点靠玩家试错）。
            string? why = CookingLogic.PlayerFacingText(plan.Blocks);
            if (why is not null)
            {
                _campToast.Show(why, CampToast.Bad);
            }
            GD.Print($"[烹饪] 下不了单：{string.Join("；", plan.Blocks.Select(b => b.DevDetail))}");   // 开发者日志才看得到热量账
            RefreshCooking();
            return;
        }

        // 开工即扣：锅里的东西全下进去（**零头静默浪费**——多出来的热量不返还、不提示、不入账）。
        foreach (KeyValuePair<string, int> kv in pot)
        {
            if (kv.Value <= 0) continue;
            if (!_inventory.TrySpendMaterial(kv.Key, kv.Value))
            {
                // Plan 刚放行过，理论上到不了这（并发/异常兜底）：不半扣，如实报。
                _campToast.Show("库里没有那么多。", CampToast.Bad);
                GD.Print($"[烹饪] 扣料失败：{kv.Key}×{kv.Value}");
                RefreshCooking();
                return;
            }
        }

        int minutes = CookingLogic.WorkMinutesFor(plan.Portions);
        StartFacilityJob(FacilityJobKeys.MainCookStation,
            new CraftingJob(CookingLogic.JobIdFor(plan.Portions), minutes), cook);
        _craftLastMinuteKey = -1;

        string work = CraftingPanelFormat.FormatWorkDuration(minutes);
        _campToast.Show($"下锅了：食物 ×{plan.Portions}（工时 {work}，夜间生产）", CampToast.Ok);
        // 开发者日志里才有热量账（玩家看不到）：投入多少、每份要多少、浪费了多少。
        GD.Print($"[烹饪] {cook.DisplayName} 下锅：总热量 {plan.TotalCalories}、每份 {plan.PortionCost} " +
                 $"⇒ {plan.Portions} 份（浪费 {plan.TotalCalories - plan.Portions * plan.PortionCost} 点）");
        RefreshCooking();
        if (_stashOpen) OpenStash(null);
    }

    /// <summary>
    /// 烹饪完工：把做好的份数加进营地食物（<see cref="CampResources.Food"/>，1 份 = 1 人 1 餐）。
    /// 食材在开工时已扣，这里只产不扣。
    /// </summary>
    private void CompleteCookJob(int portions)
    {
        _resources.AddFood(portions);
        _campToast.Show($"饭好了：食物 ×{portions}。", CampToast.Ok);
        GD.Print($"[烹饪] 完工 → 食物 ×{portions}（营地食物 {_resources.Food} 份）");
        if (_cookingOpen) RefreshCooking();
        if (_stashOpen) OpenStash(null);
    }
}
