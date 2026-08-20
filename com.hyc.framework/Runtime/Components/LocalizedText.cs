using UnityEngine;
using UnityEngine.UI;

namespace HYC.Framework.Loc
{
    /// <summary>Localization component for Unity UI <see cref="Text"/>.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public class LocalizedText : LocalizedBase
    {
        public override void Refresh(string text) => GetComponent<Text>().text = text;
    }
}