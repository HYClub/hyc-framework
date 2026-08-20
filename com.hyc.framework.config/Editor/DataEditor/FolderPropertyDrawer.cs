using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(FolderAttribute))]
    public class FolderPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var pathOld = property.stringValue;

            var validate = true;
            if (!string.IsNullOrEmpty(pathOld))
            {
                if (Path.IsPathRooted(pathOld))
                    validate = Directory.Exists(pathOld);
                else
                    validate = Directory.Exists(Path.Combine(Application.dataPath, pathOld));
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
                    : Path.IsPathRooted(property.stringValue) ? property.stringValue
                    : Path.Combine(Application.dataPath, property.stringValue);
                var picked = EditorUtility.OpenFolderPanel("选择文件夹", curr, "");
                if (!string.IsNullOrEmpty(picked))
                    pathNew = Path.GetRelativePath(Application.dataPath, picked);
            }
            if (EditorGUI.EndChangeCheck())
                property.stringValue = pathNew;

            GUI.color = color;
        }
    }
}
