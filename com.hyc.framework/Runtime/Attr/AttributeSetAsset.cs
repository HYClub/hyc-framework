using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Attr
{
    /// <summary>
    /// 属性预设（优化方向 C）：一组属性的「快照」，给实体用。
    /// 编辑时在 Inspector 里从某个 <see cref="AttributeCatalog"/> 勾选若干 key，
    /// OnValidate 会自动把选中项烘焙成 <see cref="AttributeValue"/> 列表（values）。
    /// 运行时 <see cref="AttributeInitSystem"/> 或直接 <see cref="AttributeInitializer.InitFromSet"/>
    /// 把 values 复制进实体的 DynamicBuffer&lt;AttributeValue&gt;，零样板。
    /// 创建：Project 右键 / 菜单 Assets ▸ Create ▸ HYC Framework/Attributes/Attribute Set。
    /// </summary>
    [CreateAssetMenu(menuName = "HYC Framework/Attributes/Attribute Set", fileName = "AttributeSet")]
    public class AttributeSetAsset : ScriptableObject
    {
        /// <summary>属性来源目录。</summary>
        public AttributeCatalog catalog;
        /// <summary>选中的属性 key 子集（编辑期勾选）。</summary>
        public List<string> keys = new List<string>();
        /// <summary>烘焙好的运行期属性袋内容；Init 时直接复制进实体。</summary>
        public List<AttributeValue> values = new List<AttributeValue>();

        /// <summary>依据 catalog + keys 重新烘焙 values。catalog 或 keys 变动后应调用（Inspector 的 OnValidate 会自动调）。</summary>
        public void Rebuild()
        {
            values.Clear();
            if (catalog == null) return;
            for (int i = 0; i < keys.Count; i++)
            {
                var e = catalog.Get(keys[i]);
                if (e != null) values.Add(e.ToValue());
            }
        }

        private void OnValidate() { Rebuild(); }
    }
}
