using HYC.Framework.SceneContent;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 场景内容类型定义资产的工厂：AI / 程序化创建的**唯一入口**。
    /// <para>
    /// 对应数据配置的 <c>ConfigAssetFactory</c> 与行为树的 <c>BTAssetFactory</c>：创建资产必须走工厂，
    /// 禁止 <c>ScriptableObject.CreateInstance&lt;SceneContentTypeDef&gt;()</c> + 手填 className / 手拼路径落盘。
    /// 工厂负责：归置默认文件夹、按显示名自动命名（<see cref="SceneContentTypeCodeGen.SyncTypeAssetName"/>）、
    /// 冲突加序号、清理不可见字符（<see cref="SceneContentTypeCodeGen.NormalizeIdentifiers"/>）。
    /// 场景内容类型**不参与发号服**（没有 ID/GUID，它本质是代码模板 / 生成源），所以工厂不派发 ID。
    /// </para>
    /// </summary>
    public static class SceneContentFactory
    {
        /// <summary>
        /// 创建场景内容类型定义资产。
        /// </summary>
        /// <param name="displayName">显示名（如「场景内容/出生点」）；会净化并同步成 .asset 文件名。</param>
        /// <param name="className">生成类名；为 null 时回落 <c>NewSceneContent</c> + 序号（用户随后在编辑器改名）。</param>
        /// <param name="targetFolder">目标文件夹；为 null 时落到 <see cref="SceneContentTypeCodeGen.DefaultTypeFolder"/>。</param>
        /// <returns>新建并保存的资产；失败返回 null。</returns>
        public static SceneContentTypeDef CreateType(
            string displayName = "场景内容/新类型", string className = null, string targetFolder = null)
        {
            var folder = string.IsNullOrEmpty(targetFolder)
                ? SceneContentTypeCodeGen.DefaultTypeFolder
                : targetFolder;
            EnsureFolder(folder);

            // 先在临时名 NewSceneContent 上创建，再由 SyncTypeAssetName 按显示名改名（复用数据配置同名逻辑）
            var path = $"{folder}/NewSceneContent.asset";
            var index = 1;
            while (AssetDatabase.LoadAssetAtPath<SceneContentTypeDef>(path) != null)
            {
                path = $"{folder}/NewSceneContent {index}.asset";
                index++;
            }

            var def = ScriptableObject.CreateInstance<SceneContentTypeDef>();
            def.displayName = displayName ?? "场景内容/新类型";
            def.className = string.IsNullOrWhiteSpace(className) ? "NewSceneContent" + index : className.Trim();
            AssetDatabase.CreateAsset(def, path);
            // 先落盘再改名：刚 CreateAsset 的资产路径尚未稳定，直接 MoveAsset 可能静默失败
            AssetDatabase.SaveAssets();

            // 创建即按显示名命名（与数据配置 CreateTemplate 一致）；清理不可见字符
            SceneContentTypeCodeGen.NormalizeIdentifiers(def);
            SceneContentTypeCodeGen.SyncTypeAssetName(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return def;
        }

        /// <summary>批量创建 <paramref name="count"/> 个类型（显示名 = <paramref name="prefix"/> + 序号）。</summary>
        public static SceneContentTypeDef[] CreateTypeMany(string prefix, int count, string targetFolder = null)
        {
            if (count <= 0)
                return new SceneContentTypeDef[0];
            var result = new SceneContentTypeDef[count];
            for (var i = 0; i < count; i++)
                result[i] = CreateType($"{prefix}{i + 1}", null, targetFolder);
            return result;
        }

        /// <summary>逐级创建目录（必须在 Assets 下）；已存在则跳过。</summary>
        private static void EnsureFolder(string folder)
        {
            folder = (folder ?? "").Replace("\\", "/").Trim('/');
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var parts = folder.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
