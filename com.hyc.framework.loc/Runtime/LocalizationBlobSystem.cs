using System.IO;
using Unity.Entities;
using UnityEngine;

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
            if (!Directory.Exists(path))
            {
                Debug.LogWarning($"Localization folder not found: {path}. Run HYC Framework/Localization/Import Excel first.");
                return;
            }

            LocalizationManager.Reload(path);
        }
    }
}
