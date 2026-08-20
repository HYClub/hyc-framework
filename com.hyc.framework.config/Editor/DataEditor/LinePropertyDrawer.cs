using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    [CustomPropertyDrawer(typeof(LineAttribute))]
    public class LinePropertyDrawer : DecoratorDrawer
    {
        public override float GetHeight()
        {
            var line = attribute as LineAttribute;
            return line.Space * 2 + 1;
        }

        public override void OnGUI(Rect position)
        {
            var line = attribute as LineAttribute;
            if (line.Space > 0)
                position.y += line.Space;

            position.height = 1;
            var oldColor = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.3f);
            GUI.DrawTexture(position, EditorGUIUtility.whiteTexture);
            GUI.color = oldColor;
        }
    }
}
