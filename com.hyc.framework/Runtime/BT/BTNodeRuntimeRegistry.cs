// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeRuntimeRegistry.cs
// 说明: 运行时自定义节点注册表 - 按子类型分发到 BTCustomNode 类
//       运行时反射扫描一次, 缓存实例, 树 Tick 时按子类型执行
// ============================================================

using System;
using System.Collections.Generic;

namespace HYC.Framework.BT
{
    /// <summary>
    /// 运行时注册表: (树类型, 子类型) → BTCustomNode 实例。
    /// 扫描所有程序集一次, 树 Tick 时统一分发执行。
    /// </summary>
    public static class BTNodeRuntimeRegistry
    {
        private static Dictionary<(BTTreeKind, long), BTCustomNode> _nodes;
        private static bool _initialized;

        /// <summary>确保已扫描注册(幂等)。</summary>
        public static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            _nodes = new Dictionary<(BTTreeKind, long), BTCustomNode>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(BTCustomNode).IsAssignableFrom(t)) continue;
                    try
                    {
                        var node = (BTCustomNode)Activator.CreateInstance(t);
                        _nodes[(node.TreeKind, node.SubType)] = node;
                    }
                    catch { /* 跳过无法实例化的 */ }
                }
            }
        }

        /// <summary>按子类型执行自定义节点。返回 false 表示未找到。</summary>
        public static bool Execute(BTTreeKind kind, long subType, ref BTContext ctx, ref BTNodeView view, out BTNodeState result)
        {
            EnsureInit();
            if (_nodes.TryGetValue((kind, subType), out var node))
            {
                result = node.Execute(ref ctx, ref view);
                return true;
            }
            result = BTNodeState.Failed;
            return false;
        }
    }
}
