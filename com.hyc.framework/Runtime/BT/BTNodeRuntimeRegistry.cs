// ============================================================
// HYC Framework - BT 模块
// 文件: Runtime/BT/BTNodeRuntimeRegistry.cs
// 说明: 运行时自定义节点注册表 - 按子类型分发到 BTCustomNode 类
//       运行时反射扫描一次, 缓存实例, 树 Tick 时按子类型执行
// ============================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HYC.Framework.BT
{
    /// <summary>
    /// 运行时注册表: (树类型, 子类型) → BTCustomNode 实例。
    /// 扫描所有程序集一次, 树 Tick 时统一分发执行。
    ///
    /// 两张表的分工（别把它们混用）：
    /// - <c>_nodes</c> 复合键 (TreeKind, SubType)，是<b>正常执行路径</b>，不同树类型的同号子类型互不干扰。
    /// - <c>_bySubType</c> 裸键 SubType，只服务于「解释器没显式设 GameHandler 时的回退分发」。
    ///   裸键空间本质上是全局的，所以同一 SubType 被不同 TreeKind 占用属于**冲突**，
    ///   会让回退分发执行到错误的实现（典型症状：AI 树的动作被子类型相同的技能树节点执行）。
    ///   注册时检测并报错，而不是静默「最后一个赢」。
    /// </summary>
    public static class BTNodeRuntimeRegistry
    {
        private static Dictionary<(BTTreeKind, long), BTCustomNode> _nodes;
        private static Dictionary<long, BTCustomNode> _bySubType; // 按子类型唯一定位(不依赖树类型)
        private static bool _initialized;

        /// <summary>确保已扫描注册(幂等)。</summary>
        public static void EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            _nodes = new Dictionary<(BTTreeKind, long), BTCustomNode>();
            _bySubType = new Dictionary<long, BTCustomNode>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                // 程序集里存在依赖缺失的类型时会抛 ReflectionTypeLoadException 而非返回全集，
                // 跳过是合理降级（Unity 播放时常见于未加载的可选依赖程序集）。
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException) { continue; }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BT] 扫描程序集 {asm.GetName().Name} 的类型失败，已跳过：{ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(BTCustomNode).IsAssignableFrom(t)) continue;
                    try
                    {
                        var node = (BTCustomNode)Activator.CreateInstance(t);
                        _nodes[(node.TreeKind, node.SubType)] = node;

                        // 裸键冲突检测：不同树类型抢同一个 SubType，后注册者不得静默顶掉前一个。
                        if (_bySubType.TryGetValue(node.SubType, out var prev))
                        {
                            if (prev.TreeKind != node.TreeKind || prev.GetType() != node.GetType())
                            {
                                Debug.LogError(
                                    $"[BT] 自定义节点子类型 {node.SubType} 被重复占用：" +
                                    $"{prev.GetType().Name}（{prev.TreeKind}）与 {node.GetType().Name}（{node.TreeKind}）。" +
                                    "复合键注册表不受影响，但「按子类型回退分发」将固定使用先注册的那个。" +
                                    "请为不同树类型分配不同 SubType。");
                                continue;
                            }
                        }
                        _bySubType[node.SubType] = node;
                    }
                    catch (Exception ex)
                    {
                        // 这里绝不能静默吞：实例化失败（缺构造函数/依赖）会让该节点在所有树里
                        // 静默变成永远 Failed，排查时毫无线索。
                        Debug.LogError($"[BT] 自定义节点 {t.FullName} 实例化失败，已跳过注册（该节点将在执行时返回 Failed）：{ex.GetType().Name}: {ex.Message}");
                    }
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

        /// <summary>按子类型查找已注册的自定义节点实例(未注册返回 null)。</summary>
        public static BTCustomNode Find(long subType)
        {
            EnsureInit();
            return _bySubType != null && _bySubType.TryGetValue(subType, out var node) ? node : null;
        }

        /// <summary>按子类型执行(不依赖树类型)。用作未显式设置 GameHandler 时解释器的默认回退分发。</summary>
        public static bool Execute(long subType, ref BTContext ctx, ref BTNodeView view, out BTNodeState result)
        {
            EnsureInit();
            if (_bySubType.TryGetValue(subType, out var node))
            {
                result = node.Execute(ref ctx, ref view);
                return true;
            }
            result = BTNodeState.Failed;
            return false;
        }
    }
}
