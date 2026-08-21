// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTGameNodeRegistry.cs
// 说明: 游戏层自定义节点注册表 - 通用引擎的语义注入点
//       hyc 引擎不认识游戏层的子类型数字(0/1/6...),
//       游戏层启动时注册"子类型 → 名称/参数描述",
//       编辑器据此显示人名节点和参数面板
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace HYC.Framework.BT
{
    /// <summary>自定义节点的一个参数描述。</summary>
    public struct BTGameNodeParamDesc
    {
        public string Name;      // 参数名, 如 "伤害倍率"
        public BTGameNodeParamKind Kind;
        public float DefaultFloat;
        public long DefaultLong;
        public string[] Options; // 枚举选项(Kind=Enum 时)

        public static BTGameNodeParamDesc Float(string name, float def = 0f)
            => new BTGameNodeParamDesc { Name = name, Kind = BTGameNodeParamKind.Float, DefaultFloat = def };
        public static BTGameNodeParamDesc Int(string name, long def = 0)
            => new BTGameNodeParamDesc { Name = name, Kind = BTGameNodeParamKind.Int, DefaultLong = def };
        public static BTGameNodeParamDesc Enum(string name, string[] options, long def = 0)
            => new BTGameNodeParamDesc { Name = name, Kind = BTGameNodeParamKind.Enum, Options = options, DefaultLong = def };
        public static BTGameNodeParamDesc Attribute(string name, long def = 0)
            => new BTGameNodeParamDesc { Name = name, Kind = BTGameNodeParamKind.Attribute, DefaultLong = def };
    }

    public enum BTGameNodeParamKind : byte
    {
        Float,
        Int,
        Enum,
        Attribute,   // 属性枚举下拉(用 BTTreeAsset 拖入的属性枚举)
    }

    /// <summary>游戏层自定义节点的语义信息。</summary>
    public struct BTGameNodeInfo
    {
        public string Name;              // 显示名, 如 "找最近敌人"
        public string Description;       // 提示
        public BTGameNodeParamDesc[] Params; // 参数描述(按 Long[1..], Float[0..] 顺序)
        public int Category;             // 节点分类: 0=自动(Custom), 1=条件, 2=动作(用于样式)
    }

    /// <summary>
    /// 静态注册表(引擎通用, 游戏层注入语义)。
    /// 键 = (树类型, 子类型): 技能树和 AI 树的子类型数字独立, 互不冲突。
    /// </summary>
    public static class BTGameNodeRegistry
    {
        private static readonly Dictionary<(BTTreeKind, long), BTGameNodeInfo> _nodes = new Dictionary<(BTTreeKind, long), BTGameNodeInfo>();

        /// <summary>注册一个自定义节点子类型的语义(需指定树类型)。重复注册覆盖。</summary>
        public static void Register(BTTreeKind kind, long subtype, BTGameNodeInfo info)
        {
            _nodes[(kind, subtype)] = info;
        }

        public static bool TryGet(BTTreeKind kind, long subtype, out BTGameNodeInfo info)
            => _nodes.TryGetValue((kind, subtype), out info);

        public static void Clear() => _nodes.Clear();
    }
}
