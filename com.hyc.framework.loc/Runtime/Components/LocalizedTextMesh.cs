using UnityEngine;

namespace HYC.Framework.Loc
{
    /// <summary>Localization component for 3D <see cref="TextMesh"/>.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh))]
    public class LocalizedTextMesh : LocalizedBase
    {
        public override void Refresh(string text) => GetComponent<TextMesh>().text = text;
    }
}