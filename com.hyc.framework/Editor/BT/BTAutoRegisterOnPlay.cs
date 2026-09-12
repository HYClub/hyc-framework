// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTAutoRegisterOnPlay.cs
// 说明: 进入播放模式时, 自动把工程内全部 BTTreeAsset 构建 Blob 并注册到
//       运行时 BTManager。游戏层只需给实体挂 RunningBT{TreeId},
//       即由 BTInterpreterSystem(ECS) 逐帧 Tick 驱动, 无需手动导出。
//       注意: 必须在 EnteredPlayMode(域重载之后)注册, 否则静态表会被清空。
// ============================================================

using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    [InitializeOnLoad]
    public static class BTAutoRegisterOnPlay
    {
        static BTAutoRegisterOnPlay()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            int ok = 0, fail = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:BTTreeAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<BTTreeAsset>(path);
                if (asset == null || asset.TreeId == 0) continue;
                if (BTBlobBuilder.Build(asset, out var blob))
                {
                    BTManager.Register(asset.TreeId, blob);
                    ok++;
                }
                else
                {
                    fail++;
                    Debug.LogWarning($"[BT] 自动注册失败(树数据无效): {path} treeId={asset.TreeId}");
                }
            }
            Debug.Log($"[BT] 播放模式自动注册完成: 成功 {ok} 棵, 失败 {fail} 棵(实体挂 RunningBT 即可运行)");
        }
    }
}
