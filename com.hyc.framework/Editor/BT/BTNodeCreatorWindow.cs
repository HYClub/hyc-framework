// ============================================================
// HYC Framework - BT 模块(Editor)
// 文件: Editor/BT/BTNodeCreatorWindow.cs
// 说明: 新建自定义节点弹窗 - 填名/分类/树类型
//       自动分配子类型 ID + 生成模板代码文件
// ============================================================

using System.IO;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.BT.Editor
{
    public class BTNodeCreatorWindow : EditorWindow
    {
        private string _nodeName = "NewNode";
        private BTCustomNodeKind _kind = BTCustomNodeKind.Condition;
        private BTTreeKind _treeKind = BTTreeKind.Skill;

        public static void Open()
        {
            var w = GetWindow<BTNodeCreatorWindow>(true, "新建自定义节点");
            w.minSize = new Vector2(360, 260);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("新建自定义节点", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _nodeName = EditorGUILayout.TextField("节点名", _nodeName);
            _kind = (BTCustomNodeKind)EditorGUILayout.EnumPopup("分类", _kind);
            _treeKind = (BTTreeKind)EditorGUILayout.EnumPopup("树类型", _treeKind);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                _kind == BTCustomNodeKind.Condition ? "条件: 返回成功/失败, 可阻断后续"
                : _kind == BTCustomNodeKind.Action ? "动作: 执行一个行为"
                : "组合: 有子节点, 动态端口",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建"))
            {
                if (string.IsNullOrWhiteSpace(_nodeName))
                {
                    EditorUtility.DisplayDialog("错误", "节点名不能为空", "确定");
                    return;
                }
                if (CreateNodeFile())
                    Close();
            }
            if (GUILayout.Button("取消"))
                Close();
            EditorGUILayout.EndHorizontal();
        }

        private bool CreateNodeFile()
        {
            // 1. 分配子类型 ID(同树类型内扫描最大 + 1)
            long subType = AllocateSubType(_treeKind);

            // 2. 生成类名(英文稳定: CustomNode{subType}, 避免中文/重复)
            var className = $"CustomNode_{subType}";

            // 3. 目录
            string dir = "Assets/BTTrees/Nodes";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 4. 模板代码
            string code = GenerateCode(className, subType, _nodeName, _kind, _treeKind);

            // 5. 写文件
            string path = $"{dir}/{className}.cs";
            if (File.Exists(path))
            {
                if (!EditorUtility.DisplayDialog("覆盖?", $"{className}.cs 已存在, 覆盖?", "覆盖", "取消"))
                    return false;
            }
            File.WriteAllText(path, code);
            AssetDatabase.Refresh();

            Debug.Log($"[BT] 已生成自定义节点 {path} (子类型 #{subType})");
            return true;
        }

        /// <summary>分配子类型 ID: 扫描已注册同类节点的最大 SubType + 1。</summary>
        private long AllocateSubType(BTTreeKind kind)
        {
            long max = -1;
            foreach (var t in BTCustomNodeScanner.AllNodeTypes)
            {
                var node = (BTCustomNode)System.Activator.CreateInstance(t);
                if (node.TreeKind == kind && node.SubType > max)
                    max = node.SubType;
            }
            return max + 1;
        }

        private string GenerateCode(string className, long subType, string nodeName,
            BTCustomNodeKind kind, BTTreeKind treeKind)
        {
            var kindStr = kind == BTCustomNodeKind.Condition ? "Condition"
                        : kind == BTCustomNodeKind.Action ? "Action" : "Composite";
            var treeStr = treeKind == BTTreeKind.Skill ? "Skill" : treeKind == BTTreeKind.AI ? "AI" : "Other";
            var nl = System.Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// ============================================================");
            sb.AppendLine($"// 文件: {className}.cs (自动生成)");
            sb.AppendLine($"// 说明: 自定义行为树节点 - {nodeName} (子类型 #{subType}, {treeStr}树)");
            sb.AppendLine("//       在这里实现 Execute 逻辑");
            sb.AppendLine("// ============================================================");
            sb.AppendLine();
            sb.AppendLine("using HYC.Framework.BT;");
            sb.AppendLine("using Unity.Entities;");
            sb.AppendLine();
            sb.AppendLine("namespace GameBT.Nodes");
            sb.AppendLine("{");
            sb.AppendLine($"    /// <summary>{nodeName} (子类型 #{subType}, {treeStr}树)</summary>");
            sb.AppendLine($"    public class {className} : BTCustomNode");
            sb.AppendLine("    {");
            sb.AppendLine($"        public override long SubType => {subType};");
            sb.AppendLine($"        public override string NodeName => \"{nodeName}\";");
            sb.AppendLine($"        public override BTCustomNodeKind Kind => BTCustomNodeKind.{kindStr};");
            sb.AppendLine($"        public override BTTreeKind TreeKind => BTTreeKind.{treeStr};");
            sb.AppendLine("        public override string Description => \"\";");
            sb.AppendLine();
            sb.AppendLine("        // 参数(编辑器节点上显示输入/下拉), 可选");
            sb.AppendLine("        // public override BTGameNodeParamDesc[] Params => new[]");
            sb.AppendLine("        // {");
            sb.AppendLine("        //     BTGameNodeParamDesc.Float(\"伤害倍率\", 1f),");
            sb.AppendLine("        // };");
            sb.AppendLine();
            sb.AppendLine("        // ===== 在这里写实际逻辑 =====");
            sb.AppendLine("        // ctx.Self: 树挂载的实体(谁在跑这棵树)");
            sb.AppendLine("        // ctx.Blackboard: 黑板(读写变量, 节点间传数据)");
            sb.AppendLine("        // ctx.GameContext.Data: 游戏层注入的世界数据(单位/属性等)");
            sb.AppendLine("        // view.GetFloat(0)/GetLong(0): 节点参数(编辑器配置的)");
            sb.AppendLine("        public override BTNodeState Execute(ref BTContext ctx, ref BTNodeView view)");
            sb.AppendLine("        {");
            sb.AppendLine("            // TODO: 实现逻辑, 返回 BTNodeState.Success / Failed / Running");
            sb.AppendLine("            return BTNodeState.Success;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ToPascalCase(string name)
        {
            var parts = name.Split(new[] { ' ', '_', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Concat(parts);
        }
    }
}
