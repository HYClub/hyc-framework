using UnityEditor;
using UnityEngine;

namespace HYC.Framework.RedDot.Editor
{
    /// <summary>
    /// 红点树只读查看器（v1）：列出已注册节点的层级、自有值与聚合值。
    ///
    /// <para>只读——注册与打标由游戏代码完成（<see cref="RedDotManager.Register"/> /
    /// <see cref="RedDotManager.Mark"/>）。<b>非 Play 模式下树通常为空</b>，
    /// 因为注册发生在宿主引导/玩法初始化里。</para>
    /// </summary>
    public sealed class RedDotWindow : EditorWindow
    {
        // priority 102：落在 Tools 档（Assets 100/101 之后），与相邻项差 1 ⇒ 不画分隔线。
        [MenuItem("HYC Framework/Tools/Red Dot Viewer", false, 102)]
        private static void Open() => GetWindow<RedDotWindow>("Red Dot Viewer");

        private Vector2 _scroll;
        private bool _showInactive = true;

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "只读视图：红点树由游戏代码注册与打标，这里只展示当前聚合结果。" +
                "非 Play 模式下通常为空（注册发生在宿主引导阶段）。",
                MessageType.Info);

            _showInactive = EditorGUILayout.ToggleLeft("显示未亮的节点", _showInactive);
            EditorGUILayout.LabelField("节点数", RedDotManager.Count.ToString());
            EditorGUILayout.LabelField("版本", RedDotManager.Version.ToString());
            DrawHeader();

            var keys = RedDotManager.Keys;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var active = RedDotManager.IsActive(key);
                if (!_showInactive && !active)
                    continue;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8f + DepthOf(key) * 12f);
                EditorGUILayout.LabelField(DisplayName(key), GUILayout.Width(200f));
                EditorGUILayout.LabelField(key.Id.ToString(), EditorStyles.miniLabel, GUILayout.Width(64f));
                EditorGUILayout.LabelField(RedDotManager.GetMode(key) == RedDotMode.Count ? "Count" : "Bool",
                    EditorStyles.miniLabel, GUILayout.Width(48f));
                EditorGUILayout.LabelField($"自有 {(RedDotManager.GetOwnFlag(key) ? "1" : "0")}/{RedDotManager.GetOwnCount(key)}",
                    EditorStyles.miniLabel, GUILayout.Width(96f));
                EditorGUILayout.LabelField($"聚合 {(active ? "亮" : "灭")}/{RedDotManager.GetCount(key)}",
                    EditorStyles.miniLabel, GUILayout.Width(96f));
                EditorGUILayout.LabelField($"子 {RedDotManager.GetChildCount(key)}",
                    EditorStyles.miniLabel, GUILayout.Width(48f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("节点", EditorStyles.miniBoldLabel, GUILayout.Width(208f));
            GUILayout.Label("id", EditorStyles.miniBoldLabel, GUILayout.Width(64f));
            GUILayout.Label("模式", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
            GUILayout.Label("自有", EditorStyles.miniBoldLabel, GUILayout.Width(96f));
            GUILayout.Label("聚合", EditorStyles.miniBoldLabel, GUILayout.Width(96f));
            GUILayout.Label("子", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
            EditorGUILayout.EndHorizontal();
        }

        private static string DisplayName(RedDotKey key)
        {
            var name = RedDotManager.GetDisplayName(key.Id);
            return string.IsNullOrEmpty(name) ? "(未命名)" : name;
        }

        /// <summary>沿父链计算缩进深度（父链异常时最多走 32 层，防死循环）。</summary>
        private static int DepthOf(RedDotKey key)
        {
            var depth = 0;
            var current = RedDotManager.GetParent(key);
            var guard = 0;
            while (current.IsValid && ++guard < 32)
            {
                depth++;
                current = RedDotManager.GetParent(current);
            }
            return depth;
        }
    }
}
