// ============================================================
// HYC Framework - 音频总览资产定位
// 文件: Editor/Audio/AudioCatalogAssetLocator.cs
// 说明: 在工程里定位 AudioCatalogAsset 实例。音频管理窗口与数据编辑器共用。
// ============================================================

using UnityEditor;
using UnityEngine;

// 命名空间说明：Locator 放在 HYC.Framework.Audio（而非 AudioEditor），
// 因为配置数据编辑器（Config.Editor 程序集）的 DrawAudioField 也要用它，
// 放到 AudioEditor 会让 Config 程序集反向依赖音频窗口所在的命名空间。
namespace HYC.Framework.Audio
{
    /// <summary>
    /// 定位工程内的音频总览资产。约定路径为 <c>Assets/HYC/Audio/AudioCatalog.asset</c>，
    /// 但不强求该路径 —— 找不到时回退到全工程搜索，保证用户把资产放别处也能用。
    /// </summary>
    public static class AudioCatalogAssetLocator
    {
        /// <summary>约定路径。</summary>
        public const string DefaultPath = "Assets/HYC/Audio/AudioCatalog.asset";

        /// <summary>
        /// 加载音频总览资产。找不到返回 null（调用方负责提示用户创建）。
        /// </summary>
        public static AudioCatalogAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AudioCatalogAsset>(DefaultPath);
            if (asset != null) return asset;

            var guids = AssetDatabase.FindAssets("t:AudioCatalogAsset");
            if (guids.Length == 0) return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<AudioCatalogAsset>(path);
        }

        /// <summary>加载或新建音频总览资产（窗口的"创建总览"按钮用）。</summary>
        public static AudioCatalogAsset LoadOrCreate()
        {
            var asset = Load();
            if (asset != null) return asset;

            var dir = System.IO.Path.GetDirectoryName(DefaultPath);
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            asset = ScriptableObject.CreateInstance<AudioCatalogAsset>();
            AssetDatabase.CreateAsset(asset, DefaultPath);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
