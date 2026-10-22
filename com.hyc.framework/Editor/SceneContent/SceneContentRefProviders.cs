using UnityEditor;
using UnityEngine;
using HYC.Framework.SceneContent;

namespace HYC.Framework.SceneContent.Editor
{
    /// <summary>
    /// 把「资源路径 / Addressable 地址」的解析能力注入运行期静态钩子
    /// （<see cref="SceneContentRefPath.AssetPathProvider"/>）。
    /// <para>
    /// 为什么用注入：payload 由<b>生成的标记组件</b>在编辑器里产出，但那批代码落在
    /// 运行时程序集（<c>Assets/HYC/Generated</c>），不能直接调 <see cref="AssetDatabase"/>。
    /// 让运行时暴露委托、编辑器实现，两侧职责就分干净了；运行期委托为空也不影响任何逻辑。
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneContentRefProviders
    {
        static SceneContentRefProviders()
        {
            SceneContentRefPath.AssetPathProvider = GetAssetPath;
            SceneContentRefPath.AddressableProvider = GetAddressableAddress;
        }

        private static string GetAssetPath(GameObject go)
        {
            var path = AssetDatabase.GetAssetPath(go);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private static string GetAddressableAddress(GameObject go)
        {
            var path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path)) return null;
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return null;
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return null;
            var entry = settings.FindAssetEntry(guid, true);
            return entry != null ? entry.address : null;
        }
    }
}
