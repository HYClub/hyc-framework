using TMPro;
using UnityEngine;

namespace HYC.Framework.Loc
{
    /// <summary>Localization component for <see cref="TMP_Text"/>.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedTMP : LocalizedBase
    {
        public override void Refresh(string text) => GetComponent<TMP_Text>().text = text;
    }
}