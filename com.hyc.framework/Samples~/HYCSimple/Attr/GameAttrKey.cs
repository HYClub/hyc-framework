// =============================================================================
//  HYCSimple · 属性系统示例 · 生成访问器（枚举）
//  注意：本文件是「向导生成的产物」的示例副本，正常流程里应由
//  AttributeCatalogWindow / AttributeGenerateWizard 生成到 Assets/HYC/Generated/Attr/。
//  这里手写在 Sample 里，让例子导入即可编译、无需先跑向导。
//  生成文件不要手改——改目录后重新 Generate 整体覆盖。
// =============================================================================
namespace HYC.Simple.Attr.Generated
{
    /// <summary>
    /// 属性键枚举。序数 = AttributeCatalog 里条目的顺序。
    /// ⚠️ 改名 / 增删属性后必须重新 Generate，否则枚举与运行时袋数据序数错位。
    /// keyHash 按「名」算（非序数），所以同 key 改名不影响旧袋；但枚举序数变了要重生成。
    /// </summary>
    public enum GameAttrKey
    {
        HP = 0,           // 生命值（Int）
        MP = 1,           // 法力值（Int）
        MoveSpeed = 2,    // 移动速度（Float）
        IsInvincible = 3, // 是否无敌（Bool）
    }
}
