using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(FileAttribute))]
    public class FilePropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var pathOld = property.stringValue;

            // 路径验证：相对 Assets 或绝对路径
            var validate = true;
            if (!string.IsNullOrEmpty(pathOld))
            {
                if (Path.IsPathRooted(pathOld))
                    validate = File.Exists(pathOld);
                else
                    validate = File.Exists(Path.Combine(Application.dataPath, pathOld));
            }

            var color = GUI.color;
            if (!validate)
                GUI.color = Color.red;

            var fieldRect = EditorGUI.PrefixLabel(position, label);
            var textRect = new Rect(fieldRect.x, fieldRect.y, fieldRect.width - 24, fieldRect.height);
            var btnRect = new Rect(fieldRect.xMax - 22, fieldRect.y, 22, fieldRect.height);

            EditorGUI.BeginChangeCheck();
            var pathNew = EditorGUI.TextField(textRect, property.stringValue);
            if (GUI.Button(btnRect, "..."))
            {
                var curr = string.IsNullOrEmpty(property.stringValue) ? Application.dataPath
                    : Path.IsPathRooted(property.stringValue) ? Path.GetDirectoryName(property.stringValue)
                    : Path.GetDirectoryName(Path.Combine(Application.dataPath, property.stringValue));
                var picked = EditorUtility.OpenFilePanel("选择文件", curr, (attribute as FileAttribute).ext);
                if (!string.IsNullOrEmpty(picked))
                    pathNew = Path.GetRelativePath(Application.dataPath, picked);
            }
            if (EditorGUI.EndChangeCheck())
                property.stringValue = pathNew;

            GUI.color = color;
        }
    }
}
