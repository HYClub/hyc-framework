using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 「外部配置」里 <c>.inputactions</c> 资产的右栏占位视图：只给「在官方编辑器中打开」这一个入口。
    /// </summary>
    /// <remarks>
    /// <para><b>这里刻意不自建编辑界面</b>，也不是嵌 Inspector —— 三条候选都试过 / 查过，结论如下：</para>
    /// <list type="number">
    /// <item><b>嵌官方编辑窗口（动作表 / 动作 / 绑定的编辑界面）—— 要的就是它，但拿不到。</b>
    /// <c>EditorWindow</c> 的全部公开成员里没有 Embed / Attach / Reparent / Host，
    /// <c>docked</c> 只是只读状态；官方窗口 IMGUI 版 <c>InputActionEditorWindow</c>、
    /// UITK 版 <c>InputActionsEditorWindow</c> 都是 <c>internal</c>（<c>InternalsVisibleTo</c>
    /// 只列 Unity 自家测试程序集）；唯一能改 UI 归属的 <c>rootVisualElement</c> 是 UITK 的
    /// <c>VisualElement</c>，而本数据配置窗口是 IMGUI，渲染不了。</item>
    /// <item><b>嵌原生 Inspector —— 能嵌，但不是要的东西。</b>
    /// 资产级 <c>InputActionAssetEditor</c> 的 <c>OnInspectorGUI()</c> / <c>OnHeaderGUI()</c>
    /// 都是空实现（官方注释：“We want an empty editor in the inspector…”）；
    /// 导入器级 <c>InputActionImporterEditor</c> 虽有内容，但画的是
    /// 「Edit Asset」跳转按钮 + 生成 C# 包装类的导入设置 + <c>ScriptedImporterEditor</c>
    /// 自带的 Asset Post Processors 等与配置编辑无关的控件 —— 属于噪音，不做。</item>
    /// <item><b>自建一套输入动作编辑界面 —— 与官方功能重复演进，包一升级就腐化</b>，不做。</item>
    /// </list>
    /// <para>所以最终只保留一个跳转按钮：用户点了直接进官方编辑器，在那里做真正的编辑。</para>
    /// </remarks>
    public class InputActionsAssetEditor : ConfigDataEditor
    {
        private string mAssetName;
        private string mAssetPath;

        // IMGUI 每帧重绘：样式必须缓存，不能在 OnGUI 里逐条 new
        private static GUIStyle mTitleStyle;
        private static GUIStyle mPathStyle;
        private static GUIStyle mNoteStyle;

        private static GUIStyle TitleStyle
            => mTitleStyle ?? (mTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
            });

        private static GUIStyle PathStyle
            => mPathStyle ?? (mPathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            });

        private static GUIStyle NoteStyle
            => mNoteStyle ?? (mNoteStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            });

        protected override void Init()
        {
            ReadTarget();
        }

        public override void Reload()
        {
            ReadTarget();
        }

        private void ReadTarget()
        {
            var asset = mTreeNode?.GetAsset() as InputActionAsset;
            if (asset == null)
            {
                mAssetName = null;
                mAssetPath = null;
                return;
            }

            mAssetName = asset.name;
            mAssetPath = AssetDatabase.GetAssetPath(asset);
        }

        public override void OnGUI(float viewW, float viewH)
        {
            if (mAssetName == null)
            {
                EditorGUILayout.HelpBox("无法读取该 Input Actions 资产（可能已被删除或类型不匹配）。", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(mAssetName, TitleStyle, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(mAssetPath, PathStyle, GUILayout.ExpandWidth(true));

            EditorGUILayout.Space(12f);

            EditorGUILayout.LabelField(
                "Input Actions 的编辑界面只在 Input System 自己的窗口里：\n"
                + "Unity 没有把那个窗口挂进本面板的公开 API，原生 Inspector 也不是它。\n"
                + "点下面的按钮打开官方编辑器。", NoteStyle, GUILayout.ExpandWidth(true));

            EditorGUILayout.Space(12f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("在官方编辑器中打开", GUILayout.Width(200), GUILayout.Height(30)))
                AssetDatabase.OpenAsset(mTreeNode?.GetAsset());
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }
    }
}
