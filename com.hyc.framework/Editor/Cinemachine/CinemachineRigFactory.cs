// ============================================================
// HYC Framework - Cinemachine 相机装配 · 预制工厂
// 文件: Editor/Cinemachine/CinemachineRigFactory.cs
// 说明: ① 新建 rig prefab——在预览场景里搭
//       「Camera + Brain 根 + RigTarget + 默认四机位」后存盘
//       （用预览场景而非当前场景，避免弄脏使用方打开的场景）；
//       ② 按配方增删机位组件（窗口切换类型时调用）。
// ============================================================

using System;
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HYC.Framework.Cinemachine;

namespace HYC.Framework.Cinemachine.Editor
{
    /// <summary>
    /// rig prefab 工厂：新建默认结构 + 按 <see cref="RigKinds"/> 配方装配机位组件。
    /// </summary>
    internal static class CinemachineRigFactory
    {
        /// <summary>新建 rig 时的默认机位（顺序即生成顺序，优先级 20/10/5/0 递减）。</summary>
        private static readonly RigKind[] s_DefaultKinds =
        {
            RigKind.FreeLook,
            RigKind.ThirdPerson,
            RigKind.Framed,
            RigKind.PanTilt,
        };

        /// <summary>默认机位对应的 FOV（与 <see cref="s_DefaultKinds"/> 逐项对齐）。</summary>
        private static readonly float[] s_DefaultFovs = { 50f, 40f, 45f, 60f };

        private const string k_RigTargetName = "RigTarget";

        /// <summary>
        /// 新建 rig prefab 并落盘（不导入、不选中，由调用方处理）。
        /// </summary>
        /// <param name="path">目标 .prefab 资产路径（不以 .prefab 结尾时自动补）。</param>
        /// <param name="rigName">根节点名（同时作为预制名）。</param>
        /// <param name="error">失败原因。</param>
        public static bool CreateRigPrefab(string path, string rigName, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                error = "保存路径为空。";
                return false;
            }
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                path += ".prefab";
            if (string.IsNullOrEmpty(rigName))
                rigName = Path.GetFileNameWithoutExtension(path);

            // 预览场景搭建：不碰使用方当前打开的场景（不会留脏标记）
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject(rigName);
                root.AddComponent<Camera>();
                root.tag = "MainCamera";

                var brain = root.AddComponent<CinemachineBrain>();
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, 0.5f);

                // 运行期目标绑定器（目标由宿主实例化后指派）
                root.AddComponent<CinemachineRigBinder>();

                var rigTarget = new GameObject(k_RigTargetName);
                rigTarget.transform.SetParent(root.transform, false);

                var priority = 20;
                for (int i = 0; i < s_DefaultKinds.Length; i++)
                {
                    var go = new GameObject(DefaultCameraName(s_DefaultKinds[i]));
                    go.transform.SetParent(root.transform, false);

                    var vcam = go.AddComponent<CinemachineCamera>();
                    vcam.Follow = rigTarget.transform;
                    vcam.LookAt = rigTarget.transform;
                    vcam.Priority = priority; // 隐式转换：写入即 Enabled = true

                    var lens = LensSettings.Default;
                    lens.FieldOfView = s_DefaultFovs[i];
                    vcam.Lens = lens;

                    ApplyKind(go, s_DefaultKinds[i]);

                    priority = Mathf.Max(0, priority - 10);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (root != null)
                    Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        /// <summary>
        /// 按配方给机位装配组件：缺的补上，同 Stage 的旧组件移除
        /// （CM 管线每个 Stage 只取首个组件，不移除会让旧组件继续生效造成"切换了类型却没变化"）。
        /// 已在配方里的组件不重复添加；组件类型因版本漂移不存在时告警跳过。
        /// </summary>
        public static void ApplyKind(GameObject vcamGo, RigKind kind)
        {
            if (vcamGo == null)
                return;

            var recipe = RigKinds.Get(kind);
            for (int i = 0; i < recipe.ComponentNames.Length; i++)
            {
                var type = CmComponentCatalog.Find(recipe.ComponentNames[i]);
                if (type == null)
                {
                    Debug.LogWarning("[HYC.Cinemachine] 找不到组件类型 " + recipe.ComponentNames[i]
                        + "（CM 版本漂移？），已跳过。");
                    continue;
                }
                if (vcamGo.GetComponent(type) != null)
                    continue;

                var added = Undo.AddComponent(vcamGo, type);
                if (!(added is CinemachineComponentBase stageComp))
                    continue;

                var existing = vcamGo.GetComponents<CinemachineComponentBase>();
                for (int j = 0; j < existing.Length; j++)
                {
                    var other = existing[j];
                    if (other == null || other == stageComp)
                        continue;
                    if (other.Stage == stageComp.Stage)
                        Undo.DestroyObjectImmediate(other);
                }
            }
        }

        /// <summary>默认机位节点名（配方展示名去空格，如 FreeLook 环绕 → vcam_FreeLook）。</summary>
        private static string DefaultCameraName(RigKind kind)
        {
            var recipe = RigKinds.Get(kind);
            var space = recipe.DisplayName.IndexOf(' ');
            var baseName = space > 0 ? recipe.DisplayName.Substring(0, space) : recipe.DisplayName;
            return "vcam_" + baseName;
        }

        /// <summary>确保资产文件夹存在（逐级创建，已存在则跳过）。</summary>
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            var parts = folder.Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
