using UnityEditor;

namespace HYC.Framework.Editor
{
    /// <summary>
    /// 资产后处理：当工程里任意 .inputactions 资产被改动导入时，自动重跑输入组件生成。
    /// 与 Attr/BT 一致——改了动作定义不必手动点菜单，下次编译即同步。
    /// 用 EditorApplication.delayCall 延到导入流程结束后再生成，避免重入。
    /// </summary>
    public class InputComponentPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = false;
            foreach (var p in imported)
            {
                if (p.EndsWith(".inputactions", System.StringComparison.OrdinalIgnoreCase))
                {
                    touched = true;
                    break;
                }
            }
            if (!touched) return;
            EditorApplication.delayCall += InputComponentGeneratorMenu.GenerateAll;
        }
    }
}
