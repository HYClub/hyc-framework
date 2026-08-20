using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(TextureSizeAttribute))]
    public class TextureSizeDrawer : PropertyDrawer
    {
        private static int[] MapTextureSize;
        private static GUIContent[] MapTextureSizeLabel;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (MapTextureSize == null)
            {
                MapTextureSize = new[] { 128, 512, 1024, 2048, 4096 };
                MapTextureSizeLabel = MapTextureSize.Select(r => new GUIContent(r.ToString())).ToArray();
            }

            var fieldRect = EditorGUI.PrefixLabel(position, label);

            EditorGUI.BeginChangeCheck();
            var value = ArrayUtility.IndexOf(MapTextureSize, property.intValue) == -1 ? MapTextureSize[0] : property.intValue;
            value = EditorGUI.IntPopup(fieldRect, value, MapTextureSizeLabel, MapTextureSize);
            if (EditorGUI.EndChangeCheck())
                property.intValue = value;
        }
    }
}
