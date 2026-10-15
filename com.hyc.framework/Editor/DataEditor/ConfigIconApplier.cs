using System;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    /// <summary>
    /// 模板图标 → Unity 原生 Project 窗口传播的唯一入口。
    /// <para>模板 icon 应用到该模板生成类型的 MonoScript（类级），该类型全部配置资产
    /// 经类图标继承显示。Unity 的 <see cref="EditorGUIUtility.SetIconForObject"/> 只接受
    /// GameObject / MonoScript：传 ScriptableObject 实例时图标会记到该实例的类上、
    /// 同类全部资产共享——模板资产全是 ConfigTemplate 类，给任一模板设图标会让所有模板
    /// 与基类字段一起变，因此「单个模板资产自身的原生窗口图标」通道不存在；模板自身的
    /// 图标仅由框架自绘界面消费（数据树行 / 检查器缩略图读模板字段）。</para>
    /// <para>持久化分两层：自定义纹理经 <see cref="MonoImporter.SetIcon"/> + SaveAndReimport
    /// 写进 .cs.meta（跨会话 / 换机 / 进 VCS 有效）；session 图标由打开工程时的验证式同步
    /// 兜底（主要覆盖内置图标名——内置纹理无 GUID 写不进 meta），读回当前图标与目标不一致
    /// 才设，已生效的零操作。</para>
    /// <para>MonoScript 一步独立容错（异常只记日志），生成脚本未编译等场景不中断调用方。</para>
    /// </summary>
    [InitializeOnLoad]
    public static class ConfigIconApplier
    {
        static ConfigIconApplier()
        {
            // 域重载 / 打开工程（含首次打开的新克隆）后 AssetDatabase 就绪时再同步
            EditorApplication.delayCall += SyncAll;
        }

        /// <summary>
        /// 打开工程 / 域重载后：对全部设有图标的模板做验证式同步
        /// （生成类型 MonoScript，不一致才设；session 图标不跨域存活，靠这里恢复）。
        /// </summary>
        public static void SyncAll()
        {
            foreach (var g in AssetDatabase.FindAssets("t:ConfigTemplate"))
            {
                var tpl = AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g));
                if (tpl != null && HasIcon(tpl))
                    ApplyCore(tpl);
            }
            EditorApplication.RepaintProjectWindow();
        }

        /// <summary>保存模板 / 生成代码后：生成类型 MonoScript 即时应用（类级）。</summary>
        public static void ApplyForTemplate(ConfigTemplate tpl)
        {
            if (tpl != null)
                ApplyCore(tpl);
        }

        /// <summary>
        /// 配置资产落地（工厂创建 / 复制粘贴）时按类型反查模板并应用图标。
        /// <para>tpl 为 null 时按类型反查模板；模板无图标时跳过。</para>
        /// </summary>
        public static void ApplyForAsset(ScriptableObject asset, ConfigTemplate tpl = null)
        {
            if (asset == null)
                return;
            tpl = tpl ?? FindTemplateForType(asset.GetType());
            if (tpl == null || !HasIcon(tpl))
                return;
            ApplyCore(tpl);
        }

        /// <summary>按类型反查模板：className 同名，且类型命名空间属于配置命名空间（与资产过滤器同源）。</summary>
        public static ConfigTemplate FindTemplateForType(Type type)
        {
            if (type == null)
                return null;
            var ns = ConfigDataSettings.Namespace;
            if (type.Namespace == null || string.IsNullOrEmpty(ns) || !type.Namespace.StartsWith(ns))
                return null;
            foreach (var g in AssetDatabase.FindAssets("t:ConfigTemplate"))
            {
                var tpl = AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g));
                if (tpl != null && tpl.className == type.Name)
                    return tpl;
            }
            return null;
        }

        /// <summary>
        /// 生成代码成功后：把自定义纹理持久化进 .cs.meta（内置纹理无 GUID，走不了 meta；
        /// 清除图标时以 null 恢复默认）。.meta 已是目标图标则跳过，不白触发重导入。
        /// </summary>
        public static void PersistForTemplate(ConfigTemplate tpl)
        {
            if (tpl == null || string.IsNullOrEmpty(tpl.className))
                return;
            var importer = AssetImporter.GetAtPath($"{ConfigDataSettings.OutputDir}/{tpl.className}.cs") as MonoImporter;
            if (importer == null)
                return;
            var tex = tpl.iconCustom;
            if (importer.GetIcon() == tex)
                return;
            importer.SetIcon(tex);
            importer.SaveAndReimport();
        }

        /// <summary>手动兜底 + 诊断：对全部设有图标的模板立即应用，并报告命中统计。</summary>
        [MenuItem("HYC Framework/Config/Apply Template Icons", false, 5)]
        private static void MenuApplyAll()
        {
            var templates = 0;
            var scripts = 0;
            foreach (var g in AssetDatabase.FindAssets("t:ConfigTemplate"))
            {
                var tpl = AssetDatabase.LoadAssetAtPath<ConfigTemplate>(AssetDatabase.GUIDToAssetPath(g));
                if (tpl == null || !HasIcon(tpl) || !tpl.generateCode)
                    continue;
                templates++;
                scripts += ApplyCore(tpl);
            }
            EditorApplication.RepaintProjectWindow();
            EditorUtility.DisplayDialog("Apply Template Icons",
                $"Templates with icon: {templates}\nMonoScripts matched: {scripts}", "OK");
        }

        // ---------- 内部 ----------

        /// <summary>把模板图标应用到生成类型的 MonoScript；返回命中数（生成脚本未编译 / 未生成时为 0）。</summary>
        private static int ApplyCore(ConfigTemplate tpl)
        {
            // 框架/基类模板（generateCode=false，如 ConfigBase）不参与图标传播：没有生成类型
            // 与配置资产，传播无意义
            if (!tpl.generateCode)
                return 0;

            var tex = Resolve(tpl);

            // 图标唯一有效通道 = MonoScript（类级）。SetIconForObject 只接受 GameObject /
            // MonoScript：传 ScriptableObject 实例时图标记到该实例的类上、同类全部资产共享——
            // 模板资产全是 ConfigTemplate 类，给任一模板设图标会让所有模板一起变。
            // 模板资产自身的图标只由框架自绘界面消费（数据树行 / 检查器缩略图读模板字段）。
            var scripts = 0;
            TryStep($"MonoScript {tpl.className}", () =>
            {
                var script = FindMonoScript(tpl);
                if (script != null)
                {
                    scripts++;
                    SetIfDifferent(script, tex);
                }
            });

            return scripts;
        }

        /// <summary>单步容错：一步异常只记日志，不中断其余步骤（生成脚本未编译等场景）。</summary>
        private static void TryStep(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigIconApplier] 应用图标失败（{what}）：{e}");
            }
        }

        private static void SetIfDifferent(UnityEngine.Object obj, Texture2D tex)
        {
            if (obj == null)
                return;
            if (EditorGUIUtility.GetIconForObject(obj) == tex)
                return;
            EditorGUIUtility.SetIconForObject(obj, tex);
        }

        private static MonoScript FindMonoScript(ConfigTemplate tpl)
        {
            if (tpl == null || string.IsNullOrEmpty(tpl.className))
                return null;
            // 生成主类路径确定（{OutputDir}/{className}.cs）；框架级模板无生成文件，GetAtPath 返回 null
            var importer = AssetImporter.GetAtPath($"{ConfigDataSettings.OutputDir}/{tpl.className}.cs") as MonoImporter;
            return importer?.GetScript();
        }

        private static bool HasIcon(ConfigTemplate tpl)
        {
            return tpl != null && (tpl.iconCustom != null || !string.IsNullOrEmpty(tpl.iconBuiltInName));
        }

        private static Texture2D Resolve(ConfigTemplate tpl)
        {
            return ConfigTemplateIcon.Resolve(tpl) as Texture2D;
        }
    }
}
