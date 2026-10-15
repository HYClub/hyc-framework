using System.IO;
using Unity.Entities;
using UnityEngine;
using HYC.Framework.IO;

namespace HYC.Framework.Loc
{
    /// <summary>
    /// Boot-time localization loader. Reads the blob files written by the
    /// editor import pipeline (<c>id</c> plus one <c>.lang</c> per language)
    /// from <c>StreamingAssets/Localization</c> and feeds them into
    /// <see cref="LocalizationManager"/>. Mirrors the generated config
    /// BlobSystem pattern; the host installer creates it at boot.
    /// </summary>
    public partial class LocalizationBlobSystem : SystemBase
    {
        /// <summary>StreamingAssets sub-folder where the pipeline writes data.</summary>
        public const string DefaultFolderName = "Localization";

        /// <summary>
        /// Loads localization data at play start so localized components resolve
        /// in any scene — including scenes without a framework bootstrap. The
        /// boot check is skipped once data is already loaded (e.g. by an earlier
        /// <see cref="LocalizationBlobSystem"/> instance created by an installer).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoLoadAtPlay()
        {
            // NeedsPreload 平台（WebGL / Android）上，本方法可能早于宿主 await PreloadAsync 完成就触发，
            // 此时缓存是空的，读了也全空。主动跳过，改由引导入口在预取完成后补加载入
            // （FrameworkBootstrap.InitializeAsync / FrameworkModeBootstrap.InitializeAsync
            //   → FrameworkLifecycle.EnsureBootDataLoaded）。PC / iOS 直接读文件，不受影响。
            if (StreamingAssets.NeedsPreload && !StreamingAssets.IsPreloaded)
            {
                Debug.LogWarning("[Loc] 需要预取的平台（WebGL / Android）上 StreamingAssets 尚未预取完成，" +
                                 "已跳过自动加载。请用 FrameworkBootstrap.InitializeAsync（或 FrameworkModeBootstrap.InitializeAsync）" +
                                 "作为引导入口——它会在预取完成后自动补加载入，无需为平台写不同代码。");
                return;
            }
            if (LocalizationManager.IDs == null || LocalizationManager.IDs.Length == 0)
                Load();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            Load();
        }

        protected override void OnUpdate() { }

        /// <summary>Loads localization data from <c>StreamingAssets/Localization</c>.</summary>
        public static void Load()
        {
            var path = Path.Combine(Application.streamingAssetsPath, DefaultFolderName);
            // NeedsPreload 平台（WebGL / Android）既不能列目录也不能查目录：跳过存在性检查，
            // 交给 LocalizationManager.Reload 从预取缓存里按前缀枚举读取。
            if (!StreamingAssets.NeedsPreload && !Directory.Exists(path))
            {
                Debug.LogWarning($"Localization folder not found: {path}. Run HYC Framework/Localization/Import Excel first.");
                return;
            }

            LocalizationManager.Reload(path);
        }
    }
}
