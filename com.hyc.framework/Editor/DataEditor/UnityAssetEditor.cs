using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 把任意外部 Unity 资产（如 <c>.inputactions</c> / <c>.asset</c>）的原生 Inspector 整体嵌入右栏。
    /// <para>通过 <see cref="UnityEditor.Editor.CreateEditor"/> 创建原生编辑器实例，<c>OnInspectorGUI</c> 原样转发；
    /// 切换 / 关闭时必须 <c>DestroyImmediate</c>（原生编辑器不进 GC，会泄漏 IMGUI 状态）。沿用
    /// <c>SceneContentWindow</c> 已验证的嵌入方式。</para>
    /// </summary>
    public class UnityAssetEditor : ConfigDataEditor
    {
        private UnityEditor.Editor mNative;

        public override void Open(ConfigDataWindow window, TreeView tree, ConfigDataTreeNode treeNode, SerializedObject so)
        {
            base.Open(window, tree, treeNode, so);
            RebuildNative();
        }

        public override void Reload()
        {
            // 资产可能被外部改名 / 重导入：重建原生编辑器以反映最新 SerializedObject
            RebuildNative();
        }

        private void RebuildNative()
        {
            if (mNative != null)
            {
                Object.DestroyImmediate(mNative);
                mNative = null;
            }
            var asset = mTreeNode?.GetAsset();
            if (asset != null)
                mNative = UnityEditor.Editor.CreateEditor(asset);
        }

        public override void OnGUI(float viewW, float viewH)
        {
            if (mNative == null)
            {
                RebuildNative();
                if (mNative == null)
                {
                    GUILayout.Label("无法为选中资产创建编辑器。", EditorStyles.centeredGreyMiniLabel);
                    return;
                }
            }
            mNative.OnInspectorGUI();
        }

        public override void Dispose()
        {
            if (mNative != null)
            {
                Object.DestroyImmediate(mNative);
                mNative = null;
            }
            base.Dispose();
        }
    }
}
