// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTAttributeEnum.cs
// 说明: 树属性枚举解析 - 把 BTTreeAsset 拖入的枚举脚本解析成
//       名称/值列表, 供树节点属性下拉使用
// ============================================================

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    public static class BTAttributeEnum
    {
        /// <summary>从树的属性枚举脚本解析枚举类型(非枚举返回 null)。</summary>
        public static Type Resolve(BTTreeAsset tree)
        {
            if (tree == null || tree.AttributeEnumScript == null)
                return null;

            // 1. 先试 MonoScript.GetClass(对已引用程序集有效)
            var t = tree.AttributeEnumScript.GetClass();
            if (t != null && t.IsEnum)
                return t;

            // 2. 回退: 按类型名(不含命名空间)从已加载程序集枚举类型匹配
            //    (解决 Assembly-CSharp 等非引用程序集的 MonoScript.GetClass 失败)
            var className = tree.AttributeEnumScript.name;
            if (string.IsNullOrEmpty(className))
                return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.IsEnum && type.Name == className)
                            return type;
                    }
                }
                catch { /* 跳过不可读程序集 */ }
            }
            return null;
        }

        /// <summary>枚举名称列表(空 = 未设置)。</summary>
        public static string[] GetNames(BTTreeAsset tree)
        {
            var t = Resolve(tree);
            return t == null ? Array.Empty<string>() : Enum.GetNames(t);
        }

        /// <summary>枚举值列表(与名称对应)。</summary>
        public static int[] GetValues(BTTreeAsset tree)
        {
            var t = Resolve(tree);
            return t == null ? Array.Empty<int>() : (int[])Enum.GetValues(t);
        }

        /// <summary>值 → 名称(未设置时返回数字)。</summary>
        public static string GetName(BTTreeAsset tree, int value)
        {
            var t = Resolve(tree);
            if (t == null) return value.ToString();
            var name = Enum.GetName(t, value);
            return name ?? value.ToString();
        }
    }
}
