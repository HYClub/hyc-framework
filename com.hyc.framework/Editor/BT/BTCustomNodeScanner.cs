// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTCustomNodeScanner.cs
// 说明: 自动扫描所有程序集里的 BTCustomNode 子类,
//       按 (树类型, SubType) 注册到 BTGameNodeRegistry,
//       编辑器子类型下拉自动出现(无需手动注册)
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace HYC.Framework.BT.Editor
{
    public static class BTCustomNodeScanner
    {
        private static bool _scanned;
        private static List<Type> _nodeTypes = new List<Type>();

        /// <summary>扫描所有 BTCustomNode 子类并注册。幂等。</summary>
        public static void ScanAndRegister()
        {
            if (_scanned) return;
            _scanned = true;

            _nodeTypes.Clear();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (typeof(BTCustomNode).IsAssignableFrom(t))
                        _nodeTypes.Add(t);
                }
            }

            // 注册: 实例化每个类取 SubType/NodeName/Kind
            foreach (var t in _nodeTypes)
            {
                try
                {
                    var node = (BTCustomNode)Activator.CreateInstance(t);
                    var info = new BTGameNodeInfo
                    {
                        Name = node.NodeName,
                        Description = node.Description,
                        Params = node.Params,
                        Category = (int)node.Kind,
                    };
                    BTGameNodeRegistry.Register(node.TreeKind, node.SubType, info);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[BT] 自定义节点 {t.Name} 注册失败: {e.Message}");
                }
            }
        }

        /// <summary>所有自定义节点类型(按名称排序)。</summary>
        public static List<Type> AllNodeTypes
        {
            get
            {
                if (!_scanned) ScanAndRegister();
                return _nodeTypes.OrderBy(t => ((BTCustomNode)Activator.CreateInstance(t)).NodeName).ToList();
            }
        }
    }
}
