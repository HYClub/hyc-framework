using UnityEditor;
using UnityEngine;

namespace HYC.Framework.RedDot.Editor
{
    /// <summary>
    /// <see cref="RedDotKey"/> 的检视面板绘制：已注册节点非空时显示为**下拉**（选节点名），
    /// 否则退化为 int 输入框（编辑期树为空是常态——注册发生在运行时引导/玩法初始化）。
    /// </summary>
    [CustomPropertyDrawer(typeof(RedDotKey))]
    public sealed class RedDotKeyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var idProp = property.FindPropertyRelative("_id");
            if (idProp == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var keys = RedDotManager.Keys;
            if (keys.Count == 0)
            {
                // 树未注册：保持可手填，避免编辑期无法配置
                EditorGUI.PropertyField(position, idProp, label);
                return;
            }

            var labels = new string[keys.Count + 1];
            var ids = new int[keys.Count + 1];
            labels[0] = "<未配置>";
            ids[0] = 0;

            var current = 0;
            for (var i = 0; i < keys.Count; i++)
            {
                var name = RedDotManager.GetDisplayName(keys[i].Id);
                labels[i + 1] = string.IsNullOrEmpty(name) ? "#" + keys[i].Id : name;
                ids[i + 1] = keys[i].Id;
                if (keys[i].Id == idProp.intValue)
                    current = i + 1;
            }

            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.Popup(position, label.text, current, labels);
            if (EditorGUI.EndChangeCheck())
                idProp.intValue = ids[next];
        }
    }
}
