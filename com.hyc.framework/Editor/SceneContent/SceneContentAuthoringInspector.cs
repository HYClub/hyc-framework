using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace HYC.Framework.SceneContent.Editor
{
    [CustomEditor(typeof(SceneContentAuthoring))]
    public class SceneContentAuthoringInspector : UnityEditor.Editor
    {
        private ReorderableList _list;
        private SerializedProperty _entries;

        private void OnEnable()
        {
            _entries = serializedObject.FindProperty("entries");
            _list = new ReorderableList(serializedObject, _entries, true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "A. 实例条目 (prefab + transform + payload)"),
                elementHeight = 78f,
            };
            _list.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var el = _entries.GetArrayElementAtIndex(index);
                var w = rect.width;
                var prefab = el.FindPropertyRelative("prefab");
                var pos = el.FindPropertyRelative("position");
                var rot = el.FindPropertyRelative("rotation");
                var scale = el.FindPropertyRelative("scale");
                var payload = el.FindPropertyRelative("payloadJson");

                EditorGUI.PropertyField(new Rect(rect.x, rect.y + 2, w * 0.5f, 18), prefab, new GUIContent("prefab"));
                EditorGUI.PropertyField(new Rect(rect.x + w * 0.5f, rect.y + 2, w * 0.5f, 18), pos, new GUIContent("position"));
                EditorGUI.PropertyField(new Rect(rect.x, rect.y + 24, w * 0.5f, 18), rot, new GUIContent("rotation"));
                EditorGUI.PropertyField(new Rect(rect.x + w * 0.5f, rect.y + 24, w * 0.5f, 18), scale, new GUIContent("scale"));
                EditorGUI.PropertyField(new Rect(rect.x, rect.y + 46, w, 28), payload, new GUIContent("payload (json)"));
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var a = (SceneContentAuthoring)target;

            SceneContentEditorGUI.DrawToolbar(a);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("levelId"));
            _list.DoLayoutList();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6);
            SceneContentEditorGUI.DrawMarkerIntrospection(a);
        }
    }
}
