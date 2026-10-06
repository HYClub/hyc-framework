// =============================================================================
//  HYCSimple · 配置系统示例 · 行结构体（生成产物示例）
//  真实项目里这一行结构体由 ConfigGenerator 从模板/Excel 自动生成（带 [BlobGenerate]），
//  形如 CfgItem / CfgHero。这里手写一份等价的 blittable 行，让 Sample 导入即编译、
//  不依赖先跑代码生成。字段用纯标量（long/int/float），保证 unmanaged/blittable。
//  真实生成行里的文本字段是 BlobString（见同目录 AI-NOTES.md）；本例为演示「配置来自数据文件」
//  用数值字段承载语义（名称留在 CSV 里供人读，行内只存可 blittable 的字段）。
// =============================================================================
namespace HYC.Simple.Config
{
    /// <summary>
    /// 物品配置行。对应真实项目里「物品表」的一行。
    /// 必须 blittable（ConfigBlobTable&lt;TRow&gt; where TRow : unmanaged）。
    /// </summary>
    public struct HycSimpleItemRow
    {
        public long Id;        // 主键（ConfigManager 按它查表）
        public int Price;      // 价格
        public int StackMax;   // 最大堆叠
        public float Weight;   // 重量
        public int Rarity;     // 稀有度 1=普通 2=精良 3=史诗
        public int Category;   // 类别 1=武器 2=消耗品 3=材料
    }
}
