using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T68] 弹丸飞行速度引擎轴。飞速从全局常量 560f 改为**逐武器字段** <see cref="Weapon.FlightSpeed"/>，
/// 并接上《弓与箭之道》「弹道速度 +20%」的射手侧加成（此前挂起未生效）。
/// <para>
/// 本文件锁定的是**规则形态与零漂移**：默认武器飞速＝旧的 560（既有基线一个字节不漂），
/// 读过书的弓弩飞速 ×1.2 真生效，且飞速能被乘子修改（为下游改装「飞速 +12%」留的通路）。
/// 飞速是**空间层弹道飞行**属性——Duel/CombatResolver/Ballistics 不建模弹丸飞行时间，
/// 故本轴对 Sim 结算路径结构性零影响（新字段不被任何结算入口读取）。
/// </para>
/// </summary>
public class FlightSpeedTests
{
    /// <summary>飞速轴的历史等效常量：改造前 <c>Projectile.Speed</c> 全局常量的值。</summary>
    private const double LegacyGlobalFlightSpeed = 560.0;

    // ==================== 零漂移：默认飞速 = 旧全局 560 ====================

    [Fact]
    public void 默认武器飞速等于旧全局常量560_零漂移()
    {
        // 任意未显式设 FlightSpeed 的武器（＝全部既有武器）飞速都必须等于旧全局 560f。
        Assert.Equal(LegacyGlobalFlightSpeed, new Weapon().FlightSpeed);
    }

    [Fact]
    public void 全表既有武器飞速一律560_没有谁偷偷改了默认()
    {
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.Name is not "自制手枪" and not "牙医小手枪"))
        {
            Assert.Equal(LegacyGlobalFlightSpeed, w.FlightSpeed);
        }

        Assert.Equal(450, WeaponTable.ImprovisedPistol().FlightSpeed);
        Assert.Equal(400, WeaponTable.DentistPistol().FlightSpeed);
    }

    // ==================== 《弓与箭之道》→ 飞速 ×1.2 真生效 ====================

    [Fact]
    public void 读过弓与箭之道_弓弩飞速乘一点二真生效()
    {
        foreach (Weapon bow in WeaponTable.ArcheryArsenal())
        {
            Weapon withBook = Archery.Combine(bow, ArrowTable.Handmade(), hasReadArcheryBook: true);
            Weapon noBook = Archery.Combine(bow, ArrowTable.Handmade(), hasReadArcheryBook: false);

            // 没读书：飞速原样（默认 560）；读书：×1.2。
            Assert.Equal(bow.FlightSpeed, noBook.FlightSpeed);
            Assert.Equal(bow.FlightSpeed * Archery.BookFlightSpeedMult, withBook.FlightSpeed, 9);
            Assert.Equal(1.2, Archery.BookFlightSpeedMult);
        }
    }

    [Fact]
    public void 搭箭不改飞速_飞速只由弓与书决定不由箭决定()
    {
        // 箭改写伤害/穿透/射程/冷却/散布，但**不碰飞速**——换支箭不会让箭飞得更快。
        Weapon bow = WeaponTable.ArcheryArsenal().First();
        foreach (ArrowDef arrow in ArrowTable.All)
        {
            Weapon eff = Archery.Combine(bow, arrow, hasReadArcheryBook: false);
            Assert.Equal(bow.FlightSpeed, eff.FlightSpeed);
        }
    }

    // ==================== 乘算通路：飞速可被乘子修改（为下游改装留） ====================

    [Fact]
    public void 飞速可被乘子修改_乘算通路通()
    {
        // 喂一个假乘子验证「飞速能被改装乘算改写」这条通路，本单不建真改装。
        var fast = new Weapon { FlightSpeed = LegacyGlobalFlightSpeed * 1.12 };
        Assert.Equal(LegacyGlobalFlightSpeed * 1.12, fast.FlightSpeed);

        // 书 × 改装乘子连乘（CLAUDE.md 铁律：同轴一律乘算）：560 × 1.2 × 1.12。
        double stacked = LegacyGlobalFlightSpeed * Archery.BookFlightSpeedMult * 1.12;
        Assert.Equal(560.0 * 1.2 * 1.12, stacked, 9);
    }
}
