// ============================================================
// HYC Framework - 天赋树模块(Runtime)
// 文件: Runtime/Talent/TalentNode.cs
// 说明: 天赋节点。它是 ScriptableObject 子资源 —— 因此<b>必须单独一个文件</b>。
//       Unity 只给「与文件同名的类」生成 MonoScript，而 AssetDatabase.AddObjectToAsset
//       把子资源写进 .asset 时必须能解析到 MonoScript；解析不到就会以"missing script"
//       落盘，重新导入后变成 null（现象：树里节点全没了、连线全悬空）。
//       所以 TalentNode 不能和 TalentTreeAsset 挤在同一个 .cs 里。
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.Talent
{
    /// <summary>
    /// 天赋节点。
    ///
    /// <b>它没有分类</b>——节点就是节点。天赋树的节点不像行为树那样需要
    /// Selector / Sequence / Action 这类功能性区分，图上每个节点的区别只在于
    /// 「叫什么名字」和「消耗什么」，加一层玩法语义的枚举只会限制使用方。
    /// 位置是编辑器坐标，运行期不用。
    ///
    /// <b>它是 ScriptableObject 子资源</b>：挂在所属 <see cref="TalentTreeAsset"/> 之下
    /// （通过 <c>AssetDatabase.AddObjectToAsset</c>），所以可以在 Project 窗口里展开树资产、
    /// 把单个节点直接拖进 UI 的 slot（见 <see cref="TalentTreeUIBase"/>）。
    /// 编辑器负责创建 / 删除子资源，运行期只读取已烘焙进 .asset 的引用。
    /// 因此节点<b>不能</b>用 <c>new</c> 构造，必须用 <c>ScriptableObject.CreateInstance&lt;TalentNode&gt;()</c>。
    /// </summary>
    public class TalentNode : ScriptableObject
    {
        public int id;
        public string title;

        /// <summary>可选。填了就用本地化文本渲染，不填直接用 <see cref="title"/>。</summary>
        public string descLocKey;

        /// <summary>最高可投资等级，至少 1。消耗按<b>每级</b>计。</summary>
        public int maxLevel = 1;

        public Vector2 editorPosition;

        public List<TalentCost> costs = new List<TalentCost>();
    }
}
